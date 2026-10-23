using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Serialization.Json;
using Newtonsoft.Json;
using UnityEngine;

namespace PPLua
{
    /// <summary>The mod.json next to a mod's Lua files.</summary>
    public sealed class ModInfo
    {
        public string Name;
        public string Author = "";
        public string Description = "";
        public string Version = "1.0";
        public string Main = "main.lua";
    }

    public enum ModState { Disabled, Running, Error, Stopped }

    /// <summary>
    /// One Lua mod: its sandbox, the items and categories it registered, and the event handlers and timers it
    /// installed. Every call into Lua goes through <see cref="Call"/>, which never lets an exception reach the game.
    /// </summary>
    public sealed class LuaMod
    {
        const int MaxErrorsPerWindow = 20;
        const float ErrorWindowSeconds = 5f;
        const int MaxStorageBytes = 256 * 1024;
        const int RecentLogSize = 30;

        public readonly string Id;
        public readonly string Folder;
        public readonly ModInfo Info;
        public readonly ModMetaData Meta;
        public ModState State { get; private set; } = ModState.Disabled;
        public string LastError { get; private set; }
        public readonly List<string> RecentLog = new List<string>();

        public Sandbox Sandbox { get; private set; }
        public Handles Handles { get; private set; }

        internal readonly List<ItemDef> Items = new List<ItemDef>();
        internal readonly List<CategoryDef> Categories = new List<CategoryDef>();
        internal readonly Dictionary<string, List<DynValue>> Events = new Dictionary<string, List<DynValue>>();
        readonly List<Timer> timers = new List<Timer>();
        readonly Dictionary<string, UnityEngine.Object> assetCache = new Dictionary<string, UnityEngine.Object>();
        int nextTimerId = 1;
        readonly Queue<float> errorTimes = new Queue<float>();
        string pendingStorage;
        float storageDirtySince = -1f;

        internal sealed class ItemDef
        {
            public string Original, Name, Description, Category, Order;
            public Sprite Thumbnail;
            public DynValue BeforeSpawn, AfterSpawn;
        }

        internal sealed class CategoryDef
        {
            public string Name, Description;
            public Sprite Icon;
        }

        sealed class Timer
        {
            public int Id;
            public float Due, Interval;
            public DynValue Fn;
        }

        public LuaMod(string folder, ModInfo info)
        {
            Folder = folder;
            Id = Path.GetFileName(folder.TrimEnd('/', '\\'));
            Info = info;
            if (string.IsNullOrWhiteSpace(Info.Name)) Info.Name = Id;
            Meta = new ModMetaData
            {
                Name = Info.Name,
                Author = Info.Author ?? "",
                Description = Info.Description ?? "",
                ModVersion = Info.Version ?? "1.0",
                MetaLocation = folder,
                Active = true
            };
        }

        public static LuaMod Load(string folder)
        {
            string json = File.ReadAllText(Path.Combine(folder, "mod.json"));
            ModInfo info = JsonConvert.DeserializeObject<ModInfo>(json) ?? new ModInfo();
            return new LuaMod(folder, info);
        }

        public bool Running => State == ModState.Running;

        /// <summary>Creates the sandbox, exposes the API and runs the mod's main file.</summary>
        public void Start(long budget)
        {
            Sandbox = new Sandbox(Folder, Print, budget);
            Handles = new Handles(this);
            Api.Install(this);
            State = ModState.Running;
            try
            {
                Sandbox.RunFile(string.IsNullOrWhiteSpace(Info.Main) ? "main.lua" : Info.Main);
                Plugin.Log.LogInfo($"[{Info.Name}] started");
            }
            catch (Exception e)
            {
                Report("main", e);
                if (State == ModState.Running) Stop(ModState.Error);
            }
        }

        public void Stop(ModState state)
        {
            State = state;
            Events.Clear();
            timers.Clear();
            FlushStorage();
        }

        public void Disable() => State = ModState.Disabled;

        /// <summary>Calls a Lua function; errors are logged and turned into nil.</summary>
        public DynValue Call(DynValue fn, params DynValue[] args)
        {
            if (!Running || fn == null || fn.IsNil()) return DynValue.Nil;
            try
            {
                return Sandbox.Call(fn, args);
            }
            catch (Exception e)
            {
                Report("callback", e);
                return DynValue.Nil;
            }
        }

        /// <summary>Calls every handler this mod registered for an event. Arguments are built only if there are handlers.</summary>
        public void Fire(string evt, Func<DynValue[]> args)
        {
            if (!Running || !Events.TryGetValue(evt, out List<DynValue> handlers) || handlers.Count == 0) return;
            DynValue[] a = args();
            foreach (DynValue fn in handlers.ToArray())
            {
                if (!Running) return;
                Call(fn, a);
            }
        }

        public void On(string evt, DynValue fn)
        {
            if (!Events.TryGetValue(evt, out List<DynValue> list)) Events[evt] = list = new List<DynValue>();
            list.Add(fn);
        }

        public int AddTimer(float delay, float interval, DynValue fn)
        {
            var t = new Timer { Id = nextTimerId++, Due = Time.time + Mathf.Max(0f, delay), Interval = interval, Fn = fn };
            timers.Add(t);
            return t.Id;
        }

        public void CancelTimer(int id) => timers.RemoveAll(t => t.Id == id);

        /// <summary>Clears per-map state: timers belong to the map they were started in.</summary>
        public void OnMapLoad()
        {
            timers.Clear();
            Handles?.Prune();
        }

        public void Tick()
        {
            if (!Running) return;
            float now = Time.time;
            if (timers.Count > 0)
            {
                foreach (Timer t in timers.ToArray())
                {
                    if (!Running) return;
                    if (t.Due > now || !timers.Contains(t)) continue;
                    if (t.Interval > 0f) t.Due = now + t.Interval;
                    else timers.Remove(t);
                    Call(t.Fn);
                }
            }
            if (storageDirtySince >= 0f && Time.unscaledTime - storageDirtySince > 2f) FlushStorage();
        }

        public void Print(string message)
        {
            Plugin.Log.LogInfo($"[{Info.Name}] {message}");
            RecentLog.Add(message);
            if (RecentLog.Count > RecentLogSize) RecentLog.RemoveAt(0);
        }

        void Report(string where, Exception e)
        {
            string message;
            switch (e)
            {
                case InterpreterException ie:
                    message = ie.DecoratedMessage ?? ie.Message;
                    break;
                case LuaBudgetExceededException _:
                    message = e.Message;
                    break;
                default:
                    message = $"{where}: {e.GetType().Name}: {e.Message}";
                    break;
            }
            LastError = message;
            Plugin.Log.LogError($"[{Info.Name}] {message}");
            RecentLog.Add("ERROR " + message);
            if (RecentLog.Count > RecentLogSize) RecentLog.RemoveAt(0);

            if (Sandbox != null && Sandbox.Dead)
            {
                Stop(ModState.Stopped);
                Notify($"Lua mod '{Info.Name}' was stopped: {Sandbox.DeathReason}");
                return;
            }
            float now = Time.unscaledTime;
            errorTimes.Enqueue(now);
            while (errorTimes.Count > 0 && now - errorTimes.Peek() > ErrorWindowSeconds) errorTimes.Dequeue();
            if (errorTimes.Count > MaxErrorsPerWindow)
            {
                Sandbox?.Kill("too many errors");
                Stop(ModState.Stopped);
                Notify($"Lua mod '{Info.Name}' was stopped after repeated errors (F8 for details)");
            }
        }

        static void Notify(string text)
        {
            try { ModAPI.Notify(text); } catch (Exception) { /* no notification UI outside a map */ }
        }

        // ---- files from the mod folder ----

        static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg" };
        static readonly string[] SoundTypes = { ".wav", ".mp3" };

        /// <summary>Resolves a mod-relative asset path, refusing anything outside the folder, of the wrong type, or too big.</summary>
        public string AssetPath(string relative, string[] allowed, long maxBytes, string fn)
        {
            string full = Sandbox.ResolveFile(relative);
            if (full == null) throw new ScriptRuntimeException($"{fn}: '{relative}' is not a path inside the mod folder");
            if (Array.IndexOf(allowed, Path.GetExtension(full).ToLowerInvariant()) < 0)
                throw new ScriptRuntimeException($"{fn}: '{relative}' must be one of {string.Join(", ", allowed)}");
            var file = new FileInfo(full);
            if (!file.Exists) throw new ScriptRuntimeException($"{fn}: '{relative}' not found");
            if (file.Length > maxBytes) throw new ScriptRuntimeException($"{fn}: '{relative}' is too large");
            return full;
        }

        public Texture2D LoadTexture(string relative, bool pixelated)
        {
            string key = "tex:" + pixelated + ":" + relative;
            if (assetCache.TryGetValue(key, out UnityEngine.Object o) && o) return (Texture2D)o;
            string full = AssetPath(relative, ImageTypes, 16 * 1024 * 1024, "texture");
            Texture2D tex = Utils.LoadTexture(full, pixelated ? FilterMode.Point : FilterMode.Bilinear, markNonReadable: false);
            if (!tex) throw new ScriptRuntimeException($"texture: '{relative}' could not be read");
            assetCache[key] = tex;
            return tex;
        }

        public Sprite LoadSprite(string relative, float scale, bool pixelated, Vector2? pivot = null)
        {
            Vector2 p = pivot ?? new Vector2(0.5f, 0.5f);
            string key = "spr:" + scale + ":" + pixelated + ":" + p + ":" + relative;
            if (assetCache.TryGetValue(key, out UnityEngine.Object o) && o) return (Sprite)o;
            Texture2D tex = LoadTexture(relative, pixelated);
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), p, 35f * scale);
            sprite.name = relative;
            assetCache[key] = sprite;
            return sprite;
        }

        public AudioClip LoadSound(string relative)
        {
            string key = "snd:" + relative;
            if (assetCache.TryGetValue(key, out UnityEngine.Object o) && o) return (AudioClip)o;
            string full = AssetPath(relative, SoundTypes, 32 * 1024 * 1024, "sound");
            AudioClip clip;
            try
            {
                clip = Utils.FileToAudioClip(full);
            }
            catch (Exception e)
            {
                throw new ScriptRuntimeException($"sound: '{relative}' could not be decoded ({e.Message})");
            }
            assetCache[key] = clip;
            return clip;
        }

        // ---- persistent storage: one small JSON file per mod, outside the mod folder ----

        string StoragePath => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "PPLua"), "storage", Id + ".json");

        public void SaveStorage(Table data)
        {
            string json = data.TableToJson();
            if (json.Length > MaxStorageBytes) throw new ScriptRuntimeException("ppg.save: data is larger than 256 KB");
            pendingStorage = json;
            if (storageDirtySince < 0f) storageDirtySince = Time.unscaledTime;
        }

        public DynValue LoadStorage()
        {
            string json = pendingStorage;
            if (json == null)
            {
                string path = StoragePath;
                if (!File.Exists(path) || new FileInfo(path).Length > MaxStorageBytes) return DynValue.Nil;
                json = File.ReadAllText(path);
            }
            try
            {
                return DynValue.NewTable(JsonTableConverter.JsonToTable(json, Sandbox.Script));
            }
            catch (Exception)
            {
                return DynValue.Nil;
            }
        }

        public void FlushStorage()
        {
            if (pendingStorage == null) return;
            try
            {
                string path = StoragePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, pendingStorage);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[{Info.Name}] could not write storage: {e.Message}");
            }
            pendingStorage = null;
            storageDirtySince = -1f;
        }
    }
}

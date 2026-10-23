using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MoonSharp.Interpreter;
using UnityEngine;

namespace PPLua
{
    /// <summary>
    /// Loads Lua mods from &lt;game&gt;/LuaMods/&lt;mod&gt;/mod.json and feeds their items into the game's own
    /// mod catalog (ModificationManager), which People Playground still runs even with C# mods switched off.
    /// </summary>
    [BepInPlugin(Guid, "PPLua", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "pplua.loader";
        public const string Version = "0.8.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static readonly List<LuaMod> Mods = new List<LuaMod>();
        public static string ModsFolder { get; private set; }

        ConfigEntry<string> disabledMods;
        ConfigEntry<KeyCode> overlayKey;
        ConfigEntry<long> instructionBudget;

        bool mapLoadPending;
        bool greeted;
        float nextPrune;
        bool showOverlay;
        Rect window = new Rect(40f, 40f, 640f, 520f);
        Vector2 scroll, logScroll;
        LuaMod selected;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            disabledMods = Config.Bind("Mods", "Disabled", "", "Comma-separated folder names of Lua mods that should not run.");
            overlayKey = Config.Bind("UI", "OverlayKey", KeyCode.F8, "Key that shows the Lua mod list.");
            instructionBudget = Config.Bind("Sandbox", "InstructionBudget", Sandbox.DefaultBudget,
                "Lua instructions one callback may run before the mod is stopped as stuck.");

            ModsFolder = Path.Combine(Paths.GameRootPath, "LuaMods");
            Directory.CreateDirectory(ModsFolder);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);     // before LoadMods: some fixes act when scripts compile
            LoadMods();
        }

        void LoadMods()
        {
            var disabled = new HashSet<string>(disabledMods.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.GetDirectories(ModsFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(dir, "mod.json"))) continue;
                LuaMod mod;
                try
                {
                    mod = LuaMod.Load(dir);
                }
                catch (Exception e)
                {
                    Log.LogError($"Skipping {Path.GetFileName(dir)}: bad mod.json ({e.Message})");
                    continue;
                }
                Mods.Add(mod);
                if (disabled.Contains(mod.Id)) continue;
                mod.Start(instructionBudget.Value);
            }
            Log.LogInfo($"{Mods.Count(m => m.Running)} of {Mods.Count} Lua mods running from {ModsFolder}");
        }

        /// <summary>Runs inside CatalogBehaviour.Populate, after the game cleared its own mod registrations.</summary>
        internal void OnCatalogPopulate()
        {
            GameEvents.Subscribe();
            foreach (LuaMod mod in Mods.Where(m => m.Running))
            {
                mod.OnMapLoad();
                foreach (LuaMod.CategoryDef c in mod.Categories)
                {
                    try
                    {
                        if (!ModAPI.FindCategory(c.Name)) ModAPI.RegisterCategory(c.Name, c.Description, c.Icon ? c.Icon : DefaultIcon());
                    }
                    catch (Exception e)
                    {
                        mod.Print($"ERROR category '{c.Name}': {e.Message}");
                    }
                }
                foreach (LuaMod.ItemDef item in mod.Items)
                {
                    try
                    {
                        Register(mod, item);
                    }
                    catch (Exception e)
                    {
                        mod.Print($"ERROR item '{item.Name}': {e.Message}");
                    }
                }
            }
            mapLoadPending = true;
        }

        static void Register(LuaMod mod, LuaMod.ItemDef def)
        {
            SpawnableAsset original = ModAPI.FindSpawnable(def.Original);
            if (!original)
            {
                mod.Print($"ERROR item '{def.Name}': no original item named '{def.Original}'");
                return;
            }
            Category category = def.Category == null ? null : ModAPI.FindCategory(def.Category);
            if (def.Category != null && !category) mod.Print($"item '{def.Name}': no category '{def.Category}', using the original's");

            var modification = new Modification
            {
                OriginalItem = original,
                NameOverride = def.Name,
                DescriptionOverride = def.Description,
                NameToOrderByOverride = def.Order,
                CategoryOverride = category ? category : original.Category,
                ThumbnailOverride = def.Thumbnail,
                AfterSpawn = go =>
                {
                    if (def.AfterSpawn != null) mod.Call(def.AfterSpawn, mod.Handles.Object(go));
                }
            };
            if (def.BeforeSpawn != null) modification.BeforeSpawn = go => mod.Call(def.BeforeSpawn, mod.Handles.Object(go));
            ModificationManager.Modifications.Add(modification, mod.Meta);
        }

        static Sprite defaultIcon;

        static Sprite DefaultIcon()
        {
            if (defaultIcon) return defaultIcon;
            var tex = new Texture2D(32, 32);
            var pixels = new Color32[32 * 32];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 128, 255);
            tex.SetPixels32(pixels);
            tex.Apply();
            defaultIcon = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
            return defaultIcon;
        }

        void Update()
        {
            if (Input.GetKeyDown(overlayKey.Value)) showOverlay = !showOverlay;
            if (!CatalogBehaviour.Main) return;

            if (mapLoadPending)
            {
                mapLoadPending = false;
                foreach (LuaMod mod in Mods) mod.Fire("map_loaded", Array.Empty<DynValue>);
                Greet();
            }
            if (Time.unscaledTime > nextPrune)
            {
                nextPrune = Time.unscaledTime + 10f;
                foreach (LuaMod mod in Mods) mod.Handles?.Prune();
            }
            DynValue[] dt = { DynValue.NewNumber(Time.deltaTime) };
            foreach (LuaMod mod in Mods)
            {
                if (!mod.Running) continue;
                mod.Tick();
                mod.Fire("update", () => dt);
            }
        }

        void FixedUpdate()
        {
            if (!CatalogBehaviour.Main) return;
            DynValue[] dt = { DynValue.NewNumber(Time.fixedDeltaTime) };
            foreach (LuaMod mod in Mods) mod.Fire("fixed_update", () => dt);
        }

        void Greet()
        {
            if (greeted || Mods.Count == 0) return;
            greeted = true;
            int running = Mods.Count(m => m.Running);
            int failed = Mods.Count(m => m.State == ModState.Error || m.State == ModState.Stopped);
            string text = $"PPLua: {running} Lua mod(s) running";
            if (failed > 0) text += $", {failed} failed";
            ModAPI.Notify(text + $" ({overlayKey.Value} for details)");
        }

        void OnApplicationQuit()
        {
            foreach (LuaMod mod in Mods) mod.FlushStorage();
        }

        // ---- overlay ----

        void OnGUI()
        {
            if (showOverlay) window = GUILayout.Window(0x5A1A, window, DrawWindow, $"PPLua {Version} — Lua mods");
        }

        void DrawWindow(int id)
        {
            GUILayout.Label($"Folder: {ModsFolder}");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(240f));
            foreach (LuaMod mod in Mods)
            {
                GUILayout.BeginHorizontal();
                bool enabled = !IsDisabled(mod.Id);
                bool now = GUILayout.Toggle(enabled, "", GUILayout.Width(20f));
                if (now != enabled) SetDisabled(mod.Id, !now);
                if (GUILayout.Button($"{mod.Info.Name} {mod.Info.Version}", GUILayout.Width(300f))) selected = mod;
                GUILayout.Label(StateText(mod));
                GUILayout.EndHorizontal();
            }
            if (Mods.Count == 0) GUILayout.Label("No Lua mods found. Put mod folders (with mod.json) into the folder above.");
            GUILayout.EndScrollView();
            GUILayout.Label("Enabling or disabling a mod takes effect after restarting the game.");

            if (selected != null)
            {
                GUILayout.Label($"{selected.Info.Name} by {selected.Info.Author}: {selected.Info.Description}");
                if (selected.LastError != null) GUILayout.Label("Last error: " + selected.LastError);
                logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(140f));
                foreach (string line in selected.RecentLog) GUILayout.Label(line);
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Close")) showOverlay = false;
            GUI.DragWindow();
        }

        static string StateText(LuaMod mod)
        {
            switch (mod.State)
            {
                case ModState.Running: return "running";
                case ModState.Error: return "failed to start";
                case ModState.Stopped: return "stopped";
                default: return "disabled";
            }
        }

        bool IsDisabled(string id) =>
            disabledMods.Value.Split(',').Any(s => string.Equals(s.Trim(), id, StringComparison.OrdinalIgnoreCase));

        void SetDisabled(string id, bool disabled)
        {
            var list = disabledMods.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && !string.Equals(s, id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (disabled) list.Add(id);
            disabledMods.Value = string.Join(",", list);
        }
    }

    [HarmonyPatch(typeof(CatalogBehaviour), "ApplyModifications")]
    static class ApplyModificationsPatch
    {
        static void Prefix() => Plugin.Instance.OnCatalogPopulate();
    }

    // Limbs a Lua mod made immune skip Damage() entirely (the game calls it past its own immunity flag);
    // limbs set to buffer record every kind of harm instead of taking it.
    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.Damage))]
    static class LimbDamagePatch
    {
        static bool Prefix(LimbBehaviour __instance, float damage) =>
            !Game.Absorb(__instance, damage) && !Game.IsGuarded(__instance);
    }

    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.Shot))]
    static class LimbShotPatch
    {
        static bool Prefix(LimbBehaviour __instance, Shot shot) => !Game.Absorb(__instance, shot.damage);
    }

    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.Stabbed))]
    static class LimbStabbedPatch
    {
        static bool Prefix(LimbBehaviour __instance) => !Game.Absorb(__instance, 10f);
    }

    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.Slice))]
    static class LimbSlicePatch
    {
        static bool Prefix(LimbBehaviour __instance) => !Game.Absorb(__instance, __instance.InitialHealth);
    }

    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.Crush))]
    static class LimbCrushPatch
    {
        static bool Prefix(LimbBehaviour __instance) => !Game.Absorb(__instance, __instance.InitialHealth * 2f);
    }

    [HarmonyPatch(typeof(LimbBehaviour), nameof(LimbBehaviour.BreakBone))]
    static class LimbBreakBonePatch
    {
        static bool Prefix(LimbBehaviour __instance) => !Game.Absorb(__instance, 20f);
    }

    /// <summary>Forwards the game's ModAPI events to Lua handlers. ModAPI drops all subscribers on each map load.</summary>
    static class GameEvents
    {
        public static void Subscribe()
        {
            ModAPI.OnItemSpawned += (s, e) => Fire("spawn", m => new[] { m.Handles.Object(e.Instance), A.S(e.SpawnableAsset ? e.SpawnableAsset.name : null) });
            ModAPI.OnItemRemoved += (s, e) => Fire("removed", m => new[] { m.Handles.Object(e.Instance) });
            ModAPI.OnItemActivated += (s, p) => Fire("activate", m => new[] { m.Handles.Object(p ? p.gameObject : null) });
            ModAPI.OnItemSelected += (s, p) => Fire("select", m => new[] { m.Handles.Object(p ? p.gameObject : null) });
            ModAPI.OnItemDeselected += (s, p) => Fire("deselect", m => new[] { m.Handles.Object(p ? p.gameObject : null) });
            ModAPI.OnGunShot += (s, f) => Fire("gunshot", m => new[] { m.Handles.Object(f ? f.gameObject : null) });
            ModAPI.OnDeath += (s, p) => Fire("death", m => new[] { m.Handles.Person(p) });
        }

        static void Fire(string evt, Func<LuaMod, DynValue[]> args)
        {
            foreach (LuaMod mod in Plugin.Mods) mod.Fire(evt, () => args(mod));
        }
    }
}

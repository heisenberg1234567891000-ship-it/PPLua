using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
using UnityEngine;

namespace PPLua
{
    /// <summary>
    /// Maps Lua values to game objects. A handle is an empty Lua table with a locked metatable; the game object
    /// it stands for lives only on the C# side, keyed by that table's identity. Lua therefore never holds a CLR
    /// reference and cannot forge a handle to something it wasn't given.
    /// </summary>
    public sealed class Handles
    {
        readonly Script script;
        readonly Dictionary<Table, UnityEngine.Object> targets = new Dictionary<Table, UnityEngine.Object>(new ByReference());
        readonly Dictionary<int, Table> wrappers = new Dictionary<int, Table>();

        public readonly Kind<GameObject> Objects;
        public readonly Kind<PersonBehaviour> People;
        public readonly Kind<LimbBehaviour> Limbs;
        public readonly Kind<Sprite> Sprites;
        public readonly Kind<Texture2D> Textures;
        public readonly Kind<AudioClip> Sounds;
        public readonly Kind<LineRenderer> Beams;
        public readonly Kind<AudioSource> Audio;

        public Handles(LuaMod mod)
        {
            script = mod.Sandbox.Script;
            Objects = new Kind<GameObject>(this, script, "object", o => o.name);
            People = new Kind<PersonBehaviour>(this, script, "person", p => p.gameObject.name);
            Limbs = new Kind<LimbBehaviour>(this, script, "limb", l => l.name);
            Sprites = new Kind<Sprite>(this, script, "sprite", s => s.name);
            Textures = new Kind<Texture2D>(this, script, "texture", t => t.name);
            Sounds = new Kind<AudioClip>(this, script, "sound", c => c.name);
            Beams = new Kind<LineRenderer>(this, script, "beam", l => l.gameObject.activeSelf ? "on" : "off");
            Audio = new Kind<AudioSource>(this, script, "audio", a => a.clip ? a.clip.name : "empty");
        }

        public DynValue Wrap<T>(Kind<T> kind, T target) where T : UnityEngine.Object
        {
            if (!target) return DynValue.Nil;
            int id = target.GetInstanceID();
            if (wrappers.TryGetValue(id, out Table t) && targets.TryGetValue(t, out UnityEngine.Object existing) && existing)
                return DynValue.NewTable(t);
            t = new Table(script) { MetaTable = kind.Meta };
            wrappers[id] = t;
            targets[t] = target;
            return DynValue.NewTable(t);
        }

        public DynValue Object(GameObject go) => Wrap(Objects, go);
        public DynValue Person(PersonBehaviour p) => Wrap(People, p);
        public DynValue Limb(LimbBehaviour l) => Wrap(Limbs, l);

        /// <summary>True if the value is a live handle of type T.</summary>
        public bool TryGet<T>(DynValue v, out T target) where T : UnityEngine.Object
        {
            target = null;
            if (v == null || v.Type != DataType.Table || !targets.TryGetValue(v.Table, out UnityEngine.Object o)) return false;
            target = o as T;
            return target;
        }

        public T Get<T>(DynValue v, string fn, string what) where T : UnityEngine.Object
        {
            if (TryGet(v, out T t)) return t;
            if (v != null && v.Type == DataType.Table && targets.ContainsKey(v.Table))
                throw new ScriptRuntimeException($"{fn}: that {what} no longer exists");
            throw new ScriptRuntimeException($"{fn}: expected a {what}, got {(v == null ? "nothing" : v.Type.ToLuaTypeString())}");
        }

        /// <summary>Forgets handles whose objects were destroyed (scene changes, deleted items). Asset handles survive.</summary>
        public void Prune()
        {
            var dead = new List<Table>();
            foreach (KeyValuePair<Table, UnityEngine.Object> pair in targets)
                if (!pair.Value) dead.Add(pair.Key);
            foreach (Table t in dead) targets.Remove(t);
            var deadIds = new List<int>();
            foreach (KeyValuePair<int, Table> pair in wrappers)
                if (!targets.ContainsKey(pair.Value)) deadIds.Add(pair.Key);
            foreach (int id in deadIds) wrappers.Remove(id);
        }

        sealed class ByReference : IEqualityComparer<Table>
        {
            public bool Equals(Table a, Table b) => ReferenceEquals(a, b);
            public int GetHashCode(Table t) => RuntimeHelpers.GetHashCode(t);
        }
    }

    /// <summary>The fields and methods Lua sees on one kind of handle.</summary>
    public sealed class Kind<T> where T : UnityEngine.Object
    {
        public readonly string Name;
        public readonly Table Meta;
        readonly Handles handles;
        readonly Func<T, string> describe;
        readonly Dictionary<string, Func<T, DynValue>> getters = new Dictionary<string, Func<T, DynValue>>();
        readonly Dictionary<string, Action<T, DynValue>> setters = new Dictionary<string, Action<T, DynValue>>();
        readonly Dictionary<string, DynValue> methods = new Dictionary<string, DynValue>();

        public Kind(Handles handles, Script script, string name, Func<T, string> describe)
        {
            this.handles = handles;
            this.describe = describe;
            Name = name;
            Meta = new Table(script);
            Meta.Set("__index", DynValue.NewCallback(Index, name + ".__index"));
            Meta.Set("__newindex", DynValue.NewCallback(NewIndex, name + ".__newindex"));
            Meta.Set("__tostring", DynValue.NewCallback(ToLuaString, name + ".__tostring"));
            Meta.Set("__metatable", DynValue.NewString(name)); // getmetatable() sees a name; setmetatable() is refused
            methods["exists"] = DynValue.NewCallback((ctx, a) => DynValue.NewBoolean(handles.TryGet(a[0], out T _)), name + ":exists");
        }

        public Kind<T> Prop(string key, Func<T, DynValue> get, Action<T, DynValue> set = null)
        {
            getters[key] = get;
            if (set != null) setters[key] = set;
            return this;
        }

        public Kind<T> Method(string key, Func<T, CallbackArguments, DynValue> body)
        {
            string fn = Name + ":" + key;
            methods[key] = DynValue.NewCallback((ctx, args) => body(Self(args[0], fn), args) ?? DynValue.Nil, fn);
            return this;
        }

        public Kind<T> Method(string key, Action<T> body) => Method(key, (t, a) =>
        {
            body(t);
            return DynValue.Nil;
        });

        public IEnumerable<string> Fields => getters.Keys;
        public IEnumerable<string> Methods => methods.Keys;

        T Self(DynValue v, string fn)
        {
            if (handles.TryGet(v, out T t)) return t;
            if (v.Type != DataType.Table) throw new ScriptRuntimeException($"{fn}: call it with ':' (obj:method())");
            throw new ScriptRuntimeException($"{fn}: this {Name} no longer exists");
        }

        DynValue Index(ScriptExecutionContext ctx, CallbackArguments args)
        {
            if (args[1].Type != DataType.String) return DynValue.Nil;
            string key = args[1].String;
            if (methods.TryGetValue(key, out DynValue m)) return m;
            if (getters.TryGetValue(key, out Func<T, DynValue> get)) return get(Self(args[0], Name + "." + key));
            return DynValue.Nil;
        }

        DynValue NewIndex(ScriptExecutionContext ctx, CallbackArguments args)
        {
            string key = args[1].Type == DataType.String ? args[1].String : args[1].ToPrintString();
            if (!setters.TryGetValue(key, out Action<T, DynValue> set))
                throw new ScriptRuntimeException(getters.ContainsKey(key)
                    ? $"{Name}.{key} is read-only"
                    : $"{Name} has no field '{key}'");
            set(Self(args[0], Name + "." + key), args[2]);
            return DynValue.Nil;
        }

        DynValue ToLuaString(ScriptExecutionContext ctx, CallbackArguments args) =>
            DynValue.NewString(handles.TryGet(args[0], out T t) ? $"{Name}({describe(t)})" : $"{Name}(destroyed)");
    }

    /// <summary>Argument and value conversion helpers for API callbacks.</summary>
    static class A
    {
        public static float Num(CallbackArguments a, int i, string fn) => (float)a.AsType(i, fn, DataType.Number, false).Number;

        public static float Num(CallbackArguments a, int i, string fn, float def) =>
            a[i].IsNil() ? def : (float)a.AsType(i, fn, DataType.Number, false).Number;

        public static string Str(CallbackArguments a, int i, string fn) => a.AsType(i, fn, DataType.String, false).String;

        public static string Str(CallbackArguments a, int i, string fn, string def) =>
            a[i].IsNil() ? def : a.AsType(i, fn, DataType.String, false).String;

        public static bool Bool(CallbackArguments a, int i, bool def) => a[i].IsNil() ? def : a[i].CastToBool();

        public static DynValue Fn(CallbackArguments a, int i, string fn) => a.AsType(i, fn, DataType.Function, false);

        public static Table Tbl(CallbackArguments a, int i, string fn) => a.AsType(i, fn, DataType.Table, false).Table;

        public static float F(DynValue v, string what) =>
            (float)(v.CastToNumber() ?? throw new ScriptRuntimeException(what + " must be a number"));

        public static DynValue N(double d) => DynValue.NewNumber(d);
        public static DynValue B(bool b) => DynValue.NewBoolean(b);
        public static DynValue S(string s) => s == null ? DynValue.Nil : DynValue.NewString(s);

        public static string FieldStr(Table t, string key, string def = null)
        {
            DynValue v = t.Get(key);
            if (v.IsNil()) return def;
            if (v.Type != DataType.String && v.Type != DataType.Number) throw new ScriptRuntimeException($"field '{key}' must be a string");
            return v.CastToString();
        }

        public static float FieldNum(Table t, string key, float def)
        {
            DynValue v = t.Get(key);
            return v.IsNil() ? def : F(v, "field '" + key + "'");
        }

        public static bool FieldBool(Table t, string key, bool def)
        {
            DynValue v = t.Get(key);
            return v.IsNil() ? def : v.CastToBool();
        }

        public static DynValue FieldFn(Table t, string key)
        {
            DynValue v = t.Get(key);
            if (v.IsNil()) return null;
            if (v.Type != DataType.Function) throw new ScriptRuntimeException($"field '{key}' must be a function");
            return v;
        }

        public static DynValue List(Script script, IEnumerable<DynValue> items)
        {
            var t = new Table(script);
            foreach (DynValue item in items)
                if (!item.IsNil()) t.Append(item);
            return DynValue.NewTable(t);
        }
    }
}

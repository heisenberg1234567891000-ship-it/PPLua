using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using UnityEngine;
using UnityEngine.Events;
using static PPLua.A;

namespace PPLua
{
    /// <summary>Builds the globals a Lua mod sees: `ppg` (the game API) and `mod` (its own metadata).</summary>
    static class Api
    {
        public static readonly HashSet<string> EventNames = new HashSet<string>
        {
            "map_loaded", "update", "fixed_update", "spawn", "removed", "activate", "death", "gunshot", "select", "deselect"
        };

        static readonly int PhysicsLayers = LayerMask.GetMask("Objects", "CollidingDebris", "Debris");
        static readonly int BoundsLayer = LayerMask.NameToLayer("Bounds");     // the map's walls and floor

        public static void Install(LuaMod mod)
        {
            Script script = mod.Sandbox.Script;
            Handles h = mod.Handles;
            DefineObject(mod, h);
            DefinePerson(h, script);
            DefineLimb(h);
            DefineAssets(h);

            var info = new Table(script);
            info["id"] = mod.Id;
            info["name"] = mod.Info.Name;
            info["author"] = mod.Info.Author ?? "";
            info["version"] = mod.Info.Version ?? "";
            script.Globals["mod"] = info;
            script.Globals["log"] = script.Globals.Get("print");
            script.Globals["ppg"] = Ppg(mod, h, script);
        }

        static Table Ppg(LuaMod mod, Handles h, Script script)
        {
            var ppg = new Table(script);
            void Def(string name, Func<CallbackArguments, DynValue> body) =>
                ppg[name] = DynValue.NewCallback((ctx, a) => body(a) ?? DynValue.Nil, "ppg." + name);

            ppg["version"] = Plugin.Version;
            ppg["game_version"] = Application.version;

            // ---- registration (top level of main.lua; applied every time a map loads) ----

            Def("register_item", a =>
            {
                Table t = Tbl(a, 0, "register_item");
                var def = new LuaMod.ItemDef
                {
                    Original = FieldStr(t, "original") ?? throw new ScriptRuntimeException("register_item: 'original' (an existing item name) is required"),
                    Name = FieldStr(t, "name") ?? throw new ScriptRuntimeException("register_item: 'name' is required"),
                    Description = FieldStr(t, "description", ""),
                    Category = FieldStr(t, "category"),
                    Order = FieldStr(t, "order"),
                    Thumbnail = SpriteArg(mod, h, t.Get("thumbnail"), "register_item"),
                    BeforeSpawn = FieldFn(t, "before_spawn"),
                    AfterSpawn = FieldFn(t, "after_spawn")
                };
                mod.Items.Add(def);
                return null;
            });

            Def("register_category", a =>
            {
                Table t = Tbl(a, 0, "register_category");
                mod.Categories.Add(new LuaMod.CategoryDef
                {
                    Name = FieldStr(t, "name") ?? throw new ScriptRuntimeException("register_category: 'name' is required"),
                    Description = FieldStr(t, "description", ""),
                    Icon = SpriteArg(mod, h, t.Get("icon"), "register_category")
                });
                return null;
            });

            Def("on", a =>
            {
                string evt = Str(a, 0, "on");
                if (!EventNames.Contains(evt))
                    throw new ScriptRuntimeException($"ppg.on: unknown event '{evt}' (known: {string.Join(", ", EventNames)})");
                mod.On(evt, Fn(a, 1, "on"));
                return null;
            });

            Def("after", a => N(mod.AddTimer(Num(a, 0, "after"), 0f, Fn(a, 1, "after"))));
            Def("every", a =>
            {
                float interval = Mathf.Max(0.01f, Num(a, 0, "every"));
                return N(mod.AddTimer(interval, interval, Fn(a, 1, "every")));
            });
            Def("cancel", a =>
            {
                mod.CancelTimer((int)Num(a, 0, "cancel"));
                return null;
            });

            // ---- assets from the mod folder ----

            Def("sprite", a =>
            {
                var pivot = new Vector2(Mathf.Clamp01(Num(a, 3, "sprite", 0.5f)), Mathf.Clamp01(Num(a, 4, "sprite", 0.5f)));
                return h.Wrap(h.Sprites, mod.LoadSprite(Str(a, 0, "sprite"), Num(a, 1, "sprite", 1f), Bool(a, 2, true), pivot));
            });
            Def("texture", a => h.Wrap(h.Textures, mod.LoadTexture(Str(a, 0, "texture"), Bool(a, 1, true))));
            Def("exists", a =>
            {
                string full = mod.Sandbox.ResolveFile(Str(a, 0, "exists"));
                return B(full != null && System.IO.File.Exists(full));
            });
            Def("sound", a => h.Wrap(h.Sounds, mod.LoadSound(Str(a, 0, "sound"))));

            // ---- world ----

            Def("in_map", a => B(CatalogBehaviour.Main));
            Def("notify", a =>
            {
                Game.RequireMap("notify");
                ModAPI.Notify(a[0].ToPrintString());
                return null;
            });
            Def("spawn", a =>
            {
                Game.RequireMap("spawn");
                string name = Str(a, 0, "spawn");
                Vector2 pos = a[1].IsNil() ? (Vector2)Global.main.MousePosition : new Vector2(Num(a, 1, "spawn"), Num(a, 2, "spawn"));
                GameObject go = Game.Spawn(name, pos, Bool(a, 3, false));
                return h.Object(go);
            });
            Def("objects", a =>
            {
                Game.RequireMap("objects");
                return List(script, CatalogBehaviour.SpawnedGameObjects.Where(go => go).Select(h.Object));
            });
            Def("people", a =>
            {
                Game.RequireMap("people");
                return List(script, UnityEngine.Object.FindObjectsOfType<PersonBehaviour>().Select(h.Person));
            });
            Def("find", a =>
            {
                Game.RequireMap("find");
                var at = new Vector2(Num(a, 0, "find"), Num(a, 1, "find"));
                float radius = Mathf.Clamp(Num(a, 2, "find", 1f), 0.01f, 100f);
                IEnumerable<GameObject> hits = Physics2D.OverlapCircleAll(at, radius, PhysicsLayers)
                    .Select(Game.BodyOf).Where(go => go).Distinct();
                return List(script, hits.Select(h.Object));
            });
            Def("raycast", a =>
            {
                Game.RequireMap("raycast");
                var from = new Vector2(Num(a, 0, "raycast"), Num(a, 1, "raycast"));
                var dir = new Vector2(Num(a, 2, "raycast"), Num(a, 3, "raycast"));
                float dist = Mathf.Clamp(Num(a, 4, "raycast", 100f), 0f, 1000f);
                Func<GameObject, bool> ignored = IgnoreFilter(h, a[5], "raycast");
                bool solid = Bool(a, 6, false);
                int mask = solid ? PhysicsLayers | 1 << BoundsLayer : PhysicsLayers;
                foreach (RaycastHit2D hit in Physics2D.RaycastAll(from, dir.normalized, dist, mask).OrderBy(x => x.distance))
                {
                    GameObject go = Game.BodyOf(hit.collider);
                    if (go && !ignored(go)) return DynValue.NewTuple(h.Object(go), N(hit.point.x), N(hit.point.y));
                    // a wall or the floor: no object to hand out, so `false` (nil means nothing was hit)
                    if (!go && hit.collider.gameObject.layer == BoundsLayer) return DynValue.NewTuple(DynValue.False, N(hit.point.x), N(hit.point.y));
                }
                return null;
            });
            Def("beam", a =>
            {
                Game.RequireMap("beam");
                Table t = a[0].IsNil() ? new Table(script) : Tbl(a, 0, "beam");
                var color = new Color(FieldNum(t, "r", 1f), FieldNum(t, "g", 0.1f), FieldNum(t, "b", 0.1f), FieldNum(t, "a", 1f));
                LineRenderer line = Game.CreateBeam(color, Mathf.Clamp(FieldNum(t, "width", 0.05f), 0.001f, 5f),
                    FieldBool(t, "light", false), Mathf.Clamp(FieldNum(t, "light_radius", 2f), 0f, 50f));
                return h.Wrap(h.Beams, line);
            });
            Def("explode", a =>
            {
                Game.RequireMap("explode");
                Table t = Tbl(a, 0, "explode");
                float force = Mathf.Clamp(FieldNum(t, "force", 10f), 0f, 1000f);
                ExplosionCreator.Explode(new ExplosionCreator.ExplosionParameters
                {
                    Position = new Vector3(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)),
                    FragmentForce = force,
                    Range = Mathf.Clamp(FieldNum(t, "range", force * 2.5f), 0f, 200f),
                    FragmentationRayCount = (uint)Mathf.Clamp(FieldNum(t, "fragments", 24f), 0f, 512f),
                    DismemberChance = Mathf.Clamp01(FieldNum(t, "dismember", 0.025f)),
                    BallisticShrapnelCount = (int)Mathf.Clamp(FieldNum(t, "shrapnel", 0f), 0f, 256f),
                    CreateParticlesAndSound = FieldBool(t, "effects", true),
                    EffectSize = FieldBool(t, "big", force > 30f) ? ExplosionCreator.EffectSize.Large : ExplosionCreator.EffectSize.Medium
                });
                return null;
            });
            Def("create", a =>
            {
                Game.RequireMap("create");
                Table t = Tbl(a, 0, "create");
                Sprite sprite = SpriteArg(mod, h, t.Get("sprite"), "create") ?? throw new ScriptRuntimeException("ppg.create: 'sprite' is required");
                GameObject go = Game.Create(sprite, new Vector2(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)), FieldNum(t, "angle", 0f),
                    FieldStr(t, "material", null), FieldBool(t, "flipped", false), FieldBool(t, "exact_collider", true));
                return h.Object(go);
            });
            Def("show", a =>
            {
                Game.RequireMap("show");
                Table t = Tbl(a, 0, "show");
                Sprite sprite = SpriteArg(mod, h, t.Get("sprite"), "show") ?? throw new ScriptRuntimeException("ppg.show: 'sprite' is required");
                GameObject go = Game.Show(sprite, new Vector2(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)),
                    Mathf.Clamp(FieldNum(t, "scale", 1f), 0.01f, 200f), FieldNum(t, "angle", 0f),
                    FieldBool(t, "behind", false), Mathf.Clamp01(FieldNum(t, "alpha", 1f)));
                return h.Object(go);
            });
            Def("emp", a =>
            {
                Game.RequireMap("emp");
                Table t = Tbl(a, 0, "emp");
                return N(Game.Emp(new Vector2(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)),
                    Mathf.Clamp(FieldNum(t, "range", 5f), 0f, 100f), Mathf.Clamp01(FieldNum(t, "chance", 1f))));
            });
            Def("pulse", a =>
            {
                Game.RequireMap("pulse");
                Table t = Tbl(a, 0, "pulse");
                ExplosionCreator.CreatePulseExplosion(new Vector3(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)),
                    Mathf.Clamp(FieldNum(t, "force", 10f), 0f, 1000f), Mathf.Clamp(FieldNum(t, "range", 5f), 0f, 200f),
                    FieldBool(t, "effects", true), FieldBool(t, "break_objects", true));
                return null;
            });
            Def("particle", a =>
            {
                Game.RequireMap("particle");
                ModAPI.CreateParticleEffect(Str(a, 0, "particle"), new Vector2(Num(a, 1, "particle"), Num(a, 2, "particle")));
                return null;
            });
            Def("play_sound", a =>
            {
                Game.RequireMap("play_sound");
                AudioClip clip = h.Get<AudioClip>(a[0], "play_sound", "sound");
                Game.PlaySound(clip, Num(a, 1, "play_sound", 1f));
                return null;
            });

            // ---- catalog lookups ----

            Def("spawnables", a =>
            {
                Game.RequireMap("spawnables");
                string category = Str(a, 0, "spawnables", null);
                IEnumerable<SpawnableAsset> items = CatalogBehaviour.Main.spawnableByName.Values.Distinct();
                if (category != null) items = items.Where(s => s.Category && s.Category.name == category);
                return List(script, items.Select(s => s.name).OrderBy(n => n).Select(n => DynValue.NewString(n)));
            });
            Def("categories", a =>
            {
                Game.RequireMap("categories");
                IEnumerable<Category> all = ModGlobals.MainCatalog.Categories.Concat(CatalogBehaviour.Main.ModdedCatalog);
                return List(script, all.Where(c => c).Select(c => DynValue.NewString(c.name)));
            });
            Def("particles", a => List(script, ModGlobals.LoadedParticleEffects.Keys.OrderBy(k => k).Select(k => DynValue.NewString(k))));
            Def("materials", a => List(script, Resources.LoadAll<PhysicalProperties>("PhysicalProperties")
                .Select(p => p.name).Distinct().OrderBy(n => n).Select(n => DynValue.NewString(n))));

            // ---- input and time ----

            Def("mouse", a =>
            {
                Vector3 m = Global.main ? Global.main.MousePosition : Vector3.zero;
                return DynValue.NewTuple(N(m.x), N(m.y));
            });
            Def("key", a => B(Input.GetKey(Game.Key(Str(a, 0, "key")))));
            Def("key_down", a => B(Input.GetKeyDown(Game.Key(Str(a, 0, "key_down")))));
            Def("key_up", a => B(Input.GetKeyUp(Game.Key(Str(a, 0, "key_up")))));
            Def("mouse_button", a => B(Input.GetMouseButton((int)Mathf.Clamp(Num(a, 0, "mouse_button", 0f), 0f, 2f))));
            Def("time", a => N(Time.time));

            // ---- per-mod storage ----

            Def("save", a =>
            {
                mod.SaveStorage(Tbl(a, 0, "save"));
                return null;
            });
            Def("load", a => mod.LoadStorage());

            return ppg;
        }

        /// <summary>Hits to skip: everything belonging to the given person, or to the given object's whole hierarchy.</summary>
        static Func<GameObject, bool> IgnoreFilter(Handles h, DynValue v, string fn)
        {
            if (v.IsNil()) return go => false;
            if (h.TryGet(v, out PersonBehaviour person)) return go => go.GetComponentInParent<PersonBehaviour>() == person;
            if (h.TryGet(v, out LimbBehaviour limb))
            {
                PersonBehaviour owner = limb.Person;
                return go => go.GetComponentInParent<PersonBehaviour>() == owner;
            }
            GameObject root = h.Get<GameObject>(v, "ppg." + fn, "person or object to ignore").transform.root.gameObject;
            return go => go.transform.root.gameObject == root;
        }

        static Sprite SpriteArg(LuaMod mod, Handles h, DynValue v, string fn)
        {
            if (v.IsNil()) return null;
            if (v.Type == DataType.String) return mod.LoadSprite(v.String, 1f, true);
            return h.Get<Sprite>(v, fn, "sprite");
        }

        // ---- handle kinds ----

        static void DefineObject(LuaMod mod, Handles h)
        {
            Kind<GameObject> k = h.Objects;
            k.Prop("name", go => S(go.name), (go, v) => go.name = v.ToPrintString());
            k.Prop("id", go => N(go.GetInstanceID()));
            k.Prop("spawnable", go =>
            {
                SerialiseInstructions si = go.GetComponent<SerialiseInstructions>();
                return si && si.OriginalSpawnableAsset ? S(si.OriginalSpawnableAsset.name) : DynValue.Nil;
            });
            k.Prop("x", go => N(go.transform.position.x), (go, v) => Game.Move(go, F(v, "x"), go.transform.position.y));
            k.Prop("y", go => N(go.transform.position.y), (go, v) => Game.Move(go, go.transform.position.x, F(v, "y")));
            k.Prop("angle", go => N(go.transform.eulerAngles.z), (go, v) => Game.Rotate(go, F(v, "angle")));
            k.Prop("vx", go => N(Game.Body(go).velocity.x), (go, v) =>
            {
                Rigidbody2D rb = Game.Body(go);
                rb.velocity = new Vector2(F(v, "vx"), rb.velocity.y);
            });
            k.Prop("vy", go => N(Game.Body(go).velocity.y), (go, v) =>
            {
                Rigidbody2D rb = Game.Body(go);
                rb.velocity = new Vector2(rb.velocity.x, F(v, "vy"));
            });
            k.Prop("spin", go => N(Game.Body(go).angularVelocity), (go, v) => Game.Body(go).angularVelocity = F(v, "spin"));
            k.Prop("mass", go => N(Game.Body(go).mass), (go, v) => Game.Body(go).mass = Mathf.Max(0.001f, F(v, "mass")));
            k.Prop("gravity", go => N(Game.Body(go).gravityScale), (go, v) => Game.Body(go).gravityScale = F(v, "gravity"));
            k.Prop("temperature", go => N(Game.Phys(go).Temperature), (go, v) => Game.Phys(go).Temperature = F(v, "temperature"));
            k.Prop("charge", go => N(Game.Phys(go).charge), (go, v) => Game.Phys(go).charge = F(v, "charge"));
            k.Prop("burn", go => N(Game.Phys(go).BurnProgress), (go, v) => Game.Phys(go).BurnProgress = Mathf.Clamp01(F(v, "burn")));
            k.Prop("on_fire", go => B(Game.Phys(go).OnFire));
            k.Prop("wet", go => N(Game.Phys(go).Wetness));
            k.Prop("flipped", go => B(go.transform.lossyScale.x < 0f));
            k.Prop("width", go => N(Game.Bounds(go).size.x));
            k.Prop("height", go => N(Game.Bounds(go).size.y));
            k.Prop("is_person", go => B(go.GetComponentInParent<PersonBehaviour>()));
            k.Prop("visible", go => B(go.activeSelf), (go, v) => go.SetActive(v.CastToBool()));
            k.Prop("local_angle", go => N(go.transform.localEulerAngles.z),
                (go, v) => go.transform.localRotation = Quaternion.Euler(0f, 0f, F(v, "local_angle")));
            k.Prop("bulletproof", go => B(!Game.Phys(go).BulletPenetration), (go, v) => Game.Phys(go).BulletPenetration = !v.CastToBool());
            k.Prop("disintegratable", go => B(Game.Phys(go).Disintegratable), (go, v) => Game.Phys(go).Disintegratable = v.CastToBool());
            k.Prop("softness", go => N(Game.Phys(go).Properties.Softness), (go, v) => Game.SetSoftness(go, F(v, "softness")));
            k.Prop("frozen", go => B(Game.Body(go).bodyType == RigidbodyType2D.Kinematic), (go, v) =>
            {
                Rigidbody2D rb = Game.Body(go);
                bool freeze = v.CastToBool();
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.bodyType = freeze ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            });
            k.Prop("bounciness", go => N(Game.Surface(go) ? Game.Surface(go).bounciness : 0f),
                (go, v) => Game.SetSurface(go, Mathf.Clamp(F(v, "bounciness"), 0f, 2f), null));
            k.Prop("friction", go => N(Game.Surface(go) ? Game.Surface(go).friction : 0.4f),
                (go, v) => Game.SetSurface(go, null, Mathf.Clamp(F(v, "friction"), 0f, 10f)));
            k.Prop("is_static", go =>
            {
                Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
                return B(!rb || rb.bodyType == RigidbodyType2D.Static);
            });
            k.Prop("rendered", go =>
            {
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                return B(sr && sr.enabled);
            }, (go, v) =>
            {
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (sr) sr.enabled = v.CastToBool();
            });

            k.Method("destroy", go => UnityEngine.Object.Destroy(go));
            k.Method("force", (go, a) => Game.Push(go, a, ForceMode2D.Force, "force"));
            k.Method("impulse", (go, a) => Game.Push(go, a, ForceMode2D.Impulse, "impulse"));
            k.Method("torque", (go, a) =>
            {
                Game.Body(go).AddTorque(Num(a, 1, "torque"));
                return null;
            });
            k.Method("ignite", (go, a) =>
            {
                Game.Phys(go).Ignite(Bool(a, 1, false));
                return null;
            });
            k.Method("extinguish", go => Game.Phys(go).Extinguish());
            k.Method("use", go => Game.Phys(go).ForceSendUse());
            k.Method("disintegrate", go => Game.Phys(go).Disintegrate());
            k.Method("weightless", (go, a) =>
            {
                if (Bool(a, 1, true)) Game.Phys(go).MakeWeightless();
                else Game.Phys(go).MakeWeightful();
                return null;
            });
            k.Method("material", (go, a) =>
            {
                string name = Str(a, 1, "material");
                PhysicalProperties props = ModAPI.FindPhysicalProperties(name);
                if (!props) throw new ScriptRuntimeException($"object:material: no material '{name}' (see ppg.materials())");
                Game.Phys(go).Properties = props;
                return null;
            });
            k.Method("set_sprite", (go, a) =>
            {
                Sprite sprite = h.Get<Sprite>(a[1], "object:set_sprite", "sprite");
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (!sr) throw new ScriptRuntimeException("object:set_sprite: this object has no sprite");
                sr.sprite = sprite;
                if (Bool(a, 2, true)) go.FixColliders();
                return null;
            });
            k.Method("color", (go, a) =>
            {
                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                if (!sr) throw new ScriptRuntimeException("object:color: this object has no sprite");
                sr.color = new Color(Num(a, 1, "color"), Num(a, 2, "color"), Num(a, 3, "color"), Num(a, 4, "color", 1f));
                return null;
            });
            k.Method("light", (go, a) =>
            {
                var color = new Color(Num(a, 1, "light"), Num(a, 2, "light"), Num(a, 3, "light"));
                LightSprite light = ModAPI.CreateLight(go.transform, color, Mathf.Clamp(Num(a, 4, "light", 5f), 0f, 100f), Mathf.Clamp(Num(a, 5, "light", 1.5f), 0f, 20f));
                return h.Object(light.gameObject);
            });
            k.Method("sizzle", (go, a) =>
            {
                Game.Phys(go).Sizzle(Bool(a, 1, true));
                return null;
            });
            k.Method("material_sound", (go, a) =>
            {
                PhysicalBehaviour phys = Game.Phys(go);
                string kind = Str(a, 1, "object:material_sound");
                AudioClip[] clips;
                switch (kind)
                {
                    case "sizzle": clips = phys.Properties.SizzleSounds; break;
                    case "soft": clips = phys.Properties.SoftImpact; break;
                    case "hard": clips = phys.Properties.HardImpact; break;
                    case "stab": clips = phys.Properties.StabSound; break;
                    default: throw new ScriptRuntimeException($"object:material_sound: '{kind}' is not one of sizzle, soft, hard, stab");
                }
                if (clips == null || clips.Length == 0) return B(false);
                phys.PlayClipOnce(clips[UnityEngine.Random.Range(0, clips.Length)], Mathf.Clamp01(Num(a, 2, "object:material_sound", 1f)));
                return B(true);
            });
            k.Method("impact", (go, a) =>
            {
                var at = new Vector2(Num(a, 1, "object:impact"), Num(a, 2, "object:impact"));
                var dir = new Vector2(Num(a, 3, "object:impact", 0f), Num(a, 4, "object:impact", 1f));
                Game.Phys(go).CreateImpactEffect(at, dir, Mathf.Clamp(Num(a, 5, "object:impact", 1f), 0.05f, 10f));
                return null;
            });
            k.Method("audio", (go, a) =>
            {
                AudioClip clip = h.Get<AudioClip>(a[1], "object:audio", "sound");
                Table t = a[2].IsNil() ? new Table(mod.Sandbox.Script) : Tbl(a, 2, "object:audio");
                AudioSource source = Game.CreateAudio(go, clip, Mathf.Clamp01(FieldNum(t, "volume", 1f)),
                    Mathf.Clamp(FieldNum(t, "pitch", 1f), 0.05f, 3f), FieldBool(t, "loop", false));
                return h.Wrap(h.Audio, source);
            });
            k.Method("decorate", (go, a) =>
            {
                Sprite sprite = h.Get<Sprite>(a[1], "object:decorate", "sprite");
                Table t = a[2].IsNil() ? new Table(mod.Sandbox.Script) : Tbl(a, 2, "object:decorate");
                GameObject child = Game.Decorate(go, sprite, new Vector2(FieldNum(t, "x", 0f), FieldNum(t, "y", 0f)),
                    FieldNum(t, "angle", 0f), Mathf.Clamp(FieldNum(t, "scale", 1f), 0.01f, 50f), FieldBool(t, "behind", false));
                return h.Object(child);
            });
            k.Method("resize", (go, a) =>
            {
                float sx = Mathf.Clamp(Num(a, 1, "resize"), 0.01f, 50f);
                float sy = Mathf.Clamp(Num(a, 2, "resize", sx), 0.01f, 50f);
                Vector3 s = go.transform.localScale;
                go.transform.localScale = new Vector3(Mathf.Sign(s.x) * sx, sy, s.z);
                return null;
            });
            k.Method("play", (go, a) =>
            {
                Game.Phys(go).PlayClipOnce(h.Get<AudioClip>(a[1], "object:play", "sound"), Num(a, 2, "play", 1f));
                return null;
            });
            k.Method("weld", (go, a) =>
            {
                GameObject other = h.Get<GameObject>(a[1], "object:weld", "object");
                var joint = go.AddComponent<FixedJoint2D>();
                joint.connectedBody = Game.Body(other);
                return null;
            });
            k.Method("button", (go, a) =>
            {
                string label = Str(a, 1, "button");
                DynValue fn = Fn(a, 2, "button");
                string description = Str(a, 3, "button", label);
                PhysicalBehaviour phys = Game.Phys(go);
                if (!phys.ContextMenuOptions) phys.ContextMenuOptions = go.GetOrAddComponent<ContextMenuOptionComponent>();
                string identity = "pplua." + mod.Id + "." + label;
                phys.ContextMenuOptions.Buttons.RemoveAll(b => b.Identity == identity);
                phys.ContextMenuOptions.Buttons.Add(new ContextMenuButton(identity, label, description,
                    new UnityAction(() => mod.Call(fn, h.Object(go)))));
                return null;
            });
            k.Method("on_use", (go, a) => SetHook(mod, go, a, "on_use", (b, f) => b.OnUse = f));
            k.Method("on_update", (go, a) => SetHook(mod, go, a, "on_update", (b, f) => b.OnUpdate = f));
            k.Method("on_collide", (go, a) => SetHook(mod, go, a, "on_collide", (b, f) => b.OnCollide = f));
            k.Method("on_stab", (go, a) => SetHook(mod, go, a, "on_stab", (b, f) => b.OnStab = f));
            k.Method("on_shot", (go, a) => SetHook(mod, go, a, "on_shot", (b, f) => b.OnShot = f));
            k.Method("hinge", (go, a) =>
            {
                GameObject other = h.Get<GameObject>(a[1], "object:hinge", "object");
                Vector2 at = a[2].IsNil() ? (Vector2)go.transform.position : new Vector2(Num(a, 2, "object:hinge"), Num(a, 3, "object:hinge"));
                float? lower = a[4].IsNil() ? (float?)null : Num(a, 4, "object:hinge");
                float? upper = a[5].IsNil() ? (float?)null : Num(a, 5, "object:hinge");
                Game.Hinge(go, other, at, lower, upper);
                return null;
            });
            k.Method("ignore_collision", (go, a) =>
            {
                IEnumerable<GameObject> others = h.TryGet(a[1], out PersonBehaviour p)
                    ? p.Limbs.Where(l => l).Select(l => l.gameObject)
                    : new[] { h.Get<GameObject>(a[1], "object:ignore_collision", "object or person") };
                Game.IgnoreCollision(go, others);
                return null;
            });
            k.Method("bind_to", (go, a) =>
            {
                Game.BindTo(go, h.Get<GameObject>(a[1], "object:bind_to", "object"));
                return null;
            });
            k.Method("person", (go, a) => h.Person(go.GetComponentInParent<PersonBehaviour>()));
            k.Method("limb", (go, a) => h.Limb(go.GetComponent<LimbBehaviour>()));
            k.Method("parts", (go, a) => List(mod.Sandbox.Script,
                go.GetComponentsInChildren<PhysicalBehaviour>().Select(p => h.Object(p.gameObject))));
        }

        static DynValue SetHook(LuaMod mod, GameObject go, CallbackArguments a, string fn, Action<LuaObjectBehaviour, DynValue> set)
        {
            DynValue f = a[1].IsNil() ? null : Fn(a, 1, "object:" + fn);
            set(LuaObjectBehaviour.For(go, mod), f);
            return null;
        }

        static void DefinePerson(Handles h, Script script)
        {
            Kind<PersonBehaviour> k = h.People;
            k.Prop("alive", p => B(p.IsAlive()));
            k.Prop("consciousness", p => N(p.Consciousness), (p, v) => p.Consciousness = Mathf.Clamp01(F(v, "consciousness")));
            k.Prop("pain", p => N(p.PainLevel), (p, v) => p.PainLevel = Mathf.Max(0f, F(v, "pain")));
            k.Prop("shock", p => N(p.ShockLevel), (p, v) => p.ShockLevel = Mathf.Max(0f, F(v, "shock")));
            k.Prop("adrenaline", p => N(p.AdrenalineLevel), (p, v) => p.AdrenalineLevel = Mathf.Max(0f, F(v, "adrenaline")));
            k.Prop("oxygen", p => N(p.OxygenLevel), (p, v) => p.OxygenLevel = Mathf.Clamp01(F(v, "oxygen")));
            k.Prop("braindead", p => B(p.Braindead), (p, v) => p.Braindead = v.CastToBool());
            k.Prop("walk", p => N(p.DesiredWalkingDirection), (p, v) => p.DesiredWalkingDirection = Mathf.Clamp(F(v, "walk"), -1f, 1f));
            k.Prop("pose", p => p.OverridePoseIndex >= 0 && p.Poses.Count > 0 ? S(p.Poses[p.OverridePoseIndex % p.Poses.Count].Name) : DynValue.Nil,
                (p, v) =>
                {
                    if (v.IsNil())
                    {
                        p.OverridePoseIndex = -1;
                        return;
                    }
                    string name = v.ToPrintString();
                    int index = p.Poses.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                        throw new ScriptRuntimeException($"person.pose: no pose '{name}' (have: {string.Join(", ", p.Poses.Select(x => x.Name))})");
                    p.OverridePoseIndex = index;
                });

            k.Prop("time_scale", p => N(p.GetComponent<TimeWarp>() ? p.GetComponent<TimeWarp>().Scale : 1f),
                (p, v) => TimeWarp.For(p).Scale = Mathf.Clamp(F(v, "time_scale"), 0.05f, 20f));
            k.Prop("seizure", p => N(p.SeizureTime), (p, v) => p.SeizureTime = Mathf.Max(0f, F(v, "seizure")));

            k.Method("pose_names", (p, a) => List(script, p.Poses.Select(x => DynValue.NewString(x.Name))));
            k.Method("blueprint", p =>
            {
                Blueprint.Take(p);
            });
            k.Method("rot_color", (p, a) =>
            {
                p.SetRottenColour(Byte(a, 1), Byte(a, 2), Byte(a, 3));
                return null;
            });
            k.Method("held", (p, a) => List(script, p.Limbs.Where(l => l)
                .Select(l => l.GetComponent<GripBehaviour>())
                .Where(g => g && g.isHolding && g.CurrentlyHolding)
                .Select(g => h.Object(g.CurrentlyHolding.gameObject))));
            k.Method("paint", (p, a) =>
            {
                Table t = Tbl(a, 1, "person:paint");
                var overlays = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
                foreach (TablePair pair in t.Pairs)
                {
                    if (pair.Key.Type != DataType.String) throw new ScriptRuntimeException("person:paint: keys must be limb names or \"*\"");
                    overlays[pair.Key.String] = h.Get<Texture2D>(pair.Value, "person:paint", "texture");
                }
                Game.Paint(p, overlays);
                return null;
            });
            k.Method("object", (p, a) => h.Object(p.gameObject));
            k.Method("limbs", (p, a) => List(script, p.Limbs.Where(l => l).Select(h.Limb)));
            k.Method("limb", (p, a) =>
            {
                string name = Str(a, 1, "person:limb");
                return h.Limb(p.Limbs.FirstOrDefault(l => l && string.Equals(l.name, name, StringComparison.OrdinalIgnoreCase)));
            });
            k.Method("kill", p =>
            {
                foreach (LimbBehaviour limb in p.Limbs)
                {
                    if (!limb) continue;
                    limb.Health = 0f;
                    if (limb.CirculationBehaviour) limb.CirculationBehaviour.IsPump = false;
                }
            });
            k.Method("heal", p =>
            {
                p.Braindead = false;
                p.BrainDamaged = false;
                p.BrainDamagedTime = 0f;
                p.SeizureTime = 0f;
                foreach (LimbBehaviour limb in p.Limbs)
                    if (limb) Game.Heal(limb);
            });
            k.Method("blood_color", (p, a) =>
            {
                p.SetBloodColour(Byte(a, 1), Byte(a, 2), Byte(a, 3));
                return null;
            });
            k.Method("skin", (p, a) =>
            {
                Texture2D skin = h.Get<Texture2D>(a[1], "person:skin", "texture");
                Texture2D flesh = a[2].IsNil() ? null : h.Get<Texture2D>(a[2], "person:skin", "texture");
                Texture2D bone = a[3].IsNil() ? null : h.Get<Texture2D>(a[3], "person:skin", "texture");
                p.SetBodyTextures(skin, flesh, bone, Num(a, 4, "person:skin", 1f));
                return null;
            });
        }

        static byte Byte(CallbackArguments a, int i) => (byte)Mathf.Clamp(Num(a, i, "color"), 0f, 255f);

        static void DefineLimb(Handles h)
        {
            Kind<LimbBehaviour> k = h.Limbs;
            k.Prop("name", l => S(l.name));
            k.Prop("health", l => N(l.Health), (l, v) => l.Health = F(v, "health"));
            k.Prop("max_health", l => N(l.InitialHealth));
            k.Prop("broken", l => B(l.Broken));
            k.Prop("dismembered", l => B(l.IsDismembered));
            k.Prop("has_brain", l => B(l.HasBrain));
            k.Prop("numbness", l => N(l.Numbness), (l, v) => l.Numbness = Mathf.Clamp01(F(v, "numbness")));
            k.Prop("immune", l => B(l.ImmuneToDamage), (l, v) =>
            {
                l.ImmuneToDamage = v.CastToBool();
                if (l.CirculationBehaviour) l.CirculationBehaviour.ImmuneToDamage = l.ImmuneToDamage;
                Game.Guard(l, l.ImmuneToDamage);
            });
            k.Prop("zombie", l => B(l.IsZombie), (l, v) => l.IsZombie = v.CastToBool());
            k.Prop("frozen", l => B(l.Frozen), (l, v) => l.Frozen = v.CastToBool());
            k.Prop("acid", l => N(l.SkinMaterialHandler.AcidProgress), (l, v) => l.SkinMaterialHandler.AcidProgress = Mathf.Clamp01(F(v, "acid")));
            k.Prop("rot", l => N(l.SkinMaterialHandler.RottenProgress), (l, v) => l.SkinMaterialHandler.RottenProgress = Mathf.Clamp01(F(v, "rot")));
            k.Prop("connected", l => B(l.NodeBehaviour && l.NodeBehaviour.IsConnectedToRoot));
            k.Prop("buffer", l => B(Game.IsBuffering(l)), (l, v) => Game.SetBuffering(l, v.CastToBool()));
            k.Prop("buffered", l => N(Game.Buffered(l)));
            k.Method("take_buffer", (l, a) => N(Game.TakeBuffer(l)));
            k.Prop("disintegrated", l => B(l.PhysicalBehaviour.isDisintegrated || !l.gameObject.activeSelf));
            k.Prop("joint_gap", l => Blueprint.Gap(l) is float g && g >= 0f ? N(g) : DynValue.Nil);
            k.Method("parent", (l, a) => h.Limb(Blueprint.ParentOf(l)));
            k.Method("rest_pose", (l, a) => Blueprint.RestPose(l, out Vector2 p, out float angle, out Vector2 socket)
                ? DynValue.NewTuple(N(p.x), N(p.y), N(angle), N(socket.x), N(socket.y)) : DynValue.Nil);
            k.Method("reattach", (l, a) => B(Blueprint.Reattach(l)));
            k.Method("detach", (l, a) => B(Blueprint.Detach(l)));
            k.Method("restore", (l, a) => B(Blueprint.Restore(l)));
            k.Method("clear_damage", l =>
            {
                if (l.SkinMaterialHandler) l.SkinMaterialHandler.ClearAllDamage();
                l.PhysicalBehaviour.ClearAllDecals();
            });
            k.Prop("strength", l => N(l.BaseStrength), (l, v) => l.BaseStrength = Mathf.Clamp(F(v, "strength"), 0f, 100f));
            k.Prop("toughness", l => N(l.BreakingThreshold), (l, v) => l.BreakingThreshold = Mathf.Max(0f, F(v, "toughness")));
            k.Prop("regeneration", l => N(l.RegenerationSpeed), (l, v) => l.RegenerationSpeed = Mathf.Max(0f, F(v, "regeneration")));
            k.Prop("shot_damage", l => N(l.ShotDamageMultiplier), (l, v) => l.ShotDamageMultiplier = Mathf.Max(0f, F(v, "shot_damage")));

            k.Method("skin_size", (l, a) =>
            {
                Rect r = l.SkinMaterialHandler.renderer.sprite.rect;
                return DynValue.NewTuple(N(r.width), N(r.height));
            });
            k.Method("object", (l, a) => h.Object(l.gameObject));
            k.Method("person", (l, a) => h.Person(l.Person));
            k.Method("break_bone", l => l.BreakBone());
            k.Method("heal_bone", l => l.HealBone());
            k.Method("slice", l => l.Slice());
            k.Method("crush", l => l.Crush());
            k.Method("heal", l => Game.Heal(l));
            k.Method("damage", (l, a) =>
            {
                l.Damage(Num(a, 1, "limb:damage"));
                return null;
            });
        }

        static void DefineAssets(Handles h)
        {
            h.Sprites.Prop("width", s => N(s.rect.width)).Prop("height", s => N(s.rect.height));
            h.Textures.Prop("width", t => N(t.width)).Prop("height", t => N(t.height));
            h.Sounds.Prop("length", c => N(c.length));

            Kind<LineRenderer> beams = h.Beams;
            beams.Prop("visible", l => B(l.gameObject.activeSelf), (l, v) => l.gameObject.SetActive(v.CastToBool()));
            beams.Prop("width", l => N(l.startWidth), (l, v) => l.startWidth = l.endWidth = Mathf.Clamp(F(v, "width"), 0.001f, 5f));
            beams.Method("set", (l, a) =>
            {
                Game.SetBeam(l, new Vector2(Num(a, 1, "beam:set"), Num(a, 2, "beam:set")), new Vector2(Num(a, 3, "beam:set"), Num(a, 4, "beam:set")));
                return null;
            });
            beams.Method("path", (l, a) =>
            {
                Table t = Tbl(a, 1, "beam:path");
                int n = t.Length / 2;
                if (n < 2 || n > 256) throw new ScriptRuntimeException("beam:path: give 2 to 256 points as {x1, y1, x2, y2, ...}");
                var points = new Vector3[n];
                for (int i = 0; i < n; i++) points[i] = new Vector3(F(t.Get(2 * i + 1), "beam:path x"), F(t.Get(2 * i + 2), "beam:path y"), 0f);
                l.positionCount = n;
                l.SetPositions(points);
                LightSprite light = l.GetComponentInChildren<LightSprite>(true);
                if (light) light.transform.position = points[n - 1];
                return null;
            });
            beams.Method("color", (l, a) =>
            {
                l.startColor = l.endColor = new Color(Num(a, 1, "beam:color"), Num(a, 2, "beam:color"), Num(a, 3, "beam:color"), Num(a, 4, "beam:color", 1f));
                return null;
            });
            beams.Method("destroy", l => UnityEngine.Object.Destroy(l.gameObject));

            Kind<AudioSource> audio = h.Audio;
            audio.Prop("playing", s => B(s.isPlaying));
            audio.Prop("length", s => N(s.clip ? s.clip.length : 0f));
            audio.Prop("time", s => N(s.time), (s, v) =>
            {
                if (s.clip) s.time = Mathf.Clamp(F(v, "time"), 0f, Mathf.Max(0f, s.clip.length - 0.01f));
            });
            audio.Prop("volume", s => N(s.volume), (s, v) => s.volume = Mathf.Clamp01(F(v, "volume")));
            audio.Prop("pitch", s => N(s.pitch), (s, v) => s.pitch = Mathf.Clamp(F(v, "pitch"), 0.05f, 3f));
            audio.Prop("loop", s => B(s.loop), (s, v) => s.loop = v.CastToBool());
            audio.Method("play", (s, a) =>
            {
                s.Play();
                if (!a[1].IsNil() && s.clip) s.time = Mathf.Clamp(Num(a, 1, "audio:play"), 0f, Mathf.Max(0f, s.clip.length - 0.01f));
                return null;
            });
            audio.Method("stop", s => s.Stop());
            audio.Method("destroy", s => UnityEngine.Object.Destroy(s.gameObject));
        }
    }
}

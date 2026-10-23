using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using UnityEngine;

namespace PPLua
{
    /// <summary>Small wrappers over People Playground internals that the Lua API needs.</summary>
    static class Game
    {
        static readonly Dictionary<string, KeyCode> keys = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase);

        public static void RequireMap(string fn)
        {
            if (!CatalogBehaviour.Main) throw new ScriptRuntimeException($"ppg.{fn}: only available while a map is loaded (use ppg.on('map_loaded', ...))");
        }

        /// <summary>Spawns a catalog item the way the game does when the player places it.</summary>
        public static GameObject Spawn(string name, Vector2 position, bool flipped)
        {
            SpawnableAsset asset = CatalogBehaviour.Main.GetSpawnable(name);
            if (!asset) throw new ScriptRuntimeException($"ppg.spawn: no item named '{name}' (see ppg.spawnables())");

            GameObject go = asset.InstantiateOverride != null
                ? asset.InstantiateOverride(position, ref flipped)
                : UnityEngine.Object.Instantiate(asset.Prefab, position, Quaternion.identity);
            if (!go) throw new ScriptRuntimeException($"ppg.spawn: '{name}' could not be created");

            CatalogBehaviour.PerformBeforeSpawn(asset, go);
            if (flipped)
            {
                Vector3 scale = go.transform.localScale;
                scale.x *= -1f;
                go.transform.localScale = scale;
            }
            go.AddComponent<DeregisterBehaviour>();
            go.AddComponent<TexturePackApplier>();
            go.AddComponent<AudioSourceTimeScaleBehaviour>();
            go.AddComponent<SerialiseInstructions>().OriginalSpawnableAsset = asset;
            go.name = asset.name;
            CatalogBehaviour.SpawnedGameObjects.Add(go);
            UndoControllerBehaviour.RegisterAction(new ObjectCreationAction(go));
            CatalogBehaviour.PerformAfterSpawn(asset, go);
            return go;
        }

        public static Rigidbody2D Body(GameObject go)
        {
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (!rb) throw new ScriptRuntimeException($"'{go.name}' has no physics body (for people, use a limb's object)");
            return rb;
        }

        public static PhysicalBehaviour Phys(GameObject go)
        {
            PhysicalBehaviour p = go.GetComponent<PhysicalBehaviour>();
            if (!p) throw new ScriptRuntimeException($"'{go.name}' is not a physical object (for people, use a limb's object)");
            return p;
        }

        /// <summary>The spawned object a collider belongs to, or null for scenery.</summary>
        public static GameObject BodyOf(Collider2D c)
        {
            if (!c) return null;
            GameObject go = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;
            return go.GetComponent<PhysicalBehaviour>() ? go : null;
        }

        public static Bounds Bounds(GameObject go)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr) return sr.bounds;
            Collider2D c = go.GetComponent<Collider2D>();
            return c ? c.bounds : new Bounds(go.transform.position, Vector3.zero);
        }

        /// <summary>
        /// How easily blades and needles go in: the game lets a sharp object lodge only when the target's
        /// material is softer than 0.01. Materials are shared assets, so the object gets its own copy first.
        /// </summary>
        public static void SetSoftness(GameObject go, float softness)
        {
            PhysicalBehaviour phys = Phys(go);
            if (!phys.Properties) return;
            PhysicalProperties own = UnityEngine.Object.Instantiate(phys.Properties);
            own.name = phys.Properties.name;
            own.Softness = Mathf.Clamp01(softness);
            phys.Properties = own;
        }

        public static void Move(GameObject go, float x, float y)
        {
            var p = new Vector3(x, y, go.transform.position.z);
            go.transform.position = p;
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb) rb.position = p;
        }

        public static void Rotate(GameObject go, float degrees)
        {
            go.transform.rotation = Quaternion.Euler(0f, 0f, degrees);
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            if (rb) rb.rotation = degrees;
        }

        public static DynValue Push(GameObject go, CallbackArguments a, ForceMode2D mode, string fn)
        {
            Rigidbody2D rb = Body(go);
            var f = new Vector2(A.Num(a, 1, fn), A.Num(a, 2, fn));
            if (a[3].IsNil()) rb.AddForce(f, mode);
            else rb.AddForceAtPosition(f, new Vector2(A.Num(a, 3, fn), A.Num(a, 4, fn)), mode);
            return DynValue.Nil;
        }

        /// <summary>Same reset the life syringe applies to a limb, minus its gradual burn/wound fading.</summary>
        public static void Heal(LimbBehaviour limb)
        {
            limb.HealBone();
            limb.Health = limb.InitialHealth;
            limb.Numbness = 0f;
            limb.BruiseCount = 0;
            limb.LungsPunctured = false;
            CirculationBehaviour c = limb.CirculationBehaviour;
            if (c)
            {
                c.HealBleeding();
                c.IsPump = c.WasInitiallyPumping;
                c.BloodFlow = 1f;
                Liquid blood = limb.GetOriginalBloodType();
                c.AddLiquid(blood, Mathf.Max(0f, 1f - c.GetAmount(blood)));
                c.GunshotWoundCount = 0;
                c.StabWoundCount = 0;
            }
            if (limb.PhysicalBehaviour)
            {
                limb.PhysicalBehaviour.Temperature = limb.BodyTemperature;
                limb.PhysicalBehaviour.Extinguish();
            }
            if (limb.RoughClassification == LimbBehaviour.BodyPart.Head && limb.Person)
            {
                limb.Person.Consciousness = 1f;
                limb.Person.ShockLevel = 0f;
                limb.Person.PainLevel = 0f;
                limb.Person.OxygenLevel = 1f;
            }
        }

        public static void PlaySound(AudioClip clip, float volume)
        {
            var go = new GameObject("PPLua sound");
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.volume = Mathf.Clamp01(volume);
            source.clip = clip;
            source.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.1f);
        }

        // ---- immunity ----

        // LimbBehaviour.Damage ignores ImmuneToDamage (explosions call it directly), so limbs a mod made
        // immune are also shielded there by a Harmony prefix. Vanilla limbs are left alone.
        static readonly HashSet<int> guarded = new HashSet<int>();

        public static void Guard(LimbBehaviour limb, bool on)
        {
            if (on) guarded.Add(limb.GetInstanceID());
            else guarded.Remove(limb.GetInstanceID());
        }

        public static bool IsGuarded(LimbBehaviour limb) => guarded.Count > 0 && guarded.Contains(limb.GetInstanceID());

        // ---- damage buffering (limb.buffer): harm is counted instead of applied, for stasis-style effects ----

        static readonly Dictionary<int, float> buffers = new Dictionary<int, float>();

        public static void SetBuffering(LimbBehaviour limb, bool on)
        {
            int id = limb.GetInstanceID();
            if (on && !buffers.ContainsKey(id)) buffers[id] = 0f;
            else if (!on) buffers.Remove(id);
        }

        public static bool IsBuffering(LimbBehaviour limb) => buffers.Count > 0 && buffers.ContainsKey(limb.GetInstanceID());

        public static float Buffered(LimbBehaviour limb) => buffers.TryGetValue(limb.GetInstanceID(), out float v) ? v : 0f;

        /// <summary>Adds harm to a buffering limb. Returns false (nothing recorded) if the limb is not buffering.</summary>
        public static bool Absorb(LimbBehaviour limb, float amount)
        {
            int id = limb.GetInstanceID();
            if (buffers.Count == 0 || !buffers.TryGetValue(id, out float v)) return false;
            buffers[id] = v + Mathf.Max(0f, amount);
            return true;
        }

        public static float TakeBuffer(LimbBehaviour limb)
        {
            int id = limb.GetInstanceID();
            if (!buffers.TryGetValue(id, out float v)) return 0f;
            buffers[id] = 0f;
            return v;
        }

        // ---- objects made by mods ----

        /// <summary>A free-standing physical object from a mod sprite (shards, prostheses, droplets). Not saved.</summary>
        public static GameObject Create(Sprite sprite, Vector2 position, float angle, string material, bool flipped, bool exactCollider)
        {
            GameObject go = ModAPI.CreatePhysicalObject("PPLua object", sprite);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            if (flipped) go.transform.localScale = new Vector3(-1f, 1f, 1f);
            if (exactCollider) go.FixColliders();
            PhysicalBehaviour phys = go.GetComponent<PhysicalBehaviour>();
            if (material != null)
            {
                PhysicalProperties props = ModAPI.FindPhysicalProperties(material);
                if (!props) throw new ScriptRuntimeException($"ppg.create: no material '{material}' (see ppg.materials())");
                phys.Properties = props;
            }
            phys.RecalculateMassBasedOnSize();
            return go;
        }

        /// <summary>What the game's EMP device does: electronics in range receive OnEMPHit.</summary>
        public static int Emp(Vector2 center, float range, float chance)
        {
            float r2 = range * range;
            int hit = 0;
            foreach (PhysicalBehaviour item in Global.main.PhysicalObjectsInWorld.ToArray())
            {
                if (!item || ((Vector2)item.transform.position - center).sqrMagnitude > r2 || UnityEngine.Random.value > chance) continue;
                item.SendMessage("OnEMPHit", SendMessageOptions.DontRequireReceiver);
                hit++;
            }
            return hit;
        }

        public static HingeJoint2D Hinge(GameObject go, GameObject other, Vector2 worldAnchor, float? lower, float? upper)
        {
            var joint = go.AddComponent<HingeJoint2D>();
            joint.connectedBody = Body(other);
            joint.anchor = go.transform.InverseTransformPoint(worldAnchor);
            joint.autoConfigureConnectedAnchor = true;
            if (lower.HasValue && upper.HasValue)
            {
                joint.useLimits = true;
                joint.limits = new JointAngleLimits2D { min = lower.Value, max = upper.Value };
            }
            return joint;
        }

        public static void IgnoreCollision(GameObject a, IEnumerable<GameObject> others)
        {
            Collider2D[] mine = a.GetComponentsInChildren<Collider2D>();
            foreach (GameObject other in others)
            {
                if (!other) continue;
                foreach (Collider2D theirs in other.GetComponentsInChildren<Collider2D>())
                foreach (Collider2D c in mine)
                    Physics2D.IgnoreCollision(c, theirs, true);
            }
        }

        public static void BindTo(GameObject child, GameObject parent)
        {
            LuaBinder binder = parent.GetComponent<LuaBinder>() ?? parent.AddComponent<LuaBinder>();
            if (!binder.Linked.Contains(child)) binder.Linked.Add(child);
        }

        /// <summary>Gives an object its own physics material so bounciness/friction changes don't leak to others.</summary>
        public static void SetSurface(GameObject go, float? bounciness, float? friction)
        {
            Rigidbody2D rb = Body(go);
            Collider2D[] colliders = go.GetComponents<Collider2D>();
            PhysicsMaterial2D current = colliders.Length > 0 && colliders[0].sharedMaterial ? colliders[0].sharedMaterial : rb.sharedMaterial;
            var material = new PhysicsMaterial2D("PPLua surface")
            {
                bounciness = bounciness ?? (current ? current.bounciness : 0f),
                friction = friction ?? (current ? current.friction : 0.4f)
            };
            rb.sharedMaterial = material;
            foreach (Collider2D c in colliders) c.sharedMaterial = material;
        }

        public static PhysicsMaterial2D Surface(GameObject go)
        {
            Collider2D c = go.GetComponent<Collider2D>();
            if (c && c.sharedMaterial) return c.sharedMaterial;
            Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
            return rb ? rb.sharedMaterial : null;
        }

        // ---- beams ----

        static Material beamMaterial;

        public static LineRenderer CreateBeam(Color color, float width, bool withLight, float lightRadius)
        {
            var go = new GameObject("PPLua beam");
            var line = go.AddComponent<LineRenderer>();
            if (!beamMaterial) beamMaterial = new Material(Shader.Find("Sprites/Default"));
            line.sharedMaterial = beamMaterial;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 2;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.sortingLayerID = TopSortingLayer();
            line.sortingOrder = short.MaxValue;
            if (withLight) ModAPI.CreateLight(go.transform, color, lightRadius, 2f);
            return line;
        }

        public static void SetBeam(LineRenderer line, Vector2 from, Vector2 to)
        {
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            LightSprite light = line.GetComponentInChildren<LightSprite>(true);
            if (light) light.transform.position = to;
        }

        static int TopSortingLayer()
        {
            SortingLayer[] layers = SortingLayer.layers;
            int foreground = Array.FindIndex(layers, l => l.name == "Foreground");
            return foreground >= 0 ? layers[foreground].id : layers[layers.Length - 1].id;
        }

        // ---- audio ----

        /// <summary>A controllable sound source riding on an object, routed through the game's own mixer group.</summary>
        public static AudioSource CreateAudio(GameObject parent, AudioClip clip, float volume, float pitch, bool loop)
        {
            var child = new GameObject("PPLua audio");
            child.transform.SetParent(parent.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.loop = loop;
            source.spatialBlend = 0f;
            PhysicalBehaviour phys = parent.GetComponent<PhysicalBehaviour>();
            if (phys && phys.MainAudioSource) source.outputAudioMixerGroup = phys.MainAudioSource.outputAudioMixerGroup;
            child.AddComponent<Optout>();
            return source;
        }

        // ---- free-standing sprites (ppg.show): visuals with no physics ----

        public static GameObject Show(Sprite sprite, Vector2 position, float scale, float angle, bool behind, float alpha)
        {
            var go = new GameObject("PPLua sprite");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(1f, 1f, 1f, alpha);
            if (behind)
            {
                // above the map's backdrop, below every object
                int background = SortingLayer.NameToID("Background");
                sr.sortingLayerID = SortingLayer.IsValid(background) ? background : SortingLayer.layers[0].id;
            }
            else
            {
                sr.sortingLayerID = TopSortingLayer();
            }
            sr.sortingOrder = 32000;
            return go;
        }

        // ---- decorations ----

        /// <summary>A sprite that rides along with an object: no collider, not saved (after_spawn re-adds it).</summary>
        public static GameObject Decorate(GameObject parent, Sprite sprite, Vector2 offset, float angle, float scale, bool behind)
        {
            var child = new GameObject("PPLua decoration");
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = offset;
            child.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            child.transform.localScale = Vector3.one * scale;
            var sr = child.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            SpriteRenderer parentRenderer = parent.GetComponent<SpriteRenderer>();
            if (parentRenderer)
            {
                sr.sortingLayerID = parentRenderer.sortingLayerID;
                sr.sortingOrder = parentRenderer.sortingOrder + (behind ? -1 : 1);
            }
            child.AddComponent<Optout>();
            return child;
        }

        // ---- skin painting ----

        static readonly Dictionary<string, Texture2D> painted = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Paints overlays onto a person's own skin atlas: each overlay is stretched over one limb's region,
        /// keeps the original silhouette and shading, and the result is applied with SetBodyTextures.
        /// The game's texture is read on the player's machine; mods only ship their overlays.
        /// </summary>
        public static void Paint(PersonBehaviour person, Dictionary<string, Texture2D> overlays)
        {
            LimbBehaviour first = person.Limbs.FirstOrDefault(l => l && l.SkinMaterialHandler && l.SkinMaterialHandler.renderer);
            if (!first) throw new ScriptRuntimeException("person:paint: this person has no skin");
            Texture2D atlas = first.SkinMaterialHandler.renderer.sprite.texture;
            string key = atlas.GetInstanceID() + "|" + string.Join("|", overlays.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
                .Select(o => o.Key.ToLowerInvariant() + "=" + o.Value.GetInstanceID()));
            if (!painted.TryGetValue(key, out Texture2D result) || !result)
            {
                result = Composite(person, atlas, overlays);
                painted[key] = result;
            }
            person.SetBodyTextures(result);
        }

        static Texture2D Composite(PersonBehaviour person, Texture2D atlas, Dictionary<string, Texture2D> overlays)
        {
            int width = atlas.width;
            Color32[] px = ReadPixels(atlas);
            overlays.TryGetValue("*", out Texture2D fallback);
            var done = new HashSet<Rect>();
            foreach (LimbBehaviour limb in person.Limbs)
            {
                if (!limb || !limb.SkinMaterialHandler) continue;
                Texture2D overlay = overlays.TryGetValue(limb.name, out Texture2D o) ? o : fallback;
                Sprite sprite = limb.SkinMaterialHandler.renderer.sprite;
                if (!overlay || sprite.texture != atlas || !done.Add(sprite.rect)) continue;

                int x0 = (int)sprite.rect.x, y0 = (int)sprite.rect.y, rw = (int)sprite.rect.width, rh = (int)sprite.rect.height;
                float reference = MedianLuminance(px, width, x0, y0, rw, rh);
                Color32[] ov = overlay.GetPixels32();
                int ow = overlay.width, oh = overlay.height;
                for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    int i = (y0 + y) * width + x0 + x;
                    Color32 c = px[i];
                    Color32 paint = ov[Mathf.Min(oh - 1, y * oh / rh) * ow + Mathf.Min(ow - 1, x * ow / rw)];
                    if (c.a == 0 || paint.a == 0) continue;
                    float shade = Mathf.Clamp(Luminance(c) / reference, 0.35f, 1.25f);
                    float t = paint.a / 255f;
                    px[i] = new Color32(
                        (byte)Mathf.Lerp(c.r, Mathf.Min(255f, paint.r * shade), t),
                        (byte)Mathf.Lerp(c.g, Mathf.Min(255f, paint.g * shade), t),
                        (byte)Mathf.Lerp(c.b, Mathf.Min(255f, paint.b * shade), t),
                        c.a);
                }
            }
            var result = new Texture2D(width, atlas.height, TextureFormat.RGBA32, false)
            {
                name = "PPLua paint",
                filterMode = atlas.filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            result.SetPixels32(px);
            result.Apply(false, false);
            return result;
        }

        static float Luminance(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        static float MedianLuminance(Color32[] px, int width, int x0, int y0, int w, int h)
        {
            var values = new List<float>(w * h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[(y0 + y) * width + x0 + x];
                if (c.a > 0) values.Add(Luminance(c));
            }
            if (values.Count == 0) return 1f;
            values.Sort();
            return Mathf.Max(0.05f, values[values.Count / 2]);
        }

        /// <summary>Pixels of any texture, including ones the game marked non-readable (copied through the GPU).</summary>
        static Color32[] ReadPixels(Texture2D src)
        {
            if (src.isReadable) return src.GetPixels32();
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            return px;
        }

        /// <summary>Accepts Unity KeyCode names case-insensitively, plus bare digits ("1" = Alpha1).</summary>
        public static KeyCode Key(string name)
        {
            if (keys.TryGetValue(name, out KeyCode k)) return k;
            string lookup = name.Length == 1 && char.IsDigit(name[0]) ? "Alpha" + name : name;
            try
            {
                k = (KeyCode)Enum.Parse(typeof(KeyCode), lookup, true);
            }
            catch (ArgumentException)
            {
                throw new ScriptRuntimeException($"unknown key '{name}' (use Unity KeyCode names: 'e', 'space', 'leftshift', 'mouse0', '1')");
            }
            keys[name] = k;
            return k;
        }
    }
}

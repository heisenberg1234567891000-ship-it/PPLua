using MoonSharp.Interpreter;
using UnityEngine;

namespace PPLua
{
    /// <summary>
    /// Per-object Lua hooks (object:on_use / on_update / on_collide). Skipped by the save system; after_spawn runs
    /// again when a save loads, which is where mods should install these hooks.
    /// </summary>
    [SkipSerialisation]
    public sealed class LuaObjectBehaviour : MonoBehaviour, Messages.IUse, Messages.IShot, Messages.ILodged
    {
        internal LuaMod Mod;
        internal DynValue OnUse, OnUpdate, OnCollide, OnStab, OnShot;

        /// <summary>Sent by the game to a sharp object (needle, blade) when it lodges in something.</summary>
        public void Lodged(Stabbing stabbing)
        {
            if (OnStab == null || Mod == null || !Mod.Running || !stabbing.victim) return;
            Mod.Call(OnStab, Mod.Handles.Object(gameObject), Mod.Handles.Object(stabbing.victim.gameObject));
        }

        /// <summary>Sent by the game to whatever a bullet hits.</summary>
        public void Shot(Shot shot)
        {
            if (OnShot == null || Mod == null || !Mod.Running) return;
            Mod.Call(OnShot, Mod.Handles.Object(gameObject), DynValue.NewNumber(shot.damage),
                DynValue.NewNumber(shot.point.x), DynValue.NewNumber(shot.point.y));
        }

        internal static LuaObjectBehaviour For(GameObject go, LuaMod mod)
        {
            foreach (LuaObjectBehaviour b in go.GetComponents<LuaObjectBehaviour>())
                if (b.Mod == mod) return b;
            LuaObjectBehaviour added = go.AddComponent<LuaObjectBehaviour>();
            added.Mod = mod;
            return added;
        }

        public void Use(ActivationPropagation activation)
        {
            if (OnUse != null && Mod != null && Mod.Running) Mod.Call(OnUse, Mod.Handles.Object(gameObject));
        }

        void Update()
        {
            if (OnUpdate != null && Mod != null && Mod.Running)
                Mod.Call(OnUpdate, Mod.Handles.Object(gameObject), DynValue.NewNumber(Time.deltaTime));
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (OnCollide == null || Mod == null || !Mod.Running) return;
            GameObject other = collision.rigidbody ? collision.rigidbody.gameObject : collision.gameObject;
            // the contact normal points away from the other body, i.e. the way this one should bounce
            Vector2 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector2.zero;
            Mod.Call(OnCollide, Mod.Handles.Object(gameObject), Mod.Handles.Object(other),
                DynValue.NewNumber(collision.relativeVelocity.magnitude), DynValue.NewNumber(normal.x), DynValue.NewNumber(normal.y));
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PPLua
{
    /// <summary>
    /// Makes one person live faster than the rest of the world (person.time_scale). Before each physics step
    /// their bodies' velocities are multiplied by k and gravity's pull by k²; after the step velocities are
    /// divided back. The solver therefore moves them k times further per step while contacts and joints stay
    /// physical. Slow motion is compensated, so k = 5 is five times real speed even in slo-mo.
    /// </summary>
    [SkipSerialisation]
    public sealed class TimeWarp : MonoBehaviour
    {
        public float Scale = 1f;
        const float MaxFactor = 20f;

        PersonBehaviour person;
        float applied = 1f;
        float animatedWith = 1f;
        readonly Dictionary<RagdollPose, float> baseAnimation = new Dictionary<RagdollPose, float>();

        public static TimeWarp For(PersonBehaviour person) =>
            person.GetComponent<TimeWarp>() ?? person.gameObject.AddComponent<TimeWarp>();

        void Awake()
        {
            person = GetComponent<PersonBehaviour>();
            StartCoroutine(AfterPhysics());
        }

        float Factor => Mathf.Approximately(Scale, 1f) ? 1f : Mathf.Clamp(Scale / Mathf.Max(Time.timeScale, 0.05f), 0.05f, MaxFactor);

        void FixedUpdate()
        {
            float k = Factor;
            SetAnimationSpeed(k);
            applied = k;
            if (k == 1f) return;
            Vector2 extraGravity = Physics2D.gravity * Time.fixedDeltaTime * (k * k - 1f);
            foreach (Rigidbody2D rb in Bodies())
            {
                rb.velocity = rb.velocity * k + extraGravity * rb.gravityScale;
                rb.angularVelocity *= k;
            }
        }

        IEnumerator AfterPhysics()
        {
            var wait = new WaitForFixedUpdate();
            while (true)
            {
                yield return wait;
                if (applied == 1f) continue;
                foreach (Rigidbody2D rb in Bodies())
                {
                    rb.velocity /= applied;
                    rb.angularVelocity /= applied;
                }
            }
        }

        IEnumerable<Rigidbody2D> Bodies()
        {
            if (!person) yield break;
            foreach (LimbBehaviour limb in person.Limbs)
            {
                if (!limb || !limb.PhysicalBehaviour) continue;
                Rigidbody2D rb = limb.PhysicalBehaviour.rigidbody;
                if (rb && rb.bodyType == RigidbodyType2D.Dynamic) yield return rb;
            }
        }

        // Pose animations (walking, writhing, seizures) run on game time; speed them up by the same factor.
        void SetAnimationSpeed(float k)
        {
            if (!person || Mathf.Approximately(k, animatedWith)) return;
            foreach (RagdollPose pose in person.Poses)
            {
                if (pose == null) continue;
                if (!baseAnimation.TryGetValue(pose, out float baseSpeed)) baseAnimation[pose] = baseSpeed = pose.AnimationSpeedMultiplier;
                pose.AnimationSpeedMultiplier = baseSpeed * k;
            }
            animatedWith = k;
        }

        void OnDestroy()
        {
            foreach (KeyValuePair<RagdollPose, float> pair in baseAnimation)
                if (pair.Key != null) pair.Key.AnimationSpeedMultiplier = pair.Value;
        }
    }

    /// <summary>Destroys linked objects together with the object it sits on (object:bind_to).</summary>
    [SkipSerialisation]
    public sealed class LuaBinder : MonoBehaviour
    {
        public readonly List<GameObject> Linked = new List<GameObject>();

        void OnDestroy()
        {
            foreach (GameObject go in Linked)
                if (go) Destroy(go);
        }
    }
}

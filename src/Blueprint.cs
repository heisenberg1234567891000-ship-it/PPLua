using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PPLua
{
    /// <summary>
    /// A snapshot of how a person's limbs are put together (person:blueprint), so a torn-off limb can be
    /// joined back (limb:reattach) and a disintegrated one brought back (limb:restore).
    ///
    /// Tearing a limb off makes the game destroy its HingeJoint2D, null the matching entries of both limbs'
    /// ConnectedNodeBehaviour.Connections (the "attached to the body" graph), and unlink ConnectedLimbs and
    /// CirculationBehaviour.Source. Disintegration switches colliders, renderers and simulation off,
    /// deactivates the object and bumps the root's DisintegrationCounterBehaviour.
    ///
    /// The prefab joints auto-configure their connected anchor, so Unity re-derives it from wherever the two
    /// limbs happen to be each time it rebuilds the joint (a teleport, a reactivation). A limb moved while it
    /// still had a joint would then be held at the wrong place, so Take switches that off.
    /// </summary>
    [SkipSerialisation]
    public sealed class Blueprint : MonoBehaviour
    {
        public sealed class Part
        {
            public LimbBehaviour Parent;
            public Vector2 Anchor, ConnectedAnchor;
            public float ReferenceAngle;
            public bool UseLimits, UseMotor, EnableCollision;
            public JointAngleLimits2D Limits;
            public JointMotor2D Motor;
            public float BreakForce, BreakTorque;
            public float BreakingThreshold, RegenerationSpeed;
            public ConnectedNodeBehaviour[] Connections;
            public List<LimbBehaviour> ConnectedLimbs;
            public CirculationBehaviour Source;
            public Dictionary<Collider2D, bool> Colliders;
            public Dictionary<Renderer, bool> Renderers;
        }

        readonly Dictionary<LimbBehaviour, Part> parts = new Dictionary<LimbBehaviour, Part>();

        public static Blueprint Take(PersonBehaviour person)
        {
            Blueprint bp = person.GetComponent<Blueprint>() ?? person.gameObject.AddComponent<Blueprint>();
            foreach (LimbBehaviour limb in person.Limbs)
                if (limb && limb.gameObject.activeSelf && !limb.IsDismembered) bp.Record(limb);
            // a limb already lost (torn off or destroyed) is drawn from its twin on the other side, if that one is whole
            foreach (LimbBehaviour limb in person.Limbs)
                if (limb && !bp.parts.ContainsKey(limb)) bp.RecordFromTwin(person, limb);
            return bp;
        }

        // Arms and legs come in pairs named "UpperArm" / "UpperArmFront"; the torso and head have no twin.
        static LimbBehaviour Twin(PersonBehaviour person, LimbBehaviour limb)
        {
            string name = limb.name.EndsWith("Front") ? limb.name.Substring(0, limb.name.Length - 5) : limb.name + "Front";
            return person.Limbs.FirstOrDefault(l => l && l != limb && l.name == name);
        }

        void RecordFromTwin(PersonBehaviour person, LimbBehaviour limb)
        {
            LimbBehaviour twin = Twin(person, limb);
            if (!twin || !parts.TryGetValue(twin, out Part t) || !t.Parent) return;
            // the twin's links, moved over to this side: its forearm becomes our forearm, the shared torso stays
            LimbBehaviour Mirror(LimbBehaviour l) => l ? Twin(person, l) ?? l : null;
            ConnectedNodeBehaviour MirrorNode(ConnectedNodeBehaviour n)
            {
                LimbBehaviour l = n ? n.GetComponent<LimbBehaviour>() : null;
                return l ? Mirror(l).NodeBehaviour : n;
            }
            var part = new Part
            {
                Parent = Mirror(t.Parent),
                Anchor = t.Anchor,
                ConnectedAnchor = t.ConnectedAnchor,
                ReferenceAngle = t.ReferenceAngle,
                UseLimits = t.UseLimits,
                Limits = t.Limits,
                UseMotor = t.UseMotor,
                Motor = t.Motor,
                BreakForce = t.BreakForce,
                BreakTorque = t.BreakTorque,
                EnableCollision = t.EnableCollision,
                BreakingThreshold = t.BreakingThreshold,
                RegenerationSpeed = t.RegenerationSpeed,
                Connections = t.Connections?.Select(MirrorNode).ToArray(),
                ConnectedLimbs = t.ConnectedLimbs.Select(Mirror).ToList(),
                Source = t.Source ? Mirror(t.Source.Limb).CirculationBehaviour : null,
                Colliders = limb.GetComponentsInChildren<Collider2D>(true).ToDictionary(c => c, c => LikeTwin(limb, c, twin, t.Colliders)),
                Renderers = limb.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => LikeTwin(limb, r, twin, t.Renderers)),
            };
            parts[limb] = part;
            // the parent was recorded without this limb, so give it the link back
            if (parts.TryGetValue(part.Parent, out Part p) && !p.ConnectedLimbs.Contains(limb)) p.ConnectedLimbs.Add(limb);
        }

        // Whether a collider or renderer of a lost limb should be on: as the one at the same place in its twin.
        static bool LikeTwin<T>(LimbBehaviour limb, T component, LimbBehaviour twin, Dictionary<T, bool> twinStates) where T : Component
        {
            string path = PathIn(limb.transform, component.transform);
            foreach (KeyValuePair<T, bool> s in twinStates)
                if (s.Key && s.Key.GetType() == component.GetType() && PathIn(twin.transform, s.Key.transform) == path) return s.Value;
            return true;
        }

        static string PathIn(Transform root, Transform t)
        {
            string path = "";
            for (; t && t != root; t = t.parent) path = "/" + t.name + path;
            return path;
        }

        public static Part Of(LimbBehaviour limb)
        {
            Blueprint bp = limb.Person ? limb.Person.GetComponent<Blueprint>() : null;
            return bp != null && bp.parts.TryGetValue(limb, out Part p) ? p : null;
        }

        void Record(LimbBehaviour limb)
        {
            var part = new Part
            {
                BreakingThreshold = limb.BreakingThreshold,
                RegenerationSpeed = limb.RegenerationSpeed,
                Connections = limb.NodeBehaviour ? (ConnectedNodeBehaviour[])limb.NodeBehaviour.Connections.Clone() : null,
                ConnectedLimbs = limb.ConnectedLimbs != null ? new List<LimbBehaviour>(limb.ConnectedLimbs) : new List<LimbBehaviour>(),
                Source = limb.CirculationBehaviour ? limb.CirculationBehaviour.Source : null,
                Colliders = limb.GetComponentsInChildren<Collider2D>(true).ToDictionary(c => c, c => c.enabled),
                Renderers = limb.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled),
            };
            HingeJoint2D joint = limb.HasJoint ? limb.Joint : null;
            if (joint && joint.connectedBody)
            {
                joint.autoConfigureConnectedAnchor = false;
                part.Parent = joint.connectedBody.GetComponent<LimbBehaviour>();
                part.Anchor = joint.anchor;
                part.ConnectedAnchor = joint.connectedAnchor;
                part.ReferenceAngle = joint.referenceAngle;
                part.UseLimits = joint.useLimits;
                part.Limits = joint.limits;
                part.UseMotor = joint.useMotor;
                part.Motor = joint.motor;
                part.BreakForce = joint.breakForce;
                part.BreakTorque = joint.breakTorque;
                part.EnableCollision = joint.enableCollision;
            }
            parts[limb] = part;
        }

        /// <summary>Where a limb sits on its parent right now: world position and rigidbody angle.</summary>
        public static bool RestPose(LimbBehaviour limb, out Vector2 position, out float angle) => RestPose(limb, out position, out angle, out _);

        /// <summary>The same, plus the socket: the point on the parent the limb hangs from.</summary>
        public static bool RestPose(LimbBehaviour limb, out Vector2 position, out float angle, out Vector2 socket)
        {
            position = socket = default;
            angle = 0f;
            Part part = Of(limb);
            if (part == null || !part.Parent || !part.Parent.gameObject.activeSelf) return false;
            Rigidbody2D parentBody = part.Parent.PhysicalBehaviour.rigidbody;
            // Box2D/Unity: referenceAngle = connected body angle - own angle when the joint was made
            angle = parentBody.rotation - part.ReferenceAngle;
            socket = part.Parent.transform.TransformPoint(part.ConnectedAnchor);
            Vector3 scale = limb.transform.lossyScale;
            Vector2 offset = Quaternion.Euler(0f, 0f, angle) * new Vector3(part.Anchor.x * scale.x, part.Anchor.y * scale.y, 0f);
            position = socket - offset;
            return true;
        }

        /// <summary>Puts a limb back on its parent: rest pose, a new joint, and the body's bookkeeping.</summary>
        public static bool Reattach(LimbBehaviour limb)
        {
            Part part = Of(limb);
            if (part == null || !part.Parent || limb.HasJoint && limb.Joint) return false;
            if (!limb.gameObject.activeSelf || limb.PhysicalBehaviour.isDisintegrated) return false;
            if (!RestPose(limb, out Vector2 position, out float angle)) return false;

            Rigidbody2D body = limb.PhysicalBehaviour.rigidbody;
            Rigidbody2D parentBody = part.Parent.PhysicalBehaviour.rigidbody;
            body.bodyType = RigidbodyType2D.Dynamic;
            // whatever still hangs on this limb (the shin and foot under a thigh) comes along rigidly,
            // so their joints are not left stretched across the gap
            float turn = angle - body.rotation;
            Quaternion q = Quaternion.Euler(0f, 0f, turn);
            foreach (LimbBehaviour d in Descendants(limb))
            {
                Rigidbody2D rb = d.PhysicalBehaviour.rigidbody;
                Place(d, position + (Vector2)(q * (rb.position - body.position)), rb.rotation + turn, parentBody.velocity);
            }
            Place(limb, position, angle, parentBody.velocity);

            var joint = limb.gameObject.AddComponent<HingeJoint2D>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = parentBody;
            joint.anchor = part.Anchor;
            joint.connectedAnchor = part.ConnectedAnchor;
            joint.useLimits = part.UseLimits;
            joint.limits = part.Limits;
            joint.useMotor = part.UseMotor;
            joint.motor = part.Motor;
            joint.breakForce = part.BreakForce;
            joint.breakTorque = part.BreakTorque;
            joint.enableCollision = part.EnableCollision;

            limb.Joint = joint;
            limb.HasJoint = true;
            limb.IsDismembered = false;
            limb.BreakingThreshold = part.BreakingThreshold;
            limb.RegenerationSpeed = part.RegenerationSpeed;

            Relink(limb, part.Parent);
            if (limb.CirculationBehaviour)
            {
                limb.CirculationBehaviour.IsDisconnected = false;
                limb.CirculationBehaviour.BleedingRate = 0f;
            }
            GoreStringBehaviour gore = limb.GetComponent<GoreStringBehaviour>();
            if (gore && gore.Joint) gore.DestroyJoint();
            return true;
        }

        static void Place(LimbBehaviour limb, Vector2 position, float angle, Vector2 velocity)
        {
            Rigidbody2D body = limb.PhysicalBehaviour.rigidbody;
            body.position = position;
            body.rotation = angle;
            limb.transform.SetPositionAndRotation(new Vector3(position.x, position.y, limb.transform.position.z), Quaternion.Euler(0f, 0f, angle));
            body.velocity = velocity;
            body.angularVelocity = 0f;
        }

        // Limbs hanging on this one through intact joints, all the way down.
        static List<LimbBehaviour> Descendants(LimbBehaviour limb)
        {
            var found = new List<LimbBehaviour>();
            var queue = new Queue<LimbBehaviour>();
            queue.Enqueue(limb);
            while (queue.Count > 0)
            {
                Rigidbody2D body = queue.Dequeue().PhysicalBehaviour.rigidbody;
                foreach (LimbBehaviour other in limb.Person.Limbs)
                {
                    if (!other || other == limb || found.Contains(other) || !other.HasJoint || !other.Joint || other.Joint.connectedBody != body) continue;
                    found.Add(other);
                    queue.Enqueue(other);
                }
            }
            return found;
        }

        /// <summary>Takes a limb off its parent the way a torn joint does, minus the blood and pain.</summary>
        public static bool Detach(LimbBehaviour limb)
        {
            if (!limb.HasJoint || !limb.Joint) return false;
            HingeJoint2D joint = limb.Joint;
            LimbBehaviour parent = joint.connectedBody ? joint.connectedBody.GetComponent<LimbBehaviour>() : null;
            joint.enabled = false;
            Destroy(joint);
            limb.HasJoint = false;
            limb.IsDismembered = true;
            if (limb.CirculationBehaviour) limb.CirculationBehaviour.IsDisconnected = true;
            if (!parent) return true;
            if (limb.NodeBehaviour && parent.NodeBehaviour) limb.NodeBehaviour.DisconnectFrom(parent.NodeBehaviour);
            limb.ConnectedLimbs.Remove(parent);
            parent.ConnectedLimbs.Remove(limb);
            if (limb.CirculationBehaviour && limb.CirculationBehaviour.Source && limb.CirculationBehaviour.Source.Limb == parent) limb.CirculationBehaviour.Source = null;
            if (parent.CirculationBehaviour && parent.CirculationBehaviour.Source && parent.CirculationBehaviour.Source.Limb == limb) parent.CirculationBehaviour.Source = null;
            return true;
        }

        // Restores the node-graph edge and limb/circulation links between a limb and its parent.
        static void Relink(LimbBehaviour limb, LimbBehaviour parent)
        {
            Part mine = Of(limb), theirs = Of(parent);
            RestoreEdge(limb.NodeBehaviour, mine?.Connections, parent.NodeBehaviour);
            RestoreEdge(parent.NodeBehaviour, theirs?.Connections, limb.NodeBehaviour);
            if (mine != null && mine.ConnectedLimbs.Contains(parent) && !limb.ConnectedLimbs.Contains(parent)) limb.ConnectedLimbs.Add(parent);
            if (theirs != null && theirs.ConnectedLimbs.Contains(limb) && !parent.ConnectedLimbs.Contains(limb)) parent.ConnectedLimbs.Add(limb);
            if (mine != null && mine.Source && mine.Source.Limb == parent) limb.CirculationBehaviour.Source = mine.Source;
            if (theirs != null && theirs.Source && theirs.Source.Limb == limb) parent.CirculationBehaviour.Source = theirs.Source;
        }

        // Puts the edge back in its original slot, or in a free one when that slot is taken or was never recorded
        // (a limb drawn from its twin).
        static void RestoreEdge(ConnectedNodeBehaviour node, ConnectedNodeBehaviour[] original, ConnectedNodeBehaviour other)
        {
            if (!node || !other || node.IsConnectedTo(other)) return;
            int slot = original == null ? -1 : System.Array.IndexOf(original, other);
            if (slot < 0 || slot >= node.Connections.Length || node.Connections[slot]) slot = System.Array.FindIndex(node.Connections, c => !c);
            if (slot >= 0) node.Connections[slot] = other;
        }

        /// <summary>
        /// Undoes PhysicalBehaviour.Disintegrate for a limb recorded in the blueprint and puts it back on its parent.
        /// Limbs that hung on it come back detached (dismembered), ready for Reattach.
        /// </summary>
        public static bool Restore(LimbBehaviour limb)
        {
            Part part = Of(limb);
            PhysicalBehaviour phys = limb.PhysicalBehaviour;
            if (part == null || (!phys.isDisintegrated && limb.gameObject.activeSelf)) return false;
            limb.gameObject.SetActive(true);
            foreach (KeyValuePair<Collider2D, bool> c in part.Colliders)
                if (c.Key) c.Key.enabled = c.Value;
            foreach (KeyValuePair<Renderer, bool> r in part.Renderers)
                if (r.Key) r.Key.enabled = r.Value;
            foreach (Rigidbody2D rb in limb.GetComponentsInChildren<Rigidbody2D>(true)) rb.simulated = true;
            BloodTankBehaviour tank = limb.GetComponent<BloodTankBehaviour>();
            if (tank) tank.enabled = true;
            phys.isDisintegrated = false;
            phys.BurnProgress = 0f;
            limb.CurrentlyShattered = LimbBehaviour.ShatterFlags.None;
            DisintegrationCounterBehaviour counter = limb.GetComponentInParent<DisintegrationCounterBehaviour>();
            if (counter) counter.DisintegrationCount = Mathf.Max(0, counter.DisintegrationCount - 1);

            // The game's disintegration handler zeroed this limb's strength and the break force of every joint
            // touching it (so they would tear when it came back), cut all its node links and closed its circulation.
            limb.BreakingThreshold = part.BreakingThreshold;
            limb.RegenerationSpeed = part.RegenerationSpeed;
            if (limb.CirculationBehaviour)
            {
                limb.CirculationBehaviour.IsDisconnected = false;
                limb.CirculationBehaviour.BleedingRate = 0f;
            }

            // Whatever hung on this limb fell away while it was gone: those joints were zeroed and pointed at a
            // switched-off body, so they hold nothing. Take them off properly; they go back on with Reattach.
            Rigidbody2D body = phys.rigidbody;
            foreach (LimbBehaviour child in limb.Person.Limbs)
                if (child && child != limb && child.HasJoint && child.Joint && child.Joint.connectedBody == body) Detach(child);
            // Its own joint was rebuilt by the reactivation from wherever the limb lay, so make a fresh one at the
            // socket. Without a parent to go on, it stays detached until Reattach.
            Detach(limb);
            Reattach(limb);
            return true;
        }

        public static LimbBehaviour ParentOf(LimbBehaviour limb) => Of(limb)?.Parent;

        /// <summary>How far apart the two halves of a limb's joint are, in metres; -1 without a joint.</summary>
        public static float Gap(LimbBehaviour limb)
        {
            HingeJoint2D joint = limb.HasJoint ? limb.Joint : null;
            if (!joint || !joint.connectedBody) return -1f;
            Vector2 mine = limb.transform.TransformPoint(joint.anchor);
            Vector2 theirs = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            return (mine - theirs).magnitude;
        }
    }
}

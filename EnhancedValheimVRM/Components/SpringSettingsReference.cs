using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    // runs after the animation is on the bones and before the springs read their center
    [DefaultExecutionOrder(10500)]
    public sealed class SpringSettingsReference : MonoBehaviour
    {
        // a spring only feels movement relative to its center. this one follows its source by the
        // immobile amount, so that much of the sources movement never reaches the springs
        private sealed class ImmobileCenter
        {
            internal Transform Source, Center;
            internal float Amount;
            internal bool AllMotion;
            internal Vector3 LastPosition;
            internal Quaternion LastRotation;

            // where the center is in the world. kept here since the transform rides along with the avatar
            internal Vector3 Position;
            internal Quaternion Rotation;
        }

        // further than this in one frame is a teleport, the springs should not see any of it
        private const float TeleportDistance = 2f;

        // the center drifts away from the avatar below 1.0, floats get coarse that far out
        private const float MaxDrift = 5000f;

        private readonly Dictionary<VRMSpringBone, Vector2> _vrm0 = new Dictionary<VRMSpringBone, Vector2>();

        private readonly Dictionary<VRM10SpringBoneJoint, Vector2> _vrm1 =
            new Dictionary<VRM10SpringBoneJoint, Vector2>();

        // the center each spring came with, null when the vrm has none
        private readonly Dictionary<VRMSpringBone, Transform> _vrm0Centers =
            new Dictionary<VRMSpringBone, Transform>();

        private readonly Dictionary<Vrm10InstanceSpringBone.Spring, Transform> _vrm1Centers =
            new Dictionary<Vrm10InstanceSpringBone.Spring, Transform>();

        private readonly List<ImmobileCenter> _centers = new List<ImmobileCenter>();

        public void Apply(VrmSettings settings)
        {
            foreach (var center in _centers) Destroy(center.Center.gameObject);
            _centers.Clear();

            foreach (var spring in GetComponentsInChildren<VRMSpringBone>(true))
            {
                if (!_vrm0.TryGetValue(spring, out var original))
                    _vrm0[spring] = original = new Vector2(spring.m_stiffnessForce, spring.m_gravityPower);
                spring.m_stiffnessForce = original.x * settings.SpringBoneStiffness;
                spring.m_gravityPower = original.y * settings.SpringBoneGravityPower;

                if (!_vrm0Centers.TryGetValue(spring, out var authored))
                    _vrm0Centers[spring] = authored = spring.m_center;
                var roots = spring.RootBones.Where(root => root != null).ToList();
                var center = authored != null
                    ? authored
                    : Center(settings, spring.m_comment, roots);
                if (spring.m_center == center) continue;
                spring.m_center = center;
                // the tails are kept in the centers space, so they have to be rebuilt
                spring.Setup();
            }

            foreach (var joint in GetComponentsInChildren<VRM10SpringBoneJoint>(true))
            {
                if (!_vrm1.TryGetValue(joint, out var original))
                    _vrm1[joint] = original = new Vector2(joint.m_stiffnessForce, joint.m_gravityPower);
                joint.m_stiffnessForce = original.x * settings.SpringBoneStiffness;
                joint.m_gravityPower = original.y * settings.SpringBoneGravityPower;
            }

            var vrm1 = GetComponent<Vrm10Instance>();
            if (vrm1 != null)
            {
                foreach (var spring in vrm1.SpringBone.Springs)
                {
                    if (!_vrm1Centers.TryGetValue(spring, out var authored))
                        _vrm1Centers[spring] = authored = spring.Center;
                    var roots = spring.Joints.Where(joint => joint != null)
                        .Take(1)
                        .Select(joint => joint.transform)
                        .ToList();
                    spring.Center = authored != null ? authored : Center(settings, spring.Name, roots);
                }
            }

            (GetComponent<SpringAngleLimit>() ?? gameObject.AddComponent<SpringAngleLimit>()).Apply(settings);
            if (vrm1 != null && _vrm1.Count > 0) vrm1.Runtime.ReconstructSpringBone();
            // Keep the avatar's colliders, drag, radii, gravity directions and update
            // modes. These are authored spring parameters, not scale hacks.
        }

        // null leaves the group in world space, which is what a vrm without a center gets
        private Transform Center(VrmSettings settings, string comment, List<Transform> roots)
        {
            var amount = settings.GetSpringBoneImmobile(comment, roots.Select(root => root.name));
            if (amount <= 0 || roots.Count == 0) return null;
            var allMotion = settings.SpringBoneImmobileAllMotion && roots[0].parent != null;
            var source = allMotion ? roots[0].parent : transform;
            foreach (var existing in _centers)
            {
                if (existing.Source == source && existing.AllMotion == allMotion &&
                    Mathf.Approximately(existing.Amount, amount))
                    return existing.Center;
            }

            var center = new ImmobileCenter
            {
                Source = source,
                Center = new GameObject("SpringImmobileCenter").transform,
                Amount = amount,
                AllMotion = allMotion
            };
            center.Center.SetParent(transform, false);
            Snap(center);
            _centers.Add(center);
            return center.Center;
        }

        // World only takes the position, so turning and the animation still reach the springs
        private static void Snap(ImmobileCenter center)
        {
            center.LastPosition = center.Source.position;
            center.LastRotation = center.Source.rotation;
            center.Position = center.LastPosition;
            center.Rotation = center.AllMotion ? center.LastRotation : Quaternion.identity;
            center.Center.SetPositionAndRotation(center.Position, center.Rotation);
        }

        private void LateUpdate()
        {
            var drifted = false;
            foreach (var center in _centers)
            {
                if (center.Source == null) continue;
                var position = center.Source.position;
                var rotation = center.Source.rotation;
                var moved = position - center.LastPosition;
                if (center.Amount >= 1f)
                {
                    Snap(center);
                    continue;
                }

                var amount = moved.sqrMagnitude > TeleportDistance * TeleportDistance ? 1f : center.Amount;
                if (center.AllMotion)
                {
                    // turn around the source, the center itself may have drifted off
                    var turned = Quaternion.Slerp(Quaternion.identity,
                        rotation * Quaternion.Inverse(center.LastRotation),
                        amount);
                    center.Position = center.LastPosition + turned * (center.Position - center.LastPosition);
                    center.Rotation = turned * center.Rotation;
                }

                center.Position += moved * amount;
                center.Center.SetPositionAndRotation(center.Position, center.Rotation);
                center.LastPosition = position;
                center.LastRotation = rotation;
                drifted |= (center.Position - position).sqrMagnitude > MaxDrift * MaxDrift;
            }

            if (drifted) Recenter();
        }

        // puts the centers back on the avatar. the springs restart from rest, once in a long while
        private void Recenter()
        {
            foreach (var center in _centers) Snap(center);
            foreach (var spring in GetComponentsInChildren<VRMSpringBone>(true))
            {
                if (_centers.Exists(center => center.Center == spring.m_center)) spring.Setup();
            }

            var vrm1 = GetComponent<Vrm10Instance>();
            if (vrm1 != null && _vrm1.Count > 0) vrm1.Runtime.ReconstructSpringBone();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // Rotation offsets between the vanilla rig and the avatar rig, captured while both show the
    // same pose. The ragdoll uses the vanilla skeleton, so these offsets map its bones to the
    // avatar regardless of the pose either one is in when the corpse is created.
    internal sealed class RigOffsets
    {
        internal readonly Dictionary<HumanBodyBones, Quaternion> Rotations = new Dictionary<HumanBodyBones, Quaternion>();
        internal Vector3 HipOffset;
        internal bool HasHips;

        internal static RigOffsets Capture(Animator vanilla, Animator avatar)
        {
            if (vanilla == null || avatar == null) return null;
            var offsets = new RigOffsets();
            foreach (HumanBodyBones name in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (name == HumanBodyBones.LastBone) continue;
                var source = BoneLookup.Get(vanilla, name);
                var target = BoneLookup.Get(avatar, name);
                if (source == null || target == null) continue;
                offsets.Rotations[name] = Quaternion.Inverse(source.rotation) * target.rotation;
                if (name == HumanBodyBones.Hips)
                {
                    offsets.HipOffset = Quaternion.Inverse(source.rotation) * (target.position - source.position);
                    offsets.HasHips = true;
                }
            }

            return offsets.HasHips ? offsets : null;
        }
    }

    [DefaultExecutionOrder(9000)]
    public sealed class VrmRagdoll : MonoBehaviour
    {
        private sealed class Bone
        {
            public Transform Source, Target;
            public Quaternion RotationOffset;
        }

        private List<Bone> _bones;
        private Bone _hips;
        private Vector3 _hipOffset;

        internal bool Setup(Ragdoll ragdoll, Animator avatar, RigOffsets offsets)
        {
            var bones = new List<Bone>();
            foreach (HumanBodyBones name in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (name == HumanBodyBones.LastBone) continue;
                var source = BoneLookup.Find(ragdoll.transform, name);
                var target = BoneLookup.Get(avatar, name);
                if (source == null || target == null || source.IsChildOf(transform)) continue;
                // Without captured offsets the ragdoll's spawn pose and the avatar's death pose
                // differ, and the limbs would keep that difference forever.
                var offset = offsets != null && offsets.Rotations.TryGetValue(name, out var stored)
                    ? stored
                    : Quaternion.Inverse(source.rotation) * target.rotation;
                var bone = new Bone { Source = source, Target = target, RotationOffset = offset };
                bones.Add(bone);
                if (name == HumanBodyBones.Hips) _hips = bone;
            }

            if (_hips == null) return false;
            _hipOffset = offsets != null && offsets.HasHips
                ? offsets.HipOffset
                : Quaternion.Inverse(_hips.Source.rotation) * (_hips.Target.position - _hips.Source.position);
            _bones = bones.OrderBy(bone => Depth(bone.Target)).ToList();
            avatar.enabled = false;
            // Bones move far from the model root while the corpse settles; keep the mesh rendered.
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;
            return true;
        }

        private static int Depth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        private void LateUpdate()
        {
            if (_hips?.Source == null || _hips.Target == null) return;
            _hips.Target.position = _hips.Source.position + _hips.Source.rotation * _hipOffset;
            foreach (var bone in _bones)
                if (bone.Source != null && bone.Target != null)
                    bone.Target.rotation = bone.Source.rotation * bone.RotationOffset;
        }
    }
}

using System;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    internal static class BoneLookup
    {
        public static Transform Get(Animator animator, HumanBodyBones bone)
        {
            if (animator == null) return null;
            if (animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
            {
                var mapped = animator.GetBoneTransform(bone);
                if (mapped != null) return mapped;
            }

            return Find(animator.transform, bone);
        }

        public static Transform Find(Transform root, HumanBodyBones bone)
        {
            if (root == null) return null;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (BoneNames.Matches(bone.ToString(), transform.name))
                    return transform;
            return null;
        }
    }
}

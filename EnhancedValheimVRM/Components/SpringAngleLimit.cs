using System.Collections.Generic;
using System.Linq;
using UniGLTF.SpringBoneJobs;
using UniVRM10;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    // SpringBoneMaxAngle through univrm's own angle limit. vrm 1.0 joints get a cone limit set on them, vrm 0.x
    // bones are held by PatchVrm0SpringBoneSystem. 0 leaves a vrm 1.0 avatar with the limits it was exported with
    public sealed class SpringAngleLimit : MonoBehaviour
    {
        private struct Authored
        {
            internal AnglelimitTypes Type;
            internal float Pitch, Yaw;
            internal Quaternion Offset;
        }

        private readonly List<Transform> _vrm0 = new List<Transform>();

        private readonly Dictionary<VRM10SpringBoneJoint, Authored> _vrm1 =
            new Dictionary<VRM10SpringBoneJoint, Authored>();

        // the vrm 1.0 springs read their joints when they are rebuilt, so this goes before that
        public void Apply(VrmSettings settings)
        {
            Release();
            foreach (var spring in GetComponentsInChildren<VRMSpringBone>(true))
            {
                var roots = spring.RootBones.Where(root => root != null).Distinct().ToList();
                var maxAngle = settings.GetSpringBoneMaxAngle(spring.m_comment, roots.Select(root => root.name));
                if (!Limits(maxAngle)) continue;
                // the spring moves every bone under its roots
                foreach (var bone in roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)))
                {
                    PatchVrm0SpringBoneSystem.Limits[bone] = maxAngle * Mathf.Deg2Rad;
                    _vrm0.Add(bone);
                }
            }

            var vrm1 = GetComponent<Vrm10Instance>();
            if (vrm1 == null) return;
            foreach (var spring in vrm1.SpringBone.Springs)
            {
                var joints = spring.Joints.Where(joint => joint != null).ToList();
                if (joints.Count == 0) continue;
                var maxAngle = settings.GetSpringBoneMaxAngle(spring.Name, new[] { joints[0].name });
                foreach (var joint in joints)
                {
                    if (!_vrm1.TryGetValue(joint, out var authored))
                    {
                        _vrm1[joint] = authored = new Authored
                        {
                            Type = joint.m_anglelimitType,
                            Pitch = joint.m_pitch,
                            Yaw = joint.m_yaw,
                            Offset = joint.m_limitSpaceOffset
                        };
                    }

                    var limited = Limits(maxAngle);
                    joint.m_anglelimitType = limited ? AnglelimitTypes.Cone : authored.Type;
                    joint.m_pitch = limited ? maxAngle * Mathf.Deg2Rad : authored.Pitch;
                    joint.m_yaw = limited ? 0f : authored.Yaw;
                    joint.m_limitSpaceOffset = limited ? Quaternion.identity : authored.Offset;
                }
            }
        }

        // 180 and up never stops anything
        private static bool Limits(float maxAngle)
        {
            return maxAngle > 0 && maxAngle < 180;
        }

        private void OnDestroy()
        {
            Release();
        }

        private void Release()
        {
            foreach (var bone in _vrm0) PatchVrm0SpringBoneSystem.Limits.Remove(bone);
            _vrm0.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using VRM;

namespace EnhancedValheimVRM
{
    public sealed class SpringSettingsReference : MonoBehaviour
    {
        private readonly Dictionary<VRMSpringBone, Vector2> _vrm0 = new Dictionary<VRMSpringBone, Vector2>();

        private readonly Dictionary<VRM10SpringBoneJoint, Vector2> _vrm1 =
            new Dictionary<VRM10SpringBoneJoint, Vector2>();

        public void Apply(VrmSettings settings)
        {
            foreach (var spring in GetComponentsInChildren<VRMSpringBone>(true))
            {
                if (!_vrm0.TryGetValue(spring, out var original))
                    _vrm0[spring] = original = new Vector2(spring.m_stiffnessForce, spring.m_gravityPower);
                spring.m_stiffnessForce = original.x * settings.SpringBoneStiffness;
                spring.m_gravityPower = original.y * settings.SpringBoneGravityPower;
            }

            foreach (var joint in GetComponentsInChildren<VRM10SpringBoneJoint>(true))
            {
                if (!_vrm1.TryGetValue(joint, out var original))
                    _vrm1[joint] = original = new Vector2(joint.m_stiffnessForce, joint.m_gravityPower);
                joint.m_stiffnessForce = original.x * settings.SpringBoneStiffness;
                joint.m_gravityPower = original.y * settings.SpringBoneGravityPower;
            }

            var vrm1 = GetComponent<Vrm10Instance>();
            if (vrm1 != null && _vrm1.Count > 0) vrm1.Runtime.ReconstructSpringBone();
            // Keep the avatar's centers, colliders, drag, radii, gravity directions
            // and update modes. These are authored spring parameters, not scale hacks.
        }
    }
}

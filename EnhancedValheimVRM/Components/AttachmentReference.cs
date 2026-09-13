using Unity.Mathematics;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // Lives on the existing socket, so a new VrmAnimator cannot recapture an offset
    // already converted by the previous avatar or a ragdoll setup.
    [DisallowMultipleComponent]
    public sealed class AttachmentReference : MonoBehaviour
    {
        private bool _captured;
        private float3 _offsetInSocketAxes;
        private Vector3 _worldScale;

        public void Capture()
        {
            if (_captured || transform.parent == null) return;
            _offsetInSocketAxes = AttachmentMath.ToSocketOffset(AttachmentTransforms.Vector(transform.parent.position),
                AttachmentTransforms.Vector(transform.position),
                AttachmentTransforms.Rotation(transform.rotation));
            _worldScale = transform.lossyScale;
            _captured = true;
        }

        public bool Reparent(Transform parent, Quaternion rotation, float ratio, Vector3? scaleOverride)
        {
            Capture();
            if (!_captured || parent == null) return false;
            var matrix = AttachmentTransforms.Matrix(parent.localToWorldMatrix);
            if (!AttachmentMath.TryMapSocket(matrix,
                    AttachmentTransforms.Rotation(parent.rotation),
                    AttachmentTransforms.Rotation(rotation),
                    _offsetInSocketAxes,
                    ratio,
                    out var position))
                return false;
            if (!AttachmentMath.TryLocalScale(matrix,
                    AttachmentTransforms.Rotation(rotation),
                    AttachmentTransforms.Vector(_worldScale),
                    out var scale))
                return false;

            transform.SetParent(parent, false);
            transform.SetLocalPositionAndRotation(AttachmentTransforms.Vector(position), rotation);
            // Equipment applies its own avatar-size multiplier. Preserve the socket's
            // original world scale here so the VRM ancestor scale is not applied twice.
            transform.localScale = scaleOverride ?? AttachmentTransforms.Vector(scale);
            return true;
        }
    }

    internal static class AttachmentTransforms
    {
        public static float3 Vector(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        public static Vector3 Vector(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        public static quaternion Rotation(Quaternion value)
        {
            return new quaternion(value.x, value.y, value.z, value.w);
        }

        public static float4x4 Matrix(Matrix4x4 value)
        {
            return new float4x4(new float4(value.m00, value.m10, value.m20, value.m30),
                new float4(value.m01, value.m11, value.m21, value.m31),
                new float4(value.m02, value.m12, value.m22, value.m32),
                new float4(value.m03, value.m13, value.m23, value.m33));
        }
    }
}

using UnityEngine;

namespace EnhancedValheimVRM
{
    [DisallowMultipleComponent]
    public sealed class EquipmentTransformReference : MonoBehaviour
    {
        private bool _captured;
        private Vector3 _worldScale;
        private Quaternion _localRotation;
        private Vector3 _baseOffsetInMeters;

        public static EquipmentTransformReference Get(Transform item)
        {
            var reference = item.GetComponent<EquipmentTransformReference>();
            if (reference == null) reference = item.gameObject.AddComponent<EquipmentTransformReference>();
            if (!reference._captured)
            {
                reference._worldScale = item.lossyScale;
                reference._localRotation = item.localRotation;
                reference._baseOffsetInMeters = item.parent != null
                    ? Quaternion.Inverse(item.parent.rotation) * (item.position - item.parent.position)
                    : item.position;
                reference._captured = true;
            }

            return reference;
        }

        public void SetScale(float ratio)
        {
            SetScale(ratio, Vector3.one);
        }

        public void SetScale(float ratio, Vector3 multiplier)
        {
            if (ratio <= 0 || float.IsNaN(ratio) || float.IsInfinity(ratio)) return;
            var parentMatrix = transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
            if (AttachmentMath.TryLocalScale(AttachmentTransforms.Matrix(parentMatrix),
                    AttachmentTransforms.Rotation(transform.localRotation),
                    AttachmentTransforms.Vector(Vector3.Scale(_worldScale * ratio, multiplier)),
                    out var scale))
                transform.localScale = AttachmentTransforms.Vector(scale);
        }

        public void SetRotationOffset(Vector3 offset)
        {
            transform.localRotation = _localRotation * Quaternion.Euler(offset);
        }

        public void SetPositionOffset(Vector3 offsetInMeters, float ratio)
        {
            var parent = transform.parent;
            var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            var rotation = parent != null ? parent.rotation : Quaternion.identity;
            if (AttachmentMath.TryMapItemOffset(AttachmentTransforms.Matrix(matrix),
                    AttachmentTransforms.Rotation(rotation),
                    AttachmentTransforms.Vector(_baseOffsetInMeters + offsetInMeters),
                    ratio,
                    out var position))
                transform.localPosition = AttachmentTransforms.Vector(position);
        }
    }
}

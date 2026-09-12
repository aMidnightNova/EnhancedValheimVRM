using Unity.Mathematics;

namespace EnhancedValheimVRM
{
    // Pure math, shared with the standalone numerical tests. Distances entering/leaving
    // socket space are in world units; import scale is handled by the parent matrices.
    public static class AttachmentMath
    {
        public static float3 ToSocketOffset(float3 parentPosition, float3 socketPosition, quaternion socketRotation)
        {
            return math.rotate(math.inverse(socketRotation), socketPosition - parentPosition);
        }

        public static bool TryMapSocket(float4x4 parentToWorld, quaternion parentRotation,
            quaternion socketLocalRotation, float3 originalOffset, float ratio, out float3 localPosition)
        {
            localPosition = float3.zero;
            if (!math.isfinite(ratio) || ratio <= 0 || !Finite(parentToWorld)) return false;
            float determinant = math.determinant(parentToWorld);
            if (!math.isfinite(determinant) || math.abs(determinant) < 1e-12f) return false;
            quaternion socketWorldRotation = math.mul(parentRotation, socketLocalRotation);
            float3 worldOffset = math.rotate(socketWorldRotation, originalOffset * ratio);
            // w=0 transforms a displacement, excluding the parent's translation.
            localPosition = math.mul(math.inverse(parentToWorld), new float4(worldOffset, 0)).xyz;
            return math.all(math.isfinite(localPosition));
        }

        public static bool TryLocalScale(float4x4 parentToWorld, quaternion localRotation,
            float3 worldScale, out float3 localScale)
        {
            localScale = new float3(1);
            if (!Finite(parentToWorld) || !math.all(math.isfinite(worldScale))) return false;
            // Column lengths include ancestor scale and the child's orientation. Simple
            // component-wise division by parent.lossyScale is wrong for rotated children
            // under a nonuniformly scaled parent.
            float3x3 axes = math.mul(new float3x3(parentToWorld), new float3x3(localRotation));
            float3 lengths = new float3(math.length(axes.c0), math.length(axes.c1), math.length(axes.c2));
            if (!math.all(math.isfinite(lengths)) || math.any(lengths < 1e-6f)) return false;
            localScale = worldScale / lengths;
            return math.all(math.isfinite(localScale));
        }

        public static bool TryMapItemOffset(float4x4 socketToWorld, quaternion socketRotation,
            float3 offsetInMeters, float ratio, out float3 localPosition)
        {
            // Settings express physical distances along socket axes. Convert those
            // distances into the socket's import units instead of scaling them twice.
            return TryMapSocket(socketToWorld, socketRotation, quaternion.identity,
                offsetInMeters, ratio, out localPosition);
        }

        public static float3 ToBoneOffset(quaternion boneRotation, float3 worldOffset, float3 adjustment)
        {
            return math.rotate(math.inverse(boneRotation), worldOffset) + adjustment;
        }

        public static float3 PlaceBone(float3 handPosition, quaternion boneRotation, float3 offset)
        {
            return handPosition + math.rotate(boneRotation, offset);
        }

        private static bool Finite(float4x4 matrix)
        {
            return math.all(math.isfinite(matrix.c0)) && math.all(math.isfinite(matrix.c1)) &&
                   math.all(math.isfinite(matrix.c2)) && math.all(math.isfinite(matrix.c3));
        }
    }
}

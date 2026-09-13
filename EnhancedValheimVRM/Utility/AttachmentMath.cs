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

        public static bool TryMapSocket(float4x4 parentToWorld,
            quaternion parentRotation,
            quaternion socketLocalRotation,
            float3 originalOffset,
            float ratio,
            out float3 localPosition)
        {
            localPosition = float3.zero;
            if (!math.isfinite(ratio) || ratio <= 0 || !Finite(parentToWorld)) return false;
            var determinant = math.determinant(parentToWorld);
            if (!math.isfinite(determinant) || math.abs(determinant) < 1e-12f) return false;
            var socketWorldRotation = math.mul(parentRotation, socketLocalRotation);
            var worldOffset = math.rotate(socketWorldRotation, originalOffset * ratio);
            // w=0 transforms a displacement, excluding the parent's translation.
            localPosition = math.mul(math.inverse(parentToWorld), new float4(worldOffset, 0)).xyz;
            return math.all(math.isfinite(localPosition));
        }

        public static bool TryLocalScale(float4x4 parentToWorld,
            quaternion localRotation,
            float3 worldScale,
            out float3 localScale)
        {
            localScale = new float3(1);
            if (!Finite(parentToWorld) || !math.all(math.isfinite(worldScale))) return false;
            // Column lengths include ancestor scale and the child's orientation. Simple
            // component-wise division by parent.lossyScale is wrong for rotated children
            // under a nonuniformly scaled parent.
            var axes = math.mul(new float3x3(parentToWorld), new float3x3(localRotation));
            var lengths = new float3(math.length(axes.c0), math.length(axes.c1), math.length(axes.c2));
            if (!math.all(math.isfinite(lengths)) || math.any(lengths < 1e-6f)) return false;
            localScale = worldScale / lengths;
            return math.all(math.isfinite(localScale));
        }

        public static bool TryMapItemOffset(float4x4 socketToWorld,
            quaternion socketRotation,
            float3 offsetInMeters,
            float ratio,
            out float3 localPosition)
        {
            // Settings express physical distances along socket axes. Convert those
            // distances into the socket's import units instead of scaling them twice.
            return TryMapSocket(socketToWorld,
                socketRotation,
                quaternion.identity,
                offsetInMeters,
                ratio,
                out localPosition);
        }

        public static float3 ToBoneOffset(quaternion boneRotation, float3 worldOffset, float3 adjustment)
        {
            return math.rotate(math.inverse(boneRotation), worldOffset) + adjustment;
        }

        public static float3 PlaceBone(float3 handPosition, quaternion boneRotation, float3 offset)
        {
            return handPosition + math.rotate(boneRotation, offset);
        }

        public static float3 SeatedHipCorrection(float3 vanillaHips, float3 avatarHips, quaternion seatRotation)
        {
            var delta = math.rotate(math.inverse(seatRotation), vanillaHips - avatarHips);
            delta.x = 0; // dont touch side to side
            return math.rotate(seatRotation, delta);
        }

        // in: x ground to hip, y hip bone to bottom of thigh mesh, z shin bone to back of calf mesh
        // out: x 1 = measured (0 = hips only), y thigh depth diff, z calf thickness diff
        // dont use leg length for height, unity already scales the hips by it when retargeting
        public static float3 GetSeatProportions(float3 vanilla, float3 avatar)
        {
            if (!math.all(math.isfinite(vanilla)) || !math.all(math.isfinite(avatar)) ||
                math.any(vanilla <= 0) || math.any(avatar <= 0))
                return float3.zero;
            return new float3(1, avatar.y - vanilla.y, avatar.z - vanilla.z);
        }

        public static float3 SeatedProportionCorrection(float3 vanillaHips,
            float3 avatarHips,
            float3 vanillaKnees,
            float3 avatarKnees,
            float3 seatOrigin,
            quaternion seatRotation,
            float3 proportions,
            bool forwardOnly)
        {
            if (proportions.x <= 0) return SeatedHipCorrection(vanillaHips, avatarHips, seatRotation);
            var inverse = math.inverse(seatRotation);
            var vanillaSeated = math.rotate(inverse, vanillaHips - seatOrigin);
            var avatarSeated = math.rotate(inverse, avatarHips - seatOrigin);
            var vanillaKneeSeated = math.rotate(inverse, vanillaKnees - seatOrigin);
            var avatarKneeSeated = math.rotate(inverse, avatarKnees - seatOrigin);
            var delta = vanillaSeated - avatarSeated;
            // bottom of the vrm thigh goes where the game models thigh bottom is
            delta.y += proportions.y;
            // knees line up so the back of the calf lands on the seat edge, thicker calf moves forward
            delta.z = vanillaKneeSeated.z - avatarKneeSeated.z + proportions.z;
            // taller avatars do not move back
            if (forwardOnly) delta.z = math.max(0, delta.z);
            delta.x = 0;
            return math.rotate(seatRotation, delta);
        }


        private static bool Finite(float4x4 matrix)
        {
            return math.all(math.isfinite(matrix.c0)) && math.all(math.isfinite(matrix.c1)) &&
                math.all(math.isfinite(matrix.c2)) && math.all(math.isfinite(matrix.c3));
        }
    }
}

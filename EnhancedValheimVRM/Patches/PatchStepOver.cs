using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // the game has no step height. what you walk over is whatever the round bottom of the vanilla
    // capsule rolls up onto: OnCollisionStay calls a contact ground when it sits lower than the
    // capsule radius and the surface pushes upward at all, and physics does the lifting. the avatar
    // capsule is sized to the avatar so it fits under things, and a thin one meets a step side on,
    // so nothing lifts it. this gives every avatar the vanilla band: a contact below the vanilla
    // radius counts as ground, and the push a vanilla sphere would have got from it is applied,
    // its normal is where that sphere's centre would be
    [HarmonyPatch(typeof(Character), "OnCollisionStay")]
    internal static class PatchStepOver
    {
        // Player.prefab capsule: radius 0.49, height 1.85, centre 0.925, bottom on the feet
        private const float VanillaRadius = 0.49f;

        // how far over the games own rise an edge carries you. 1 is what physics gives a solid sphere
        private const float Bump = 1.15f;

        // /vrm dev stepover | sensor on | off. not saved
        internal static bool Enabled = true, UseSensor = true;

        private static readonly AccessTools.FieldRef<Character, bool> GroundContact =
            AccessTools.FieldRefAccess<Character, bool>("m_groundContact");

        private static readonly AccessTools.FieldRef<Character, Vector3> GroundContactNormal =
            AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactNormal");

        private static readonly AccessTools.FieldRef<Character, Vector3> GroundContactPoint =
            AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactPoint");

        private static readonly AccessTools.FieldRef<Character, Collider> LowestContactCollider =
            AccessTools.FieldRefAccess<Character, Collider>("m_lowestContactCollider");

        private static readonly AccessTools.FieldRef<Character, float> StandUp =
            AccessTools.FieldRefAccess<Character, float>("m_standUp");

        private static readonly AccessTools.FieldRef<Character, float> JumpTimer =
            AccessTools.FieldRefAccess<Character, float>("m_jumpTimer");

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body =
            AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private static readonly AccessTools.FieldRef<Character, Vector3> CurrentVelocity =
            AccessTools.FieldRefAccess<Character, Vector3>("m_currentVel");

        private static void Postfix(Character __instance, Collision collision)
        {
            if (!Enabled || !(__instance is Player player) || player.GetVrmInstance() == null) return;
            if (!__instance.IsOwner() || JumpTimer(__instance) < 0.1f) return;
            var feet = __instance.transform.position;
            var body = Body(__instance);
            var contacts = collision.contacts;
            foreach (var contact in contacts)
            {
                var height = contact.point.y - feet.y;
                // skip the floor under your feet and anything 0.49 m or higher, only low edges get stepped over.
                // higher ones broke the maths below and got people stuck on walls
                if (height <= 0f || height >= VanillaRadius) continue;
                var facing = contact.normal;
                if (facing.y < 0f) facing.y = -facing.y;
                // tried a vanilla sized sphere (Physics.ComputePenetration) instead of the ring. no wall test
                // needed but it boosts way over the game even with the fixed lift, the ring is closer and cheaper
                // to how vanilla feels
                // a wall, the vanilla sphere would have met this collider flat on and gone nowhere
                if (UseSensor && WallSensor.Touching(player, collision.collider)) continue;
                // anything else this low is an edge the vanilla sphere rides up. facing up it keeps its
                // own normal, side on it gets the normal the sphere meets an edge at height h with,
                // leaning up by (radius - h) / radius
                var normal = facing;
                if (facing.y <= 0.1f)
                {
                    var up = (VanillaRadius - height) / VanillaRadius;
                    var outward = new Vector3(facing.x, 0f, facing.z).normalized;
                    normal = outward * Mathf.Sqrt(1f - up * up) + Vector3.up * up;
                }

                if (normal.y <= 0.1f) continue;
                if (normal.y > GroundContactNormal(__instance).y || !GroundContact(__instance))
                {
                    if (StandUp(__instance) == -100f) StandUp(__instance) = 2f;
                    GroundContact(__instance) = true;
                    GroundContactNormal(__instance) = normal;
                    GroundContactPoint(__instance) = contact.point;
                    LowestContactCollider(__instance) = collision.collider;
                }

                if (body == null) continue;
                // what the vanilla sphere gets from this edge: the game sets the sideways speed every step and
                // leaves the vertical alone, the contact takes away the part of that pointing into the surface.
                // the rise already there counts, so it settles on sliding along the surface, under the games
                // 3 m/s cap on a low edge. a sloped contact gets this from physics, a straight edge gets nothing
                var wanted = CurrentVelocity(__instance);
                var sideways = new Vector3(normal.x, 0f, normal.z);
                var into = Vector3.Dot(new Vector3(wanted.x, 0f, wanted.z), -sideways.normalized);
                if (into <= 0.05f) continue;
                var velocity = body.linearVelocity;
                var push = into * sideways.magnitude;
                var closing = push * Bump - velocity.y * normal.y;
                if (closing <= 0f) continue;
                // never more in one step than the first version added, that one ignored the rise and went
                // straight to the cap
                var lifted = Mathf.Min(3f, velocity.y + Mathf.Min(closing, push) * normal.y);
                if (lifted > velocity.y) body.linearVelocity = new Vector3(velocity.x, lifted, velocity.z);
            }
        }
    }
}

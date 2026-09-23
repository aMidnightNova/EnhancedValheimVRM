using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // bows and staffs shoot from a fixed height above the feet, this scales it with the avatar.
    // forward is left alone, summon staffs use it as the distance to put the summon at
    [HarmonyPatch(typeof(Attack), "GetProjectileSpawnPoint")]
    internal static class PatchAttackGetProjectileSpawnPoint
    {
        private static void Postfix(Humanoid ___m_character, ref Vector3 spawnPoint)
        {
            if (!(___m_character is Player player)) return;
            var settings = player.GetVrmInstance()?.GetSettings();
            if (settings == null) return;
            var root = player.transform;
            var local = root.InverseTransformPoint(spawnPoint);
            local.x *= settings.PlayerVrmScale;
            local.y *= settings.PlayerVrmScale;
            spawnPoint = root.TransformPoint(local);
        }
    }
}

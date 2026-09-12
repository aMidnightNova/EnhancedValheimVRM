using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Character), "SetVisible")]
    internal static class PatchCharacterSetVisible
    {
        private static void Postfix(Character __instance, bool visible)
        {
            if (!(__instance is Player player)) return;
            var vrm = player.GetVrmInstance();
            // No installed/shared avatar, an in-flight import, and corpse transfer
            // are all normal states. This hook can run every frame.
            if (vrm == null || vrm.IsCorpse) return;
            var model = vrm.GetGameObject();
            if (model == null) return;
            var lod = model.GetComponent<LODGroup>();
            if (lod == null) return;
            lod.localReferencePoint = visible ? vrm.LodReferencePoint : new Vector3(999999f, 999999f, 999999f);
        }
    }
}

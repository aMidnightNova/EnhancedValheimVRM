using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Character), "GetHeadPoint")]
    internal static class PatchCharacterGetHeadPoint
    {
        private static bool Prefix(Character __instance, ref Vector3 __result)
        {
            if (!__instance.IsPlayer()) return true;

            var player = __instance as Player;
            // A dead player's own object keeps sliding from the killing blow until respawn;
            // its name tag belongs on the corpse.
            var vrmInstance = player.GetVrmInstance() ?? VrmController.FindCorpseFor(player);

            if (vrmInstance != null)
            {
                var vrmGo = vrmInstance.GetGameObject();
                if (vrmGo == null) return true;

                var vrmGoAnimator = vrmGo.GetComponentInChildren<Animator>();

                if (vrmGoAnimator == null || vrmGoAnimator.avatar == null) return true;

                var head = vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head);

                if (head == null) return true;

                __result = head.position;
                return false;
            }

            return true;
        }
    }
}

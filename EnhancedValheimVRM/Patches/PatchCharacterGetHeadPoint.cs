using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Character), "GetHeadPoint")]
    internal static class PatchCharacterGetHeadPoint
    {
        private static bool Prefix(Character __instance, ref Vector3 __result)
        {
            if (!__instance.IsPlayer())
            {
                return true;
            }

            var player = __instance as Player;
            var vrmInstance = player.GetVrmInstance();

            if (vrmInstance != null)
            {
                var vrmGo = vrmInstance.GetGameObject();
                if (vrmGo == null)
                {
                    Logger.LogError("VrmGo Is Null GetHeadPoint");
                    return true;
                }
                var vrmGoAnimator = vrmGo.GetComponentInChildren<Animator>();

                if (vrmGoAnimator == null || vrmGoAnimator.avatar == null)
                {
                    return true;
                }

                var head = vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head);

                if (head == null)
                {
                    return true;
                }

                __result = head.position;
                return false;
            }

            return true;
        }
    }
}
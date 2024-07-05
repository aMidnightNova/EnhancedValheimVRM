using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(GameCamera), "GetCameraBaseOffset")]
    internal static class PatchGameCameraGetCameraBaseOffset
    {
        private static bool Prefix(GameCamera __instance, Player player, ref Vector3 __result)
        {
            if (player.InBed())
            {
                __result = player.GetHeadPoint() - player.transform.position;
                return false;
            }

            var vrmInstance = player.GetVrmInstance();
            var settings = vrmInstance.GetSettings();
            var vrmAnimator = vrmInstance.GetVrmGoAnimator();

            // 0.3f is a magic number used in valhiem. it looks like its just there default camera offset number.
            // this number is  getting scaled here to stay scaled with the vrm height. 
            var scaledDistance = (Vector3.up * 0.3f) * settings.PlayerVrmScale;
            var vrmEye = vrmAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
            
            __result = player.IsAttached() || player.IsSitting() ? player.GetHeadPoint() + scaledDistance - player.transform.position : vrmEye.transform.position - player.transform.position;

            return false;
        }
    }
}
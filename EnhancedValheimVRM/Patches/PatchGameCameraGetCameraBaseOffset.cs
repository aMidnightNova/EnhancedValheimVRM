using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(GameCamera), "GetCameraBaseOffset")]
    internal static class PatchGameCameraGetCameraBaseOffset
    {
        private static bool Prefix(GameCamera __instance, Player player, ref Vector3 __result)
        {
            if (player == null) return true;
            if (player.InBed())
            {
                __result = player.GetHeadPoint() - player.transform.position;
                return false;
            }

            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance == null) return true;
            var settings = vrmInstance.GetSettings();
            if (!settings.FixCameraHeight) return true;
            var vrmAnimator = vrmInstance.GetVrmGoAnimator();

            // 0.3f is a magic number used in valhiem. it looks like its just there default camera offset number.
            // this number is  getting scaled here to stay scaled with the vrm height. 
            var scaledDistance = Vector3.up * 0.3f * settings.PlayerVrmScale;
            if (vrmAnimator == null) return true;
            var vrmEye = vrmAnimator.GetBoneTransform(HumanBodyBones.LeftEye)
                ?? vrmAnimator.GetBoneTransform(HumanBodyBones.Head)
                ?? vrmAnimator.GetBoneTransform(HumanBodyBones.Neck);
            if (vrmEye == null) return true;

            __result = player.IsAttached() || player.IsSitting()
                ? player.GetHeadPoint() + scaledDistance - player.transform.position
                : vrmEye.transform.position - player.transform.position;

            return false;
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PatchPlayerOnDeath
    {
 
        private static void Postfix(Player __instance)
        {
            var vrmInstance = __instance.GetVrmInstance();
            if (vrmInstance == null) return;
            var settings = vrmInstance.GetSettings();

            if (settings.FixCameraHeight)
            {
                
                Object.Destroy(__instance.GetComponent<VrmEyeAnimator>());
            }
            
            
        }
    }
}
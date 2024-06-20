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
            var settings = vrmInstance.GetSettings();

            if (settings.FixCameraHeight)
            {
                
                Logger.Log("______________ Destroy EYE");
                Object.Destroy(__instance.GetComponent<VrmEyeAnimator>());
            }
            
            
        }
    }
}
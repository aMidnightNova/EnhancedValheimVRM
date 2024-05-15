using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "Awake")]
    public class PatchPlayerAwake
    {
        static void Postfix(Player __instance)
        {
            VrmController.AttachVrmToPlayer(__instance);
        }
    }
}
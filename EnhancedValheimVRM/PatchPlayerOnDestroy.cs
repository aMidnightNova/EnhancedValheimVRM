using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    public class PatchPlayerOnDestroy
    {
        static void Prefix(Player player)
        {
            VrmController.DetachVrmFromPlayer(player);
        }
    }
}
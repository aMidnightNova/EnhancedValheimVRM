using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class PatchPlayerOnDestroy
    {
        private static void Prefix(Player __instance)
        {
            VrmController.DetachVrmFromPlayer(__instance);
        }
    }
}
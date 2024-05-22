using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    public class PatchPlayerOnDestroy
    {
        static void Prefix(Player __instance)
        {
            VrmController.DetachVrmFromPlayer(__instance);
        }
    }
}
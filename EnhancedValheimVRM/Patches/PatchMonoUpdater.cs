using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
    public static class PatchMonoLateUpdate
    {
        public static void Postfix(MonoUpdaters __instance)
        {
            float deltaTime = Time.deltaTime;
            if (__instance.TryGetField("m_update", out List<IMonoUpdater> m_update))
            {
                m_update.CustomLateUpdate(VrmAnimator.Instances, "MonoUpdaters.LateUpdate.VrmAnimator", deltaTime);
            }
            else
            {
                Logger.LogError("Failed to get m_update field from MonoUpdaters instance.");
            }
        }
    }

    // [HarmonyPatch(typeof(MonoUpdaters), "Update")]
    // public static class PatchMonoUpdate
    // {
    //     public static void Postfix(MonoUpdaters __instance)
    //     {
    //     }
    // }
    //
    // [HarmonyPatch(typeof(MonoUpdaters), "FixedUpdate")]
    // public static class PatchMonoFixedUpdate
    // {
    //     public static void Postfix(MonoUpdaters __instance)
    //     {
    //     }
    // }
}
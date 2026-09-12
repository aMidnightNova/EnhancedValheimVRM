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
                Logger.LogOnce("mono-updater-field", "Failed to get m_update field from MonoUpdaters instance.");
            }
        }
    }
}
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(MonoUpdaters), "LateUpdate")]
    class PatchMonoUpdater
    {
        public static void Postfix()
        {
            foreach (var vrmAnimator in VrmAnimator.Instances)
            {
                vrmAnimator.CustomLateUpdate();
            }
        }
    }
}
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    internal static class PatchFejdStartup
    {
        public static void Apply(Harmony harmony)
        {
            // Dedicated builds do not need the menu bootstrap. Resolve it only if
            // that type is present, without touching any client avatar types.
            var startupType = typeof(ZNet).Assembly.GetType("FejdStartup", false);
            var originalMethod = startupType?.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            if (originalMethod == null) return;

            var postfix = new HarmonyMethod(typeof(PatchFejdStartup), nameof(Postfix));

            harmony.Patch(originalMethod, null, postfix);
        }

        private static void Postfix()
        {
            EnhancedValheimVrmPlugin.PatchAll();
        }
    }
}

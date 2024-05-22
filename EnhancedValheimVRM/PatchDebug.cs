using System;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class PatchDebug
    {
        private const string Prepend = "[EnhancedValheimVRM]";

        [HarmonyPatch(typeof(Debug), "Log",  typeof(object))]
        [HarmonyPriority(Priority.First)]
        public class DebugLogPatch
        {
            static bool Prefix(ref object message)
            {
                message = $"{Prepend} {message}";
                return true;
            }
        }

        [HarmonyPatch(typeof(Debug), "LogError", typeof(object))]
        [HarmonyPriority(Priority.First)]
        public class DebugLogErrorPatch
        {
            static bool Prefix(ref object message)
            {
                message = $"{Prepend} {message}";
                return true;
            }
        }

        [HarmonyPatch(typeof(Debug), "LogWarning", typeof(object))]
        [HarmonyPriority(Priority.First)]
        public class DebugLogWarningPatch
        {
            static bool Prefix(ref object message)
            {
                message = $"{Prepend} {message}";
                return true;
            }
        }
    }
}
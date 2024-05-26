using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Logger
    {
        private static readonly string Prepend = $"[{Constants.PluginName}]";

        public static void Log(object message)
        {
            Debug.Log($"{Prepend} {message}");
        }

        public static void LogError(object message)
        {
            Debug.LogError($"{Prepend} {message}");
        }

        public static void LogWarning(object message)
        {
            Debug.LogWarning($"{Prepend} {message}");
        }
    }
}
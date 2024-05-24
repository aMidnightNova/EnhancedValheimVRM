using UnityEngine;

namespace EnhancedValheimVRM
{
    public class Logger
    {
        private const string Prepend = "[EnhancedValheimVRM]";

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
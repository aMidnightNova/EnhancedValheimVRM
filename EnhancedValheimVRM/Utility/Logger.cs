using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Logger
    {
        private static readonly string Prepend = $"[{Constants.PluginName}]";

        public enum LogLevel
        {
            None = 0,
            Info = 1,
            Debug = 2,
            All = 3,
            Override = -1
        }

        private static bool LogLevelCheck(LogLevel level)
        {
            
            // If level is Override, always log
            if (level == LogLevel.Override) return true;
            
            // If logging is disabled, return false
            if (Settings.LogLevel == LogLevel.None) return false;

            // Proceed if the current log level is greater than or equal to the specified level
            return Settings.LogLevel >= level;
        }

        public static void Log(object message, LogLevel level = LogLevel.Override)
        {
            if (!LogLevelCheck(level)) return;

            Debug.Log($"{Prepend} {message}");
        }
        
        public static void LogWarning(object message, LogLevel level = LogLevel.Override)
        {
            if (!LogLevelCheck(level)) return;
            
            Debug.LogWarning($"{Prepend} {message}");
        }
        
        public static void LogError(object message, LogLevel level = LogLevel.Override)
        {
            if (!LogLevelCheck(level)) return;

            Debug.LogError($"{Prepend} {message}");
        }
    }
}
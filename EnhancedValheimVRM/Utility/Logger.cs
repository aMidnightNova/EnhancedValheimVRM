using System.Collections.Concurrent;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Logger
    {
        private static readonly string Prepend = $"[{Constants.PluginName}]";
        private static readonly ConcurrentDictionary<string, byte> Reported = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentQueue<string> Order = new ConcurrentQueue<string>();
        private const int MaximumOnceKeys = 512;

        public enum LogLevel
        {
            None = 0,
            Info = 1,
            Debug = 2,
            All = 3,
            Override = -1
        }

        private static bool Enabled(LogLevel level)
        {
            return level == LogLevel.Override ||
                (Settings.LogLevel != LogLevel.None &&
                    Settings.LogLevel >= level);
        }

        public static void Log(object message, LogLevel level = LogLevel.Override)
        {
            if (Enabled(level)) Debug.Log($"{Prepend} {message}");
        }

        public static void LogWarning(object message, LogLevel level = LogLevel.Override)
        {
            if (Enabled(level)) Debug.LogWarning($"{Prepend} {message}");
        }

        public static void LogError(object message, LogLevel level = LogLevel.Override)
        {
            if (Enabled(level)) Debug.LogError($"{Prepend} {message}");
        }

        public static void LogOnce(string key, object message, LogLevel level = LogLevel.Override)
        {
            if (!Enabled(level) || !Reported.TryAdd(key, 0)) return;
            Order.Enqueue(key);
            while (Reported.Count > MaximumOnceKeys && Order.TryDequeue(out var oldest))
                Reported.TryRemove(oldest, out _);
            Debug.Log($"{Prepend} {message}");
        }

        internal static void ResetOnce()
        {
            Reported.Clear();
            while (Order.TryDequeue(out _)) { }
        }
    }
}

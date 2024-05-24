using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace EnhancedValheimVRM
{
    public class TimeElapsedHelper

    {
        private static DateTime startTime;
        private static DateTime lastAccessTime;
        private static bool isStartTimeSet = false;

        public static void SetStartTime()
        {
            startTime = DateTime.UtcNow;
            lastAccessTime = DateTime.UtcNow;
            isStartTimeSet = true;
            Logger.Log("Start time set.");
        }

        public static string GetElapsedTime()
        {
            if (!isStartTimeSet)
            {
                Logger.LogWarning("Start time not set. Automatically calling SetStartTime().");
                SetStartTime();
            }

            DateTime currentTime = DateTime.UtcNow;
            TimeSpan timeSinceLastAccess = currentTime - lastAccessTime;
            TimeSpan totalTimeElapsed = currentTime - startTime;

            lastAccessTime = currentTime;

            string timeSinceLastAccessFormatted = $"{Math.Round(timeSinceLastAccess.TotalMilliseconds, 2)}ms";
            string totalTimeElapsedFormatted = $"{Math.Round(totalTimeElapsed.TotalSeconds, 2)}s";

            return $" Time From Last: {timeSinceLastAccessFormatted} | Total Time Elapsed: {totalTimeElapsedFormatted}";
        }
    }


    public class Timer : IDisposable
    {
        private Stopwatch _stopwatch;
        private string _name;

        public Timer(string name)
        {
            _name = name;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Logger.Log($"{_name} took {_stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
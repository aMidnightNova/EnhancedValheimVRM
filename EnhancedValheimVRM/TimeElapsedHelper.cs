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
        }

        public static string GetElapsedTime()
        {
            if (!isStartTimeSet)
            {
                Debug.LogError("Start time not set. Call SetStartTime() before getting elapsed time.");
                return "Error: Start time not set.";
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
        private Stopwatch stopwatch;
        private string name;

        public Timer(string name)
        {
            this.name = name;
            this.stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            stopwatch.Stop();
            Debug.Log($"{name} took {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
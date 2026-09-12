using System;
using System.Diagnostics;
using System.Threading;

namespace EnhancedValheimVRM.Sharing
{
    // Each transfer owns an instance; concurrent download slots are paced independently.
    public sealed class BandwidthLimiter
    {
        private readonly int _bytesPerSecond;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new object();
        private long _nextWrite;

        public BandwidthLimiter(int bytesPerSecond)
        {
            if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
            _bytesPerSecond = bytesPerSecond;
        }

        public void WaitForBytes(int count, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            long due;
            lock (_gate)
            {
                // Idle time and blocked socket writes do not accumulate burst credit.
                long duration = (long)Math.Ceiling(count * (double)Stopwatch.Frequency / _bytesPerSecond);
                due = Math.Max(_clock.ElapsedTicks, _nextWrite) + duration;
                _nextWrite = due;
            }
            while (true)
            {
                double remaining = (due - _clock.ElapsedTicks) * 1000.0 / Stopwatch.Frequency;
                if (remaining <= 0) break;
                if (cancellation.WaitHandle.WaitOne((int)Math.Ceiling(remaining)))
                    cancellation.ThrowIfCancellationRequested();
            }
            cancellation.ThrowIfCancellationRequested();
        }
    }
}

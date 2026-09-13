using System;

namespace EnhancedValheimVRM.Sharing
{
    public sealed class SharingDownloadPolicy
    {
        public int BytesPerSecond
        {
            get;
        }

        public int Slots
        {
            get;
        }


        public SharingDownloadPolicy(int megabitsPerSecond, int slots)
        {
            if (slots < 1 || slots > 32) throw new ArgumentOutOfRangeException(nameof(slots));
            BytesPerSecond = ToBytesPerSecond(megabitsPerSecond);
            Slots = slots;
        }

        public static int ToBytesPerSecond(int megabitsPerSecond)
        {
            if (megabitsPerSecond < 1 || megabitsPerSecond > 10000)
                throw new ArgumentOutOfRangeException(nameof(megabitsPerSecond));
            return megabitsPerSecond * 125000;
        }
    }
}

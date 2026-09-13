using System;

namespace EnhancedValheimVRM.Sharing
{
    // Upload rules shared by the client and the dedicated server.
    // The client caps itself at HardLimitMbps, the server announces its own per-upload limit over
    // RPC and the client uses the lower of the two. HardLimitMbps is the ceiling for both sides;
    // the server also enforces it on the receiving socket.
    public static class SharingUploadPolicy
    {
        public const int HardLimitMbps = 100;
        public const int DefaultServerMbps = 60;

        public const int DefaultClientMbps = 6;

        public static int ClampServer(int megabitsPerSecond)
        {
            return Math.Max(1, Math.Min(megabitsPerSecond, HardLimitMbps));
        }

        // serverMegabitsPerSecond is 0 until the server has announced its limit.
        public static int Resolve(int clientMegabitsPerSecond, int serverMegabitsPerSecond)
        {
            var result = Math.Max(1, Math.Min(clientMegabitsPerSecond, HardLimitMbps));
            if (serverMegabitsPerSecond > 0) result = Math.Min(result, ClampServer(serverMegabitsPerSecond));
            return result;
        }

        public static int ToBytesPerSecond(int megabitsPerSecond)
        {
            if (megabitsPerSecond < 1 || megabitsPerSecond > HardLimitMbps)
                throw new ArgumentOutOfRangeException(nameof(megabitsPerSecond));
            return megabitsPerSecond * 125000;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;

internal static class UploadPolicyChecks
{
    // The server paces what it reads, so a client asking for more than the server allows is
    // slowed by the socket rather than trusted.
    public static async Task Run(string root, Action<bool, string> check)
    {
        var storage = Path.Combine(root, "uploads-" + Guid.NewGuid().ToString("N"));
        using (var stop = new CancellationTokenSource())
        using (var server = new AvatarTcpServer(storage, IPAddress.Loopback, 0, new SharingDownloadPolicy(25, 4)))
        {
            server.UploadMbps = 2;
            var serving = server.RunAsync(stop.Token);
            try
            {
                var payload = new byte[375000];
                new Random(7).NextBytes(payload);
                var key = BundleCrypto.GenerateKey();
                var encrypted = TestBundle.Encrypt(BundleCrypto.PackAvatar(payload), key, 779);
                var info = new BundleInfo { Hash = BundleCrypto.Hash(encrypted), Version = new string('a', 64) };
                // The client believes it may send at 6 Mbps; the server only accepts 2 Mbps.
                var client = new AvatarTcpClient("127.0.0.1", server.Port, 750000);
                var ticket = server.AuthorizeTransfer("uploader", 779, SharingWire.AvatarKind, info, true);
                var timer = Stopwatch.StartNew();
                client.UploadBlob(779, ticket, encrypted, default);
                var seconds = timer.Elapsed.TotalSeconds;
                var minimumSeconds = encrypted.Length / 250000.0;
                check(server.ReadCurrent(779)?.Hash == info.Hash, "Paced upload was not stored");
                check(seconds >= minimumSeconds - 0.05, "Server let an upload exceed its per-upload limit");
                check(seconds < minimumSeconds * 1.75 + 0.5, "Server paced an upload far below its per-upload limit");
                Console.WriteLine("Upload policy: server limit 2 Mbps against a 6 Mbps client; " +
                    encrypted.Length + " bytes took " + seconds.ToString("F2") + "s (minimum " +
                    minimumSeconds.ToString("F2") + "s).");
            }
            finally
            {
                stop.Cancel();
                try
                {
                    await serving;
                }
                catch (OperationCanceledException) { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;

internal static class DownloadPolicyChecks
{
    public static async Task Run(string root, SharingDownloadPolicy policy, Action<bool, string> check)
    {
        var storage = Path.Combine(root, "downloads-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(storage, "777");
        Directory.CreateDirectory(directory);
        // Seed an opaque committed blob: this test isolates serving from upload pacing.
        var payload = new byte[policy.BytesPerSecond * 2];
        new Random(42).NextBytes(payload);
        var hash = BundleCrypto.Hash(payload);
        File.WriteAllBytes(Path.Combine(directory, hash + ".vrm.bundle"), payload);
        File.WriteAllBytes(Path.Combine(directory, hash + ".settings.bundle"), payload);
        var info = new BundleInfo { Version = hash, Hash = hash, ProfileVersion = hash, ProfileHash = hash };
        using (var file = File.Create(Path.Combine(directory, "current")))
        using (var writer = new BinaryWriter(file))
            info.Write(writer);

        using (var stop = new CancellationTokenSource())
        using (var server = new AvatarTcpServer(storage, IPAddress.Loopback, 0, policy))
        {
            var serving = server.RunAsync(stop.Token);
            var readers = new List<Download>();
            try
            {
                for (var i = 0; i < policy.Slots; i++)
                {
                    var download = new Download(server.Port,
                        server.AuthorizeTransfer("recipient", 777, SharingWire.AvatarKind, info, false));
                    readers.Add(download);
                    check(download.Accepted, "Configured download slot " + i + " was not available");
                }

                // Metadata access is worker-side RPC storage, independent of download slots.
                check(server.ReadCurrent(777)?.Hash == hash, "Full download slots blocked metadata");
                var client = new AvatarTcpClient("127.0.0.1", server.Port, 750000);
                var upload = TestBundle.Encrypt(BundleCrypto.PackAvatar(new byte[32]),
                    BundleCrypto.GenerateKey(),
                    778);
                var uploadInfo = new BundleInfo { Hash = BundleCrypto.Hash(upload), Version = new string('a', 64) };
                var ticket = server.AuthorizeTransfer("uploader", 778, SharingWire.AvatarKind, uploadInfo, true);
                client.UploadBlob(778, ticket, upload, default);
                check(server.ReadCurrent(778)?.Hash == uploadInfo.Hash, "Full download slots blocked uploads");

                var elapsed = await Task.WhenAll(readers.Select(reader => Task.Run(() => reader.ReadAll(payload))));
                var minimumSeconds = payload.Length / (double)policy.BytesPerSecond;
                foreach (var seconds in elapsed)
                {
                    check(seconds >= minimumSeconds - 0.03, "A download exceeded its configured per-slot rate");
                    // A shared budget would take Slots times as long. Allow timing slack,
                    // but verify that each slot gets its own rate instead of sharing it.
                    check(seconds < minimumSeconds * 1.75 + 0.1,
                        "Concurrent downloads shared a bandwidth budget or stalled");
                }

                // Completed transfers must release their slots.
                foreach (var reader in readers) reader.Dispose();
                readers.Clear();
                using (var next = new Download(server.Port,
                           server.AuthorizeTransfer("recipient", 777, SharingWire.AvatarKind, info, false)))
                    check(next.Accepted, "Completed download did not release its slot");
                Console.WriteLine("Download policy: " + policy.Slots + " slots at " +
                    policy.BytesPerSecond * 8 / 1000000 +
                    " Mbps each; concurrent transfers completed in " + elapsed.Max().ToString("F2") +
                    "s.");
            }
            finally
            {
                foreach (var reader in readers) reader.Dispose();
                stop.Cancel();
                try
                {
                    await serving;
                }
                catch (OperationCanceledException) { }
            }
        }
    }

    private sealed class Download : IDisposable
    {
        private readonly TcpClient _client;
        private readonly BinaryReader _reader;
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private readonly int _length;

        public bool Accepted { get; }

        public Download(int port, string hash)
        {
            _client = new TcpClient("127.0.0.1", port) { ReceiveTimeout = 30000 };
            var stream = _client.GetStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(SharingWire.Magic);
                writer.Write(SharingWire.Download);
                writer.Write(777L);
                SharingWire.WriteText(writer, hash);
                writer.Flush();
            }

            _reader = new BinaryReader(stream);
            Accepted = _reader.ReadBoolean();
            if (Accepted) _length = _reader.ReadInt32();
        }

        public double ReadAll(byte[] expected)
        {
            var actual = _reader.ReadBytes(_length);
            if (!actual.SequenceEqual(expected)) throw new Exception("Throttled download payload mismatch");
            return _timer.Elapsed.TotalSeconds;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _client.Dispose();
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;

internal static class PruneChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var serverRoot = Path.Combine(root, "prune-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "prune-cache-" + Guid.NewGuid().ToString("N"));
        const long id = 4242;
        using (var stop = new CancellationTokenSource())
        using (var server = new AvatarTcpServer(serverRoot, IPAddress.Loopback, 0, new SharingDownloadPolicy(25, 4)))
        {
            var serving = server.RunAsync(stop.Token);
            try
            {
                var client = new AvatarTcpClient("127.0.0.1", server.Port, 750000);
                var key = BundleCrypto.GenerateKey();
                var serverDirectory = Path.Combine(serverRoot, id.ToString());

                BundleInfo Publish(byte[] vrm, string settings)
                {
                    var packed = BundleCrypto.PackAvatar(vrm);
                    var profile = BundleCrypto.PackProfile(settings, "");
                    var encrypted = TestBundle.Encrypt(packed, key, id);
                    var encryptedProfile = TestBundle.Encrypt(profile, key, id);
                    var info = new BundleInfo
                    {
                        Version = BundleCrypto.Version(packed, key),
                        Hash = BundleCrypto.Hash(encrypted),
                        ProfileVersion = BundleCrypto.Version(profile, key),
                        ProfileHash = BundleCrypto.Hash(encryptedProfile)
                    };
                    client.UploadBlob(id,
                        server.AuthorizeTransfer("owner", id, SharingWire.AvatarKind, info, true),
                        encrypted,
                        default);
                    client.UploadBlob(id,
                        server.AuthorizeTransfer("owner", id, SharingWire.ProfileKind, info, true),
                        encryptedProfile,
                        default);
                    return info;
                }

                void Fetch(BundleInfo info)
                {
                    client.Receive(id,
                        info,
                        key,
                        cache,
                        default,
                        server.AuthorizeTransfer("viewer", id, SharingWire.AvatarKind, info, false),
                        server.AuthorizeTransfer("viewer", id, SharingWire.ProfileKind, info, false),
                        null);
                }

                bool Holds(string directory, params string[] expected)
                {
                    var names = Directory.GetFiles(directory)
                        .Select(Path.GetFileName)
                        .OrderBy(n => n, StringComparer.Ordinal);
                    return names.SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal));
                }

                var first = Publish(new byte[] { 1, 2, 3, 4 }, "ModelScale=1");
                Fetch(first);
                var second = Publish(new byte[] { 5, 6, 7, 8, 9 }, "ModelScale=2");
                check(Holds(serverDirectory, "current", second.Hash + ".settings.bundle", second.Hash + ".vrm.bundle"),
                    "Server kept blobs of a superseded model version");
                Fetch(second);
                check(Holds(Path.Combine(cache, id.ToString()),
                        second.Hash + ".settings.bundle",
                        second.Hash + ".vrm.bundle"),
                    "Client cache kept blobs of a superseded model version");

                // leftovers from older builds go on the next server start
                File.WriteAllBytes(Path.Combine(serverDirectory, new string('e', 64) + ".vrm.bundle"),
                    new byte[] { 0 });
                File.WriteAllBytes(Path.Combine(serverDirectory, new string('e', 64) + ".settings.bundle"),
                    new byte[] { 0 });
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

        using (var stop = new CancellationTokenSource())
        using (var server = new AvatarTcpServer(serverRoot, IPAddress.Loopback, 0, new SharingDownloadPolicy(25, 4)))
        {
            var serving = server.RunAsync(stop.Token);
            try
            {
                await Task.Delay(50);
                var names = Directory.GetFiles(Path.Combine(serverRoot, id.ToString()))
                    .Select(Path.GetFileName)
                    .ToArray();
                check(names.Length == 3 && !names.Any(n => n.StartsWith(new string('e', 64))),
                    "Server start did not sweep leftover blobs from older builds");
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

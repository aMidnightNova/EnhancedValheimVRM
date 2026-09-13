using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;

internal static class Program
{
    private static int _assertions;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _assertions++;
    }

    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is CryptographicException || ex is InvalidDataException || ex is IOException)
        {
            _assertions++;
            return;
        }

        throw new Exception(message);
    }

    public static async Task Main(string[] args)
    {
        ThreadPool.GetMinThreads(out var workers, out var completionPorts);
        ThreadPool.SetMinThreads(Math.Max(workers, 32), completionPorts);
        var root = Path.Combine(Path.GetTempPath(), "evrm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (args.Contains("--zip-only"))
            {
                await ZipChecks.Run(root, Check);
                await RpcChecks.Run(root, Check);
                Console.WriteLine("PASS: " + _assertions + " ZIP/lifetime and RPC transfer checks.");
                return;
            }

            if (args.Contains("--rpc-only"))
            {
                await RpcChecks.Run(root, Check);
                await RpcChecks.RetryChecks(Check);
                Console.WriteLine("PASS: " + _assertions + " focused RPC/identity/retry checks.");
                return;
            }

            await OutfitChecks.Run(root, Check);
            var dedicatedDownloads = new SharingDownloadPolicy(25, 4);
            Check(dedicatedDownloads.BytesPerSecond == 3125000 && dedicatedDownloads.Slots == 4,
                "Dedicated download defaults");
            var customDownloads = new SharingDownloadPolicy(2, 3);
            Check(customDownloads.BytesPerSecond == 250000 && customDownloads.Slots == 3, "Custom download settings");
            await DownloadPolicyChecks.Run(root, dedicatedDownloads, Check);
            await DownloadPolicyChecks.Run(root, customDownloads, Check);
            Check(SharingEndpoint.ShouldHost(true, true, true),
                "Dedicated server must host TCP without a local player/menu");
            Check(!SharingEndpoint.ShouldHost(true, false, true),
                "Player-hosted sharing must stay disabled without verified NAT traversal");
            Check(!SharingEndpoint.ShouldHost(false, false, true), "Remote client must not host TCP");
            Check(!SharingEndpoint.ShouldHost(true, false, true), "Private single-player game must not host TCP");
            Check(!SharingEndpoint.ShouldHost(true, true, false), "Disabled TCP server started");
            Check(SharingEndpoint.ResolveClientHost("", "socket/192.0.2.10:2456") == "192.0.2.10",
                "Direct server IP discovery");
            Check(SharingEndpoint.ResolveClientHost("", "socket/valheim.example:2456") == "valheim.example",
                "Server hostname discovery");
            Check(SharingEndpoint.ResolveClientHost("", "steam/12345/192.0.2.10:2456") == "192.0.2.10",
                "Steam direct address discovery");
            Check(SharingEndpoint.ResolveClientHost("", "socket/[2001:db8::1]:2456") == "2001:db8::1",
                "IPv6 server discovery");
            Check(SharingEndpoint.ResolveClientHost("", "steam/12345/:0") == null,
                "Steam identity mistaken for TCP address");
            Check(SharingEndpoint.ResolveClientHost("", "playfab/12345") == null,
                "PlayFab identity mistaken for TCP address");
            Check(SharingEndpoint.ResolveClientHost(" custom.example ", "playfab/12345") == "custom.example",
                "Relay hostname override");
            var key = BundleCrypto.GenerateKey();
            var vrm = new byte[600000];
            RandomNumberGenerator.Fill(vrm);
            const long id = 1001;
            var packed = BundleCrypto.PackAvatar(vrm);
            var profile = BundleCrypto.PackProfile("ModelScale=1.25\nAllowShare=True", "");
            var encrypted = TestBundle.Encrypt(packed, key, id);
            var encryptedProfile = TestBundle.Encrypt(profile, key, id);
            string version = BundleCrypto.Version(packed, key), profileVersion = BundleCrypto.Version(profile, key);
            Check(BundleCrypto.IsValidKey(key), "Generated key invalid");
            Check(!BundleCrypto.IsValidKey("invalid"), "Invalid key accepted");
            Check(!encrypted.SequenceEqual(TestBundle.Encrypt(packed, key, id)), "IV was reused");
            Check(packed.SequenceEqual(BundleCrypto.Decrypt(encrypted, key, version, id)),
                "Encryption roundtrip failed");
            Reject(() => BundleCrypto.Decrypt(encrypted, BundleCrypto.GenerateKey(), version, id),
                "Wrong key accepted");
            Reject(() => BundleCrypto.Decrypt(encrypted, key, version, id + 1), "Wrong character accepted");
            Reject(() => BundleCrypto.Decrypt(encrypted, key, profileVersion, id), "Wrong version accepted");
            foreach (var offset in new[] { 0, 32, encrypted.Length - 1 })
            {
                var corrupted = (byte[])encrypted.Clone();
                corrupted[offset] ^= 1;
                Reject(() => BundleCrypto.Decrypt(corrupted, key, version, id), "Tampered bundle accepted");
            }

            Reject(() => SharingWire.ValidateHash("../../current"), "Path traversal accepted");
            Reject(() => SharingWire.ValidateKind("../vrm"), "Blob kind traversal accepted");
            using (var badFrame = new MemoryStream(BitConverter.GetBytes(SharingWire.MaxBundleBytes + 1)))
            using (var reader = new BinaryReader(badFrame))
                Reject(() => SharingWire.ReadBytes(reader, SharingWire.MaxBundleBytes), "Oversized frame accepted");

            using (var stop = new CancellationTokenSource())
            using (var server = new AvatarTcpServer(Path.Combine(root, "server"),
                       IPAddress.Loopback,
                       0,
                       new SharingDownloadPolicy(25, 4)))
            {
                var commits = new System.Collections.Concurrent.ConcurrentQueue<BundleInfo>();
                server.Uploaded += (session, uploadedId, uploaded) => commits.Enqueue(uploaded);
                var serving = server.RunAsync(stop.Token);
                var client = new AvatarTcpClient("127.0.0.1", server.Port, 750000);
                Check(server.ReadCurrent(id) == null, "Unpublished avatar advertised");
                var info = new BundleInfo
                {
                    Version = version,
                    Hash = BundleCrypto.Hash(encrypted),
                    ProfileVersion = profileVersion,
                    ProfileHash = BundleCrypto.Hash(encryptedProfile)
                };
                var uploadTicket = server.AuthorizeTransfer("owner", id, SharingWire.AvatarKind, info, true);
                var timer = Stopwatch.StartNew();
                client.UploadBlob(id, uploadTicket, encrypted, default);
                Check(timer.Elapsed.TotalSeconds >= encrypted.Length / 750000.0 - 0.02, "Upload exceeded 6 Mbps");
                Check(commits.TryDequeue(out var committed) && committed.Hash == info.Hash && !committed.HasProfile,
                    "Server did not notify the model upload");
                Reject(() => client.UploadBlob(id, uploadTicket, encrypted, default), "Upload ticket replay accepted");
                var stored = server.ReadCurrent(id);
                Check(stored.Version == version && !stored.HasProfile, "Model-only manifest lost or invented data");
                var profileTicket = server.AuthorizeTransfer("owner", id, SharingWire.ProfileKind, info, true);
                client.UploadBlob(id, profileTicket, encryptedProfile, default);
                Check(commits.TryDequeue(out committed) && committed.SameAs(info),
                    "Server did not merge the settings upload");
                Check(server.ReadCurrent(id).SameAs(info), "Version lookup lost unchanged content");
                var serverDirectory = Path.Combine(root, "server", id.ToString());
                Check(Directory.GetFiles(serverDirectory)
                        .Select(Path.GetFileName)
                        .OrderBy(n => n)
                        .SequenceEqual(new[] { "current", info.Hash + ".settings.bundle", info.Hash + ".vrm.bundle" }
                            .OrderBy(n => n)),
                    "Server blob names must be <modelhash>.vrm.bundle and <modelhash>.settings.bundle");
                var storedBytes = File.ReadAllBytes(Path.Combine(serverDirectory, info.Hash + ".vrm.bundle"));
                Check(BundleCrypto.Hash(storedBytes) == info.Hash && !storedBytes.SequenceEqual(packed),
                    "Server stored wrong/plaintext blob");

                // A settings change replaces the settings file in place under the same model hash.
                var profile2 = BundleCrypto.PackProfile("ModelScale=1.5\nAllowShare=True", "");
                var encryptedProfile2 = TestBundle.Encrypt(profile2, key, id);
                var info2 = info.Clone();
                info2.ProfileVersion = BundleCrypto.Version(profile2, key);
                info2.ProfileHash = BundleCrypto.Hash(encryptedProfile2);
                client.UploadBlob(id,
                    server.AuthorizeTransfer("owner", id, SharingWire.ProfileKind, info2, true),
                    encryptedProfile2,
                    default);
                Check(server.ReadCurrent(id).SameAs(info2) && Directory.GetFiles(serverDirectory).Length == 3,
                    "Settings update did not replace the settings blob in place");
                Reject(() => server.AuthorizeTransfer("owner",
                        id,
                        SharingWire.ProfileKind,
                        new BundleInfo
                        {
                            Version = version,
                            Hash = "",
                            ProfileVersion = info2.ProfileVersion,
                            ProfileHash = info2.ProfileHash
                        },
                        true),
                    "Settings blob authorized without a model hash to name it");

                var cache = Path.Combine(root, "cache");
                var downloadTicket = server.AuthorizeTransfer("recipient", id, SharingWire.AvatarKind, info2, false);
                var downloadProfile =
                    server.AuthorizeTransfer("recipient", id, SharingWire.ProfileKind, info2, false);
                var received =
                    client.Receive(id, info2, key, cache, default, downloadTicket, downloadProfile, null);
                Check(received.Vrm.SequenceEqual(vrm) && received.Settings == "ModelScale=1.5\nAllowShare=True",
                    "Downloaded avatar/settings differ");
                var cachePath = Path.Combine(cache, id.ToString(), info.Hash + ".vrm.bundle");
                var cacheProfile = Path.Combine(cache, id.ToString(), info.Hash + ".settings.bundle");
                var modified = File.GetLastWriteTimeUtc(cachePath);
                Check(client.Receive(id, info2, key, cache, default, "", "", null).Vrm.SequenceEqual(vrm),
                    "Valid cache required TCP");
                Check(File.GetLastWriteTimeUtc(cachePath) == modified, "Valid cache rewritten");
                Check(File.ReadAllBytes(cachePath).SequenceEqual(storedBytes), "Recipient cache is not ciphertext");
                Check(Directory.GetFiles(cache, "*", SearchOption.AllDirectories).All(p => p.EndsWith(".bundle")),
                    "Plaintext/key file in cache");
                Reject(() => client.Receive(id, info2, BundleCrypto.GenerateKey(), cache, default, "", "", null),
                    "Wrong key accepted from cache");
                File.WriteAllBytes(cachePath, new byte[] { 1, 2, 3 });
                downloadTicket = server.AuthorizeTransfer("recipient", id, SharingWire.AvatarKind, info2, false);
                Check(client.Receive(id, info2, key, cache, default, downloadTicket, "", null).Vrm.SequenceEqual(vrm),
                    "Corrupt cache was not replaced");
                // The cached settings blob carries the old version; its tag no longer matches and it is re-fetched.
                var info3 = info2.Clone();
                var profile3 = BundleCrypto.PackProfile("ModelScale=2\nAllowShare=True", "");
                var encryptedProfile3 = TestBundle.Encrypt(profile3, key, id);
                info3.ProfileVersion = BundleCrypto.Version(profile3, key);
                info3.ProfileHash = BundleCrypto.Hash(encryptedProfile3);
                client.UploadBlob(id,
                    server.AuthorizeTransfer("owner", id, SharingWire.ProfileKind, info3, true),
                    encryptedProfile3,
                    default);
                downloadProfile = server.AuthorizeTransfer("recipient", id, SharingWire.ProfileKind, info3, false);
                Check(client.Receive(id, info3, key, cache, default, "", downloadProfile, null).Settings ==
                    "ModelScale=2\nAllowShare=True",
                    "Stale cached settings were not replaced after a settings change");
                Check(Directory.GetFiles(Path.Combine(cache, id.ToString())).Length == 2,
                    "Settings change grew the cache");

                // A revoked game session cannot use a previously issued transfer ticket.
                var revoked = server.AuthorizeTransfer("leaving", id, SharingWire.AvatarKind, info3, true);
                server.RevokeSession("leaving");
                var denied = false;
                try
                {
                    client.UploadBlob(id, revoked, encrypted, default);
                }
                catch (IOException)
                {
                    denied = true;
                }

                Check(denied, "Disconnected peer retained upload authorization");
                var ownTicket = server.AuthorizeTransfer("recipient", id, SharingWire.AvatarKind, info3, false);
                server.RevokeCharacter(id);
                using (var socket = new TcpClient("127.0.0.1", server.Port))
                using (var writer = new BinaryWriter(socket.GetStream()))
                using (var reader = new BinaryReader(socket.GetStream()))
                {
                    writer.Write(SharingWire.Magic);
                    writer.Write(SharingWire.Download);
                    writer.Write(id);
                    SharingWire.WriteText(writer, ownTicket);
                    writer.Flush();
                    Check(!reader.ReadBoolean(), "Owner disconnect retained download authorization");
                }

                // Disconnect partway through a new upload; the previous durable version survives.
                var partialTicket = server.AuthorizeTransfer("owner2", id, SharingWire.AvatarKind, info3, true);
                using (var partial = new TcpClient("127.0.0.1", server.Port))
                using (var writer = new BinaryWriter(partial.GetStream()))
                using (var reader = new BinaryReader(partial.GetStream()))
                {
                    writer.Write(SharingWire.Magic);
                    writer.Write(SharingWire.Upload);
                    writer.Write(id);
                    SharingWire.WriteText(writer, partialTicket);
                    writer.Flush();
                    Check(reader.ReadBoolean(), "Authorized upload rejected");
                    writer.Write(10000);
                    writer.Write(new byte[10]);
                    writer.Flush();
                    server.RevokeSession("owner2");
                }

                Check(server.ReadCurrent(id).SameAs(info3), "Disconnect lost stored avatar");
                while (commits.TryDequeue(out committed)) Check(committed.HasAvatar, "Partial upload was published");
                stop.Cancel();
                try
                {
                    await serving;
                }
                catch (OperationCanceledException) { }

                using (var restarted = new AvatarTcpServer(Path.Combine(root, "server"),
                           IPAddress.Loopback,
                           0,
                           new SharingDownloadPolicy(25, 4)))
                    Check(restarted.ReadCurrent(id).SameAs(info3), "Server restart lost durable blob");
            }

            await RpcChecks.Run(root, Check);
            Console.WriteLine("PASS: " + _assertions +
                " sharing checks (RPC control, blob-only TCP, disconnect cleanup, encryption, cache, throttle).");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

internal static class TestBundle
{
    internal static byte[] Encrypt(byte[] packed, string key, long characterId)
    {
        return BundleCrypto.Encrypt(packed, key, BundleCrypto.Version(packed, key), characterId);
    }
}

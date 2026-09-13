using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM;
using EnhancedValheimVRM.Sharing;

internal static class ZipChecks
{
    private static void Reject(Action action, Action<bool, string> check, string name)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
        {
            rejected = true;
        }

        check(rejected, name);
    }

    private static byte[] Archive(params (string name, byte[] bytes)[] entries)
    {
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var entry in entries)
                {
                    using (var file = zip.CreateEntry(entry.name).Open())
                        file.Write(entry.bytes, 0, entry.bytes.Length);
                }
            }

            return output.ToArray();
        }
    }

    internal static async Task Run(string root, Action<bool, string> check)
    {
        const long id = 4567;
        var key = BundleCrypto.GenerateKey();
        var vrm = new byte[2 * 1024 * 1024];
        // Repeated model-like data makes compression measurable rather than timing-dependent.
        for (var i = 0; i < vrm.Length; i++) vrm[i] = (byte)(i % 127);
        const string settingsText = "AllowShare=True\r\n# café\n", outfitsText = "[Default]\nDefault=True\n";
        var packed = BundleCrypto.PackAvatar(vrm);
        var profile = BundleCrypto.PackProfile(settingsText, outfitsText);
        check(packed[0] == 'P' && packed[1] == 'K' && profile[0] == 'P', "Plaintext bundles are not ZIPs");
        check(packed.Length < vrm.Length / 10, "Bundle compression was not applied");
        using (var zip = new ZipArchive(new MemoryStream(packed), ZipArchiveMode.Read))
        {
            check(zip.Entries.Select(e => e.FullName).SequenceEqual(new[] { "avatar.vrm" }),
                "Model ZIP must contain exactly avatar.vrm");
            check(zip.Entries.All(e => e.LastWriteTime.Year == 2000), "ZIP timestamps depend on current time");
        }

        using (var zip = new ZipArchive(new MemoryStream(profile), ZipArchiveMode.Read))
        {
            check(zip.Entries.Select(e => e.FullName).SequenceEqual(new[] { "settings.txt", "outfits.txt" }),
                "Settings ZIP must contain exactly settings.txt and outfits.txt");
        }

        check(packed.SequenceEqual(BundleCrypto.PackAvatar(vrm)), "Identical files produced different ZIP versions");
        check(BundleCrypto.UnpackAvatar(packed).SequenceEqual(vrm), "Model ZIP roundtrip changed the file");
        BundleCrypto.UnpackProfile(profile, out var settings, out var outfits);
        check(settings == settingsText && outfits == outfitsText, "Settings ZIP roundtrip changed files");
        BundleCrypto.UnpackProfile(BundleCrypto.PackProfile(settingsText, ""), out _, out outfits);
        check(outfits == "", "Empty outfit file was lost");

        // The model version depends only on the model; settings and outfits have their own version.
        string modelVersion = BundleCrypto.Version(packed, key), profileVersion = BundleCrypto.Version(profile, key);
        check(BundleCrypto.Version(BundleCrypto.PackProfile(settingsText, "[Changed]\nDefault=True\n"), key) !=
            profileVersion,
            "Changed outfits reused old settings version");
        check(BundleCrypto.Version(BundleCrypto.PackProfile("ModelScale=2\n", outfitsText), key) != profileVersion,
            "Changed settings reused old settings version");
        check(BundleCrypto.Version(BundleCrypto.PackAvatar(vrm), key) == modelVersion,
            "Model version changed without the model changing");

        var avatarEntry = ("avatar.vrm", new byte[] { 1, 2, 3 });
        var settingsEntry = ("settings.txt", Encoding.UTF8.GetBytes(settingsText));
        Reject(() => BundleCrypto.UnpackAvatar(Archive(avatarEntry, settingsEntry)),
            check,
            "Extra file in model ZIP accepted");
        Reject(() => BundleCrypto.UnpackAvatar(Archive(("../avatar.vrm", new byte[] { 1 }))),
            check,
            "ZIP path entry accepted");
        Reject(() => BundleCrypto.UnpackProfile(Archive(settingsEntry), out _, out _),
            check,
            "Missing outfits file accepted");
        Reject(() => BundleCrypto.UnpackProfile(Archive(settingsEntry, settingsEntry), out _, out _),
            check,
            "Duplicate ZIP name accepted");
        Reject(() =>
                BundleCrypto.UnpackProfile(Archive(settingsEntry, ("fourth.txt", Array.Empty<byte>())), out _, out _),
            check,
            "Unexpected file in settings ZIP accepted");
        Reject(() =>
                BundleCrypto.UnpackProfile(
                    Archive(settingsEntry, ("outfits.txt", new byte[SharingWire.MaxSettingsBytes + 1])),
                    out _,
                    out _),
            check,
            "Oversized decompressed text accepted");
        Reject(() => BundleCrypto.UnpackAvatar(new byte[] { 1, 2, 3 }), check, "Malformed ZIP accepted");

        var encrypted = TestBundle.Encrypt(packed, key, id);
        var encryptedProfile = TestBundle.Encrypt(profile, key, id);
        var info = new BundleInfo
        {
            Version = modelVersion,
            Hash = BundleCrypto.Hash(encrypted),
            ProfileVersion = profileVersion,
            ProfileHash = BundleCrypto.Hash(encryptedProfile)
        };
        var versionRejected = false;
        try
        {
            BundleCrypto.Decrypt(encrypted, key, new string('b', 64), id);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            versionRejected = true;
        }

        check(versionRejected, "Authentication did not bind the announced version");
        var characterRejected = false;
        try
        {
            BundleCrypto.Decrypt(encrypted, key, modelVersion, id + 1);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            characterRejected = true;
        }

        check(characterRejected, "Authentication did not bind the character");

        string cache = Path.Combine(root, "zip-cache"), directory = Path.Combine(cache, id.ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, info.Hash + ".vrm.bundle");
        File.WriteAllBytes(path, encrypted);
        File.WriteAllBytes(Path.Combine(directory, info.Hash + ".settings.bundle"), encryptedProfile);
        var modified = File.GetLastWriteTimeUtc(path);
        var client = new AvatarTcpClient("unused.invalid", 1, 750000);
        var stages = new System.Collections.Generic.List<string>();
        var decoded = client.Receive(id, info, key, cache, default, "", "", stages.Add);
        check(stages.Any(stage => stage.StartsWith("model: loading from local cache")) &&
            stages.Any(stage => stage.StartsWith("settings: loading from local cache")) &&
            stages.Any(stage => stage.StartsWith("verified in")),
            "Load stages did not report both blobs");
        check(!stages.Any(stage => stage.Contains(key)), "Load stages leaked the sharing secret");
        foreach (var word in new[] { "key", "ticket", "AES", "HMAC", "cipher", "encrypt", "decrypt", "RPC", "TCP" })
        {
            check(!stages.Any(stage => stage.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0),
                "Load stages mention protocol detail: " + word);
        }

        var limited = new AvatarTcpClient("unused.invalid", 1, 750000) { BundleLimitBytes = 64 };
        Reject(() => limited.UploadBlob(id, "", encrypted, default),
            check,
            "Upload exceeded announced limit before opening TCP");
        check(decoded.Vrm.SequenceEqual(vrm) && decoded.Settings == settingsText && decoded.Outfits == outfitsText,
            "Cached encrypted ZIPs failed to load");
        check(decoded.VerifiedVersion == modelVersion && decoded.ProfileVersion == profileVersion,
            "Receive did not record both versions");
        // A viewer that already imported this model version fetches only the settings blob.
        stages.Clear();
        string seenSettings = null;
        var skipped = client.Receive(id,
            info,
            key,
            cache,
            default,
            "",
            "",
            stages.Add,
            text =>
            {
                seenSettings = text;
                return true;
            });
        check(skipped.Vrm == null && skipped.Settings == settingsText && seenSettings == settingsText &&
            stages.Any(stage => stage.StartsWith("model: already loaded")) &&
            !stages.Any(stage => stage.StartsWith("model: loading")),
            "Already-imported model was fetched again");
        check(client.Receive(id, info, key, cache, default, "", "", null, text => false).Vrm.SequenceEqual(vrm),
            "Model skipped although the viewer has not imported it");
        check(File.GetLastWriteTimeUtc(path) == modified, "Cached ZIP was rewritten");
        check(Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length == 2 &&
            Directory.GetFiles(cache, "*", SearchOption.AllDirectories).All(f => f.EndsWith(".bundle")),
            "Plaintext ZIP/files written to cache");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            var cancelled = false;
            try
            {
                client.Receive(id, info, key, cache, cancel.Token, "", "", null);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            check(cancelled, "Cancelled ZIP load proceeded");
        }

        await CheckUploadLifetime(id, vrm, settingsText, key, check);
        Console.WriteLine("ZIP sample: " + vrm.Length + " bytes -> " + packed.Length + " bytes before encryption.");
    }

    private static ZPackage Reply(long request, BundleInfo info = null, string value = "")
    {
        info = info ?? new BundleInfo();
        return RpcChecks.Packet(7,
            request,
            4567L,
            info.Version,
            info.Hash,
            value,
            "",
            info.ProfileVersion,
            info.ProfileHash,
            "");
    }

    private static int Op(ZPackage packet)
    {
        packet.Rewind();
        return packet.ReadInt();
    }

    private static long Request(ZPackage packet)
    {
        packet.Rewind();
        packet.ReadInt();
        return packet.ReadLong();
    }

    private static async Task<ZPackage> WaitFor(ZNetPeer server, int op, int minimum = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        do
        {
            SharingRpc.Tick(null);
            var packets = server.m_rpc.Sent.Where(p => Op(p) == op).ToList();
            if (packets.Count >= minimum) return packets.Last();
            await Task.Delay(5);
        } while (DateTime.UtcNow < deadline);

        throw new Exception("Missing client RPC " + op);
    }

    private static async Task CheckUploadLifetime(long id,
        byte[] vrm,
        string settingsText,
        string key,
        Action<bool, string> check)
    {
        var net = new ZNet { Server = false };
        ZNet.instance = net;
        var server = new ZNetPeer { m_uid = 1 };
        net.Peers.Add(server);
        SharingRpc.Reset(net);
        SharingRpc.RegisterPeer(net, server);
        server.m_rpc.Deliver(RpcChecks.Packet(13, SharingWire.DefaultBundleLimitBytes, version: "1", value: "6067"));
        SharingRpc.ClientReady();
        var client = new AvatarTcpClient("unused.invalid", 1, 750000);
        var zip = BundleCrypto.PackAvatar(vrm);
        string version = BundleCrypto.Version(zip, key), hash = new('a', 64);
        var profile = BundleCrypto.PackProfile(settingsText, "");
        var stored = new BundleInfo
        {
            Version = version,
            Hash = hash,
            ProfileVersion = BundleCrypto.Version(profile, key),
            ProfileHash = new string('c', 64)
        };
        var packs = 0;
        var packAvatar = () =>
        {
            packs++;
            return zip;
        };

        // Both blobs stored: nothing is packed, the settings ZIP is released.
        var publication =
            SharingRpc.PublishAsync(client, id, version, packAvatar, profile, key, default, SharingRpc.Epoch);
        var request = await WaitFor(server, 1);
        server.m_rpc.Deliver(Reply(Request(request), stored));
        check((await publication).SameAs(stored) && packs == 0 && profile.All(b => b == 0),
            "Stored-version hit packed the model or retained the settings ZIP");
        server.m_rpc.Sent.Clear();

        // Only the settings changed: the model is not packed; the settings blob is offered by name of the stored model.
        profile = BundleCrypto.PackProfile("ModelScale=2\n", "");
        publication = SharingRpc.PublishAsync(client, id, version, packAvatar, profile, key, default, SharingRpc.Epoch);
        request = await WaitFor(server, 1);
        server.m_rpc.Deliver(Reply(Request(request), stored));
        var offer = await WaitFor(server, 2);
        offer.Rewind();
        var offered = RpcChecks.Read(offer);
        check(offered.Value == SharingWire.ProfileKind && offered.Hash == hash && offered.ProfileVersion ==
            BundleCrypto.Version(BundleCrypto.PackProfile("ModelScale=2\n", ""), key) && packs == 0,
            "Settings-only change repacked the model or offered the wrong blob");
        check(profile.All(b => b == 0),
            "Plaintext settings ZIP retained while awaiting permission to transfer ciphertext");
        server.m_rpc.Deliver(Reply(Request(offer), value: "error"));
        var failed = false;
        try
        {
            await publication;
        }
        catch (SharingRpc.PublishFailure)
        {
            failed = true;
        }

        check(failed, "Rejected settings offer did not fail the publication");
        server.m_rpc.Sent.Clear();

        // Nothing stored: the model is packed once and offered first.
        profile = BundleCrypto.PackProfile(settingsText, "");
        publication = SharingRpc.PublishAsync(client, id, version, packAvatar, profile, key, default, SharingRpc.Epoch);
        request = await WaitFor(server, 1);
        server.m_rpc.Deliver(Reply(Request(request), value: "storage"));
        failed = false;
        try
        {
            await publication;
        }
        catch (SharingRpc.PublishFailure)
        {
            failed = true;
        }

        check(failed && profile.All(b => b == 0) && packs == 0,
            "Version-check failure packed the model or retained plaintext");
        server.m_rpc.Sent.Clear();

        using (var cancel = new CancellationTokenSource())
        {
            zip = BundleCrypto.PackAvatar(vrm);
            profile = BundleCrypto.PackProfile(settingsText, "");
            publication = SharingRpc.PublishAsync(client,
                id,
                version,
                packAvatar,
                profile,
                key,
                cancel.Token,
                SharingRpc.Epoch);
            request = await WaitFor(server, 1);
            server.m_rpc.Deliver(Reply(Request(request)));
            offer = await WaitFor(server, 2);
            offered = RpcChecks.Read(offer);
            check(offered.Value == SharingWire.AvatarKind && packs == 1, "Empty server did not offer the model first");
            check(zip.All(b => b == 0),
                "Plaintext model ZIP retained while awaiting permission to transfer ciphertext");
            cancel.Cancel();
            SharingRpc.Tick(null);
            var cancelled = false;
            try
            {
                await publication;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            check(cancelled && profile.All(b => b == 0), "Pending upload did not cancel or retained the settings ZIP");
        }

        SharingRpc.Reset(null);
        ZNet.instance = null;
    }
}

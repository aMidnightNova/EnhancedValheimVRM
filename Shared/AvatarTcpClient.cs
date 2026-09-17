using System;
using System.Threading.Tasks;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace EnhancedValheimVRM.Sharing
{
    public sealed class AvatarTcpClient
    {
        public int BundleLimitBytes { get; set; } = SharingWire.DefaultBundleLimitBytes;

        private readonly string _host;
        private readonly int _port;
        private readonly int _uploadBytesPerSecond;

        public AvatarTcpClient(string host, int port, int uploadBytesPerSecond)
        {
            if (uploadBytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(uploadBytesPerSecond));
            _host = host;
            _port = port;
            _uploadBytesPerSecond = uploadBytesPerSecond;
        }

        private TcpClient Connect(CancellationToken cancellation)
        {
            return Connect(_host, _port, 10000, cancellation);
        }

        private static TcpClient Connect(string host, int port, int timeoutMs, CancellationToken cancellation)
        {
            var client = new TcpClient { NoDelay = true, ReceiveTimeout = 30000, SendTimeout = 30000 };
            try
            {
                var connecting = client.ConnectAsync(host, port);
                if (!connecting.Wait(timeoutMs, cancellation))
                    throw new TimeoutException("TCP server connection timed out.");
                connecting.GetAwaiter().GetResult();
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        // plain tcp, sends the wire magic, ping and a random number. only the sharing server answers with the
        // magic and that same number, anything else on the port counts as no answer
        public static bool Ping(string host, int port, int timeoutMs)
        {
            var random = new byte[8];
            using (var generator = System.Security.Cryptography.RandomNumberGenerator.Create())
                generator.GetBytes(random);
            var nonce = BitConverter.ToInt64(random, 0);
            try
            {
                using (var client = Connect(host, port, timeoutMs, CancellationToken.None))
                using (var stream = client.GetStream())
                using (var reader = new BinaryReader(stream))
                using (var writer = new BinaryWriter(stream))
                {
                    client.ReceiveTimeout = client.SendTimeout = timeoutMs;
                    writer.Write(SharingWire.Magic);
                    writer.Write(SharingWire.Ping);
                    writer.Write(nonce);
                    writer.Flush();
                    return reader.ReadInt32() == SharingWire.Magic && reader.ReadInt64() == nonce;
                }
            }
            catch (Exception error) when (error is SocketException || error is IOException ||
                                          error is TimeoutException || error is AggregateException ||
                                          error is ObjectDisposedException)
            {
                return false;
            }
        }

        private static void Header(BinaryWriter writer, byte command, long characterId)
        {
            SharingWire.ValidateId(characterId);
            writer.Write(SharingWire.Magic);
            writer.Write(command);
            writer.Write(characterId);
            writer.Flush();
        }

        // The ticket is issued over the authenticated game RPC connection.
        // TCP contains only transfer framing and encrypted blob bytes.
        public void UploadBlob(long id, string ticket, byte[] encrypted, CancellationToken cancellation)
        {
            if (encrypted.Length > BundleLimitBytes)
            {
                throw new InvalidDataException("Avatar bundle exceeds the server bundle limit (" +
                    BundleLimitBytes / 1048576 + " MiB).");
            }

            using (var client = Connect(cancellation))
            using (cancellation.Register(() => Task.Run(() => client.Close())))
            using (var stream = client.GetStream())
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                client.ReceiveTimeout = 0; // A transfer may queue behind occupied slots; disconnect cancels it.
                Header(writer, SharingWire.Upload, id);
                SharingWire.WriteText(writer, ticket);
                writer.Flush();
                if (!reader.ReadBoolean()) throw new IOException("The server rejected the upload.");
                SharingWire.WriteThrottled(writer, encrypted, cancellation, _uploadBytesPerSecond);
                if (!reader.ReadBoolean()) throw new IOException("The upload did not complete.");
            }
        }

        private byte[] Download(long id, string expectedHash, string ticket, CancellationToken cancellation)
        {
            using (var client = Connect(cancellation))
            using (cancellation.Register(() => Task.Run(() => client.Close())))
            using (var stream = client.GetStream())
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                client.ReceiveTimeout = 0;
                Header(writer, SharingWire.Download, id);
                SharingWire.WriteText(writer, ticket);
                writer.Flush();
                if (!reader.ReadBoolean()) throw new IOException("The avatar is no longer available on the server.");
                var encrypted = SharingWire.ReadBytes(reader, BundleLimitBytes);
                if (BundleCrypto.Hash(encrypted) != expectedHash)
                    throw new InvalidDataException("Downloaded bundle hash mismatch.");
                return encrypted;
            }
        }

        // Loads one encrypted blob from the local cache or the server and returns its plaintext ZIP.
        private byte[] LoadBlob(long id,
            string kind,
            BundleInfo info,
            string key,
            string directory,
            string ticket,
            CancellationToken cancellation,
            Action<string> timing)
        {
            string hash = info.HashOf(kind), version = info.VersionOf(kind);
            var path = Path.Combine(directory, SharingWire.BlobFileName(kind, info.Hash));
            var label = kind == SharingWire.AvatarKind ? "model" : "settings";
            var clock = timing == null ? null : System.Diagnostics.Stopwatch.StartNew();
            var fromCache = File.Exists(path) && new FileInfo(path).Length <= BundleLimitBytes;
            timing?.Invoke(label + ": " + (fromCache ? "loading from local cache" : "downloading from server"));
            var encrypted = fromCache ? File.ReadAllBytes(path) : Download(id, hash, ticket, cancellation);
            var readMs = clock?.Elapsed.TotalMilliseconds ?? 0;
            timing?.Invoke(label + ": received " + (encrypted.Length / 1048576.0).ToString("F1") + " MB in " +
                readMs.ToString("F0") + "ms");
            cancellation.ThrowIfCancellationRequested();
            byte[] plaintext;
            try
            {
                plaintext = BundleCrypto.DecryptTimed(encrypted, key, version, id, timing);
            }
            catch (Exception error) when (fromCache &&
                                          (error is System.Security.Cryptography.CryptographicException ||
                                              error is InvalidDataException))
            {
                // Authentication checks cache integrity, its announced version and its character.
                timing?.Invoke(label + ": local copy was invalid; downloading again");
                encrypted = Download(id, hash, ticket, cancellation);
                fromCache = false;
                plaintext = BundleCrypto.DecryptTimed(encrypted, key, version, id, timing);
            }

            if (!fromCache)
            {
                var cacheStart = clock?.Elapsed.TotalMilliseconds ?? 0;
                Directory.CreateDirectory(directory);
                AtomicWrite(path, encrypted);
                SharingWire.PruneBlobs(directory, info.Hash);
                timing?.Invoke(label + ": saved to local cache in " +
                    (clock.Elapsed.TotalMilliseconds - cacheStart).ToString("F0") + "ms");
            }

            return plaintext;
        }

        // Receives the settings blob and the model blob; the model is skipped by the caller's
        // import cache when its version is unchanged, so a settings edit costs only the small blob.
        // modelAlreadyImported receives the settings text and may answer true when the viewer already
        // has this model version imported; the model blob is then skipped and Vrm stays null.
        public AvatarBundle Receive(long id,
            BundleInfo info,
            string key,
            string cacheDirectory,
            CancellationToken cancellation,
            string avatarTicket,
            string profileTicket,
            Action<string> timing,
            Func<string, bool> modelAlreadyImported = null)
        {
            info.Validate();
            var directory = Path.Combine(cacheDirectory,
                id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var clock = timing == null ? null : System.Diagnostics.Stopwatch.StartNew();
            var bundle = new AvatarBundle
            {
                CharacterId = id, VerifiedVersion = info.Version, ProfileVersion = info.ProfileVersion
            };
            byte[] plaintext = null;
            try
            {
                plaintext = LoadBlob(id,
                    SharingWire.ProfileKind,
                    info,
                    key,
                    directory,
                    profileTicket,
                    cancellation,
                    timing);
                BundleCrypto.UnpackProfile(plaintext, out bundle.Settings, out bundle.Outfits);
                BundleCrypto.ClearBytes(ref plaintext);
                var profileMs = clock?.Elapsed.TotalMilliseconds ?? 0;
                cancellation.ThrowIfCancellationRequested();
                if (modelAlreadyImported != null && modelAlreadyImported(bundle.Settings))
                {
                    timing?.Invoke("model: already loaded, skipped");
                    return bundle;
                }

                plaintext = LoadBlob(id,
                    SharingWire.AvatarKind,
                    info,
                    key,
                    directory,
                    avatarTicket,
                    cancellation,
                    timing);
                bundle.Vrm = BundleCrypto.UnpackAvatar(plaintext);
                BundleCrypto.ClearBytes(ref plaintext);
                timing?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "load: settings={0:F0}ms, model={1:F0}ms",
                    profileMs,
                    (clock?.Elapsed.TotalMilliseconds ?? 0) - profileMs));
                return bundle;
            }
            catch
            {
                BundleCrypto.ClearBytes(ref bundle.Vrm);
                throw;
            }
            finally
            {
                BundleCrypto.ClearBytes(ref plaintext);
            }
        }

        public static void AtomicWrite(string path, byte[] bytes)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EnhancedValheimVRM.Sharing
{
    // Blob storage/transport only. All authorization and publication use game RPC.
    public sealed class AvatarTcpServer : IDisposable
    {
        private sealed class Transfer
        {
            internal string Session, Kind, Hash, Version, FileHash;
            internal long Id;
            internal bool Upload;
            internal DateTime Expires;
            internal volatile bool Revoked;
            internal TcpClient Client;
            internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
        }

        private readonly string _directory;
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, Transfer> _tickets = new ConcurrentDictionary<string, Transfer>();
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new ConcurrentDictionary<TcpClient, byte>();
        private readonly ConcurrentDictionary<long, byte> _uploads = new ConcurrentDictionary<long, byte>();
        private readonly ConcurrentDictionary<string, Transfer> _active = new ConcurrentDictionary<string, Transfer>();
        public event Action<string, long, BundleInfo> Uploaded;
        private readonly object _commitLock = new object();
        private readonly SemaphoreSlim _connections = new SemaphoreSlim(128);
        private readonly SemaphoreSlim _uploadSlots = new SemaphoreSlim(4);
        private readonly SemaphoreSlim _downloadSlots;

        public SharingDownloadPolicy DownloadPolicy { get; }

        public int BundleLimitBytes { get; set; } = SharingWire.DefaultBundleLimitBytes;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public AvatarTcpServer(string directory, IPAddress address, int port, SharingDownloadPolicy downloadPolicy)
        {
            DownloadPolicy = downloadPolicy ?? throw new ArgumentNullException(nameof(downloadPolicy));
            _downloadSlots = new SemaphoreSlim(DownloadPolicy.Slots);
            _directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_directory);
            _listener = new TcpListener(address, port);
            _listener.Start();
        }

        // Called by RPC handlers: memory only, never filesystem/socket waits on Unity's thread.
        public string AuthorizeTransfer(string session, long id, string kind, BundleInfo info, bool upload)
        {
            SharingWire.ValidateId(id);
            SharingWire.ValidateKind(kind);
            string hash = info.HashOf(kind), version = info.VersionOf(kind);
            SharingWire.ValidateHash(hash);
            SharingWire.ValidateHash(version);
            SharingWire.ValidateHash(info.Hash); // Both files are named by the model hash.
            foreach (var pair in _tickets)
                if (pair.Value.Expires < DateTime.UtcNow && _tickets.TryRemove(pair.Key, out var expired) &&
                    !_active.ContainsKey(pair.Key))
                    expired.Stop.Dispose();
            if (_tickets.Count >= 256) throw new IOException("Too many pending transfers.");
            string ticket = Guid.NewGuid().ToString("N");
            _tickets[ticket] = new Transfer
            {
                Session = session,
                Id = id,
                Kind = kind,
                Hash = hash,
                Version = version,
                FileHash = info.Hash,
                Upload = upload,
                Expires = DateTime.UtcNow.AddMinutes(2)
            };
            return ticket;
        }

        public void RevokeSession(string session) => Revoke(transfer => transfer.Session == session);

        public void RevokeCharacter(long id)
        {
            if (id != 0) Revoke(transfer => transfer.Id == id);
        }

        private void Revoke(Func<Transfer, bool> matches)
        {
            foreach (var pair in _tickets)
                if (matches(pair.Value) && _tickets.TryRemove(pair.Key, out var pending))
                {
                    pending.Revoked = true;
                    if (!_active.ContainsKey(pair.Key)) pending.Stop.Dispose();
                }

            foreach (var transfer in _active.Values)
                if (matches(transfer))
                {
                    transfer.Revoked = true;
                    try
                    {
                        transfer.Stop.Cancel();
                    }
                    catch (ObjectDisposedException) { }

                    var client = transfer.Client;
                    if (client != null) _ = Task.Run(() => client.Close());
                }
        }

        // Worker-only disk access. Encrypted files/manifests intentionally survive disconnects.
        // A manifest can describe one blob while the other is still missing; each pair is
        // reported only while its file is present and within the current limit.
        public BundleInfo ReadCurrent(long id)
        {
            lock (_commitLock)
            {
                var info = ReadManifest(id);
                if (info == null) return null;
                if (info.HasAvatar && !BlobUsable(id, SharingWire.AvatarKind, info.Hash)) info.Version = info.Hash = "";
                if (info.HasProfile && (!info.HasAvatar || !BlobUsable(id, SharingWire.ProfileKind, info.Hash)))
                    info.ProfileVersion = info.ProfileHash = "";
                return info.HasAvatar || info.HasProfile ? info : null;
            }
        }

        private BundleInfo ReadManifest(long id)
        {
            string path = Path.Combine(CharacterDirectory(id), "current");
            if (!File.Exists(path)) return null;
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                    return BundleInfo.Read(reader);
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException)
            {
                return null; // An unreadable or older manifest is treated as absent; the owner re-uploads.
            }
        }

        private bool BlobUsable(long id, string kind, string hash)
        {
            var blob = new FileInfo(Path.Combine(CharacterDirectory(id), SharingWire.BlobFileName(kind, hash)));
            return blob.Exists && blob.Length >= 64 && blob.Length <= BundleLimitBytes;
        }

        public async Task RunAsync(CancellationToken cancellation)
        {
            using (cancellation.Register(Dispose))
            {
                while (!cancellation.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SocketException) when (cancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    if (cancellation.IsCancellationRequested || !_connections.Wait(0))
                    {
                        client.Dispose();
                        continue;
                    }

                    _clients.TryAdd(client, 0);
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Handle(client, cancellation);
                        }
                        catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                                                   ex is SocketException || ex is ArgumentException ||
                                                   ex is ObjectDisposedException || ex is OperationCanceledException)
                        {
                            /* Invalid/incomplete transfers are not published; no payload logging. */
                        }
                        finally
                        {
                            client.Dispose();
                            _clients.TryRemove(client, out _);
                            _connections.Release();
                        }
                    });
                }
            }
        }

        private string CharacterDirectory(long id)
        {
            SharingWire.ValidateId(id);
            return Path.Combine(_directory, id.ToString(CultureInfo.InvariantCulture));
        }

        private void Handle(TcpClient client, CancellationToken cancellation)
        {
            client.NoDelay = true;
            client.ReceiveTimeout = client.SendTimeout = 30000;
            using (var stream = client.GetStream())
            using (var reader = new BinaryReader(stream))
            using (var writer = new BinaryWriter(stream))
            {
                if (reader.ReadInt32() != SharingWire.Magic)
                    throw new InvalidDataException(
                        "EnhancedValheimVRM: unsupported TCP transfer header. Use matching mod builds on the server and clients, then restart them.");
                byte direction = reader.ReadByte();
                long id = reader.ReadInt64();
                string ticket = SharingWire.ReadText(reader, 64);
                if (!_tickets.TryGetValue(ticket, out var transfer))
                {
                    writer.Write(false);
                    return;
                }

                // Register before removing the ticket so disconnect cannot miss a transfer in flight.
                if (!_active.TryAdd(ticket, transfer))
                {
                    writer.Write(false);
                    return;
                }

                transfer.Client = client;
                try
                {
                    if (!_tickets.TryRemove(ticket, out _) || transfer.Revoked || transfer.Expires < DateTime.UtcNow ||
                        id != transfer.Id || direction != (transfer.Upload ? SharingWire.Upload : SharingWire.Download))
                    {
                        writer.Write(false);
                        return;
                    }

                    using (var transferStop =
                           CancellationTokenSource.CreateLinkedTokenSource(cancellation, transfer.Stop.Token))
                    {
                        cancellation = transferStop.Token;
                        if (transfer.Upload)
                        {
                            if (!_uploads.TryAdd(id, 0))
                            {
                                writer.Write(false);
                                return;
                            }

                            try
                            {
                                _uploadSlots.Wait(cancellation);
                                try
                                {
                                    writer.Write(true);
                                    writer.Flush();
                                    var stored = Upload(transfer, reader, cancellation);
                                    if (transfer.Revoked) throw new OperationCanceledException();
                                    Uploaded?.Invoke(transfer.Session, transfer.Id, stored);
                                    writer.Write(true);
                                }
                                finally
                                {
                                    _uploadSlots.Release();
                                }
                            }
                            finally
                            {
                                _uploads.TryRemove(id, out _);
                            }
                        }
                        else
                        {
                            _downloadSlots.Wait(cancellation);
                            try
                            {
                                if (transfer.Revoked) throw new OperationCanceledException();
                                using (var file = File.OpenRead(Path.Combine(CharacterDirectory(id),
                                           SharingWire.BlobFileName(transfer.Kind, transfer.FileHash))))
                                {
                                    if (file.Length > BundleLimitBytes) throw new InvalidDataException();
                                    writer.Write(true);
                                    writer.Write((int)file.Length);
                                    writer.Flush();
                                    SharingWire.CopyThrottled(file,
                                        stream,
                                        new BandwidthLimiter(DownloadPolicy.BytesPerSecond),
                                        cancellation);
                                }
                            }
                            finally
                            {
                                _downloadSlots.Release();
                            }
                        }

                        writer.Flush();
                    }
                }
                finally
                {
                    _active.TryRemove(ticket, out _);
                    transfer.Stop.Dispose();
                }
            }
        }

        private BundleInfo Upload(Transfer transfer, BinaryReader reader, CancellationToken cancellation)
        {
            int length = reader.ReadInt32();
            if (length < 64 || length > BundleLimitBytes) throw new InvalidDataException("Invalid upload size.");
            string directory = CharacterDirectory(transfer.Id);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var hash = System.Security.Cryptography.SHA256.Create())
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536];
                        int remaining = length;
                        while (remaining > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (transfer.Revoked) throw new OperationCanceledException();
                            int count = reader.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                            if (count == 0) throw new EndOfStreamException();
                            hash.TransformBlock(buffer, 0, count, null, 0);
                            output.Write(buffer, 0, count);
                            remaining -= count;
                        }

                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    }

                    if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != transfer.Hash)
                        throw new InvalidDataException("Corrupt upload.");
                }

                // Only the small commit/manifest update is serialized. Large encrypted
                // uploads stream to temporary files with fixed memory per connection.
                lock (_commitLock)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (transfer.Revoked) throw new OperationCanceledException();
                    string destination = Path.Combine(directory,
                        SharingWire.BlobFileName(transfer.Kind, transfer.FileHash));
                    // The model file is immutable per hash; the settings file is replaced in place.
                    if (transfer.Kind == SharingWire.ProfileKind && File.Exists(destination)) File.Delete(destination);
                    if (!File.Exists(destination)) File.Move(temporary, destination);
                    // Merge this blob into the character's manifest; the other blob keeps its entry.
                    var current = ReadManifest(transfer.Id) ?? new BundleInfo();
                    if (transfer.Kind == SharingWire.AvatarKind)
                    {
                        // A new model invalidates the settings file named after the previous model.
                        if (current.Hash != transfer.Hash)
                        {
                            current.ProfileVersion = "";
                            current.ProfileHash = "";
                        }

                        current.Version = transfer.Version;
                        current.Hash = transfer.Hash;
                    }
                    else
                    {
                        if (current.Hash != transfer.FileHash)
                            throw new InvalidDataException("Settings do not belong to the stored model.");
                        current.ProfileVersion = transfer.Version;
                        current.ProfileHash = transfer.Hash;
                    }

                    using (var metadata = new MemoryStream())
                    using (var manifest = new BinaryWriter(metadata))
                    {
                        current.Write(manifest);
                        AvatarTcpClient.AtomicWrite(Path.Combine(directory, "current"), metadata.ToArray());
                    }

                    return current;
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            foreach (var transfer in _tickets.Values) transfer.Revoked = true;
            foreach (var transfer in _active.Values) transfer.Revoked = true;
            Revoke(transfer => true);
            _tickets.Clear();
            foreach (var client in _clients.Keys) client.Dispose();
        }
    }
}

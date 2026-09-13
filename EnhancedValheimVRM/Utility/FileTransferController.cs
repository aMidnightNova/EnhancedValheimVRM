using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // Unity work stays on Update/coroutines; TCP, encryption and disk I/O run off-thread.
    public sealed class FileTransferController : MonoBehaviour
    {
        private sealed class Download
        {
            public Player Player;
            public long Id;
            public Task<Received> Task;
            public string LastStage = "fetching from server";
            public string LoadedHash, RequestedHash, BlockedHash;
            public float NextCheck;
            public bool Installing;
            public CancellationTokenSource Stop;
        }

        private sealed class Received
        {
            public BundleInfo Info;
            public AvatarBundle Bundle;
            public string Timing;
        }

        private readonly Dictionary<Player, Download> _downloads = new Dictionary<Player, Download>();
        private CancellationTokenSource _session, _publish;
        private System.Diagnostics.Stopwatch _localSpawn;
        private AvatarTcpClient _client;
        private long _localId;
        private string _key, _host;
        private int _port, _uploadBytesPerSecond;
        private Task<BundleInfo> _upload;
        private volatile BundleInfo _published;
        private float _nextTick, _nextPublish, _nextAddressWarning;
        private Player _localPlayer;
        private bool _wasSharing;
        private static FileTransferController _instance;

        private static readonly
            System.Runtime.CompilerServices.ConditionalWeakTable<Player, System.Diagnostics.Stopwatch> FirstSeen =
                new System.Runtime.CompilerServices.ConditionalWeakTable<Player, System.Diagnostics.Stopwatch>();

        // Milliseconds since the local player spawned into the world, or -1 before that.
        internal static double MillisecondsSinceLocalSpawn => _instance?._localSpawn?.Elapsed.TotalMilliseconds ?? -1;

        internal static string PlayerLabel(Player player)
        {
            return player != null && !string.IsNullOrEmpty(player.GetPlayerName()) ? player.GetPlayerName() : "player";
        }

        // Main thread only: resolves a character id to the display name of a loaded player.
        internal static string PlayerLabel(long id)
        {
            foreach (var player in Player.GetAllPlayers())
            {
                if (player != null && player.GetPlayerID() == id) return PlayerLabel(player);
            }

            return "player " + id;
        }

        internal static void RecordPlayerSeen(Player player)
        {
            if (player == null || player.IsInStartMenu()) return;
            if (VrmController.HasKnownAvatar(player.GetPlayerID())) VrmController.ExpectSharedAvatar(player);
            if (Settings.LogLoadTiming)
            {
                FirstSeen.GetValue(player,
                    ignored =>
                    {
                        Logger.Log("Avatar load: player object entered world; instance=" + player.GetInstanceID() +
                            "; character=" + player.GetPlayerID());
                        return System.Diagnostics.Stopwatch.StartNew();
                    });
            }
        }

        private Task<HashSet<string>> _inventoryRead;
        private HashSet<string> _installedAvatars;
        private float _nextInventory;

        private void RefreshInventory()
        {
            if (_inventoryRead != null && _inventoryRead.IsCompleted)
            {
                if (_inventoryRead.Status == TaskStatus.RanToCompletion)
                    _installedAvatars = _inventoryRead.Result;
                else
                {
                    var observed = _inventoryRead.Exception;
                }

                _inventoryRead = null;
            }

            if (_inventoryRead != null || Time.realtimeSinceStartup < _nextInventory) return;
            _nextInventory = Time.realtimeSinceStartup + 5;
            var directory = Constants.Vrm.Dir;
            _inventoryRead = Task.Run(() => new HashSet<string>(Directory.Exists(directory)
                    ? Directory.EnumerateFiles(directory, "*.vrm").Select(Path.GetFileNameWithoutExtension)
                    : Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase));
        }

        private void Awake()
        {
            _instance = this;
        }

        public static void RefreshUpload()
        {
            if (_instance != null) _instance._nextPublish = 0;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextTick) return;
            _nextTick = Time.realtimeSinceStartup + 0.1f;
            var network = ZNet.instance;
            if (network == null || network.IsDedicated())
            {
                StopSession();
                return;
            }

            if (network.IsServer())
            {
                StopSession();
                return;
            }

            if (!Settings.EnableVrmSharing || Game.instance == null)
            {
                StopSession();
                return;
            }

            // A client never uses its local ServerPort value or guesses a default.
            if (!SharingRpc.TryGetServerPort(network, out var serverPort))
            {
                StopSession();
                return;
            }

            var serverHost = SharingEndpoint.ResolveClientHost(Settings.SharingHost, ZNet.GetServerString());
            if (string.IsNullOrEmpty(serverHost))
            {
                StopSession();
                if (Time.realtimeSinceStartup >= _nextAddressWarning)
                {
                    _nextAddressWarning = Time.realtimeSinceStartup + 60;
                    Logger.LogOnce("sharing-address",
                        "The game connection does not expose a TCP server address. Set Sharing.ServerHost to the game server's reachable hostname.");
                }

                return;
            }

            if (_session == null || _key != Settings.VrmKey ||
                _host != serverHost || _port != serverPort || _uploadBytesPerSecond != Settings.UploadBytesPerSecond)
            {
                StopSession();
                _key = Settings.VrmKey;
                _host = serverHost;
                _port = serverPort;
                _session = new CancellationTokenSource();
                _uploadBytesPerSecond = Settings.UploadBytesPerSecond;
                _client = new AvatarTcpClient(_host, _port, _uploadBytesPerSecond)
                {
                    BundleLimitBytes = SharingRpc.BundleLimitBytes
                };
                _nextPublish = 0;
                SharingRpc.ClientReady();
                OutfitRpc.ClientReady();
            }

            // Other players' avatars are fetched as soon as the server connection exists, so a
            // joining player's loading screen already covers the receive and import. Publishing
            // the local avatar waits for the local character to exist.
            var local = Player.m_localPlayer;
            if (local == null)
                _localSpawn = null;
            else if (_localSpawn == null) _localSpawn = System.Diagnostics.Stopwatch.StartNew();
            PumpPublish(local);
            if (_session == null) return;
            PumpDownloads(local);
        }

        private void ResetPublish()
        {
            _published = null;
            _wasSharing = false;
            _publish?.Cancel();
            _publish = null;
            _upload = null;
            _localPlayer = null;
            _localId = 0;
        }

        private void PumpPublish(Player local)
        {
            var localId = local != null ? local.GetPlayerID() : 0;
            if (localId == 0)
            {
                if (_localPlayer != null) ResetPublish();
                return;
            }

            if (local != _localPlayer || _localId != localId)
            {
                ResetPublish();
                _localPlayer = local;
                _localId = localId;
                _publish = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
                _nextPublish = 0;
            }

            var localVrm = VrmController.FindSharingInstance(local);
            var allowShare = localVrm != null && localVrm.GetSettings().AllowShare;
            var localModel = localVrm?.GetGameObject();
            var localOutfits = localModel != null ? localModel.GetComponent<OutfitController>() : null;
            if (_wasSharing && !allowShare)
            {
                StopSession();
                return;
            }

            _wasSharing = allowShare;
            if (_upload != null && _upload.IsCompleted)
            {
                if (_upload.Status == TaskStatus.RanToCompletion && allowShare)
                {
                    var changed = _published == null || !_published.SameAs(_upload.Result);
                    _published = _upload.Result;
                    if (changed) Logger.Log("Your avatar is shared with the server.");
                }
                else if (_upload.IsFaulted)
                {
                    var failure = _upload.Exception.Flatten().InnerExceptions.First();
                    var stage = (failure as SharingRpc.PublishFailure)?.Stage ?? "preparing the local bundle";
                    var reason = failure.GetBaseException().GetType().Name;
                    _nextPublish = Time.realtimeSinceStartup + 15;
                    // Stable, deduplicated warning: no keys, payloads, or raw exception text.
                    Logger.LogOnce("publish-failed:" + _localId + ":" + stage,
                        "VRM sharing failed while " + stage + " (" + reason + "); retrying.");
                }

                _upload = null;
            }

            if (allowShare && _upload == null && Time.realtimeSinceStartup >= _nextPublish)
            {
                _nextPublish = float.PositiveInfinity;
                var path = localVrm.GetVrmFilePath();
                var settings = localVrm.GetSettings().Serialize();
                var outfits = localOutfits?.SourceText ?? "";
                var id = _localId;
                var key = _key;
                var client = _client;
                var cancellation = _publish.Token;
                var epoch = SharingRpc.Epoch;
                _upload = Task.Run(async () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var file = new FileInfo(path);
                    if (file.Length > SharingWire.MaxExpandedVrmBytes)
                        throw new InvalidDataException("VRM exceeds the sharing limit.");
                    long length = file.Length, modified = file.LastWriteTimeUtc.Ticks;
                    // The model's version is remembered per file, so an unchanged model is never
                    // packed again. Settings and outfits travel in their own small blob.
                    var signature = BundleCrypto.Version(
                        System.Text.Encoding.UTF8.GetBytes(path + "\0" + length + "\0" + modified),
                        key);
                    var manifest = Path.Combine(Constants.Vrm.Dir,
                        "Published",
                        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "version");
                    string avatarVersion = null;
                    try
                    {
                        if (File.Exists(manifest) && new FileInfo(manifest).Length < 1024)
                        {
                            var previous = File.ReadAllLines(manifest);
                            if (previous.Length == 2 && previous[0] == signature)
                            {
                                SharingWire.ValidateHash(previous[1]);
                                avatarVersion = previous[1];
                            }
                        }
                    }
                    catch (Exception error) when (error is IOException || error is InvalidDataException)
                    {
                        Logger.LogOnce("publication-manifest-read",
                            "Could not read the local sharing record; preparing the avatar again.");
                    }

                    var prepareClock = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
                    var remembered = avatarVersion != null;
                    byte[] avatarZip = null;
                    var profile = BundleCrypto.PackProfile(settings, outfits);
                    Func<byte[]> packAvatar = () =>
                    {
                        if (avatarZip != null) return avatarZip;
                        var vrm = File.ReadAllBytes(path);
                        try
                        {
                            return avatarZip = BundleCrypto.PackAvatar(vrm);
                        }
                        finally
                        {
                            BundleCrypto.ClearBytes(ref vrm);
                        }
                    };
                    if (!remembered) avatarVersion = BundleCrypto.Version(packAvatar(), key);
                    BundleInfo result;
                    try
                    {
                        result = await SharingRpc.PublishAsync(client,
                                id,
                                avatarVersion,
                                packAvatar,
                                profile,
                                key,
                                cancellation,
                                epoch)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        BundleCrypto.ClearBytes(ref avatarZip);
                    }

                    file.Refresh();
                    if (file.Length == length && file.LastWriteTimeUtc.Ticks == modified)
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(manifest));
                            AvatarTcpClient.AtomicWrite(manifest,
                                System.Text.Encoding.UTF8.GetBytes(signature + "\n" + avatarVersion));
                        }
                        catch (IOException)
                        {
                            Logger.LogOnce("publication-manifest",
                                "Your avatar is shared, but its local record could not be saved.");
                        }
                    }

                    if (prepareClock != null)
                    {
                        Logger.Log("Avatar sharing checked in " +
                            prepareClock.Elapsed.TotalMilliseconds.ToString("F0") + "ms" +
                            (remembered ? " (model unchanged)." : "."));
                    }

                    return result;
                });
            }
        }

        private void PumpDownloads(Player local)
        {
            foreach (var stale in _downloads.Keys.Where(p => p == null || p.GetPlayerID() != _downloads[p].Id)
                         .ToArray())
            {
                CancelDownload(_downloads[stale]);
                _downloads.Remove(stale);
            }

            _client.BundleLimitBytes = SharingRpc.BundleLimitBytes;
            RefreshInventory();
            // Wait for the first background inventory before overriding installed models.
            if (_installedAvatars == null) return;
            // Before the local player spawns, the profile name is the only way to skip our own character.
            var localName = Game.instance.GetPlayerProfile()?.GetName();
            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null || player == local || player.IsDead() || player.GetPlayerID() == 0) continue;
                if (local == null && !string.IsNullOrEmpty(localName) && player.GetPlayerName() == localName) continue;
                // An explicitly installed avatar takes precedence over sharing and the default model.
                if (_installedAvatars.Contains(player.GetPlayerName())) continue;
                if (!_downloads.TryGetValue(player, out var download))
                {
                    download = new Download
                    {
                        Player = player,
                        Id = player.GetPlayerID(),
                        Stop = CancellationTokenSource.CreateLinkedTokenSource(_session.Token)
                    };
                    _downloads.Add(player, download);
                    if (Settings.LogLoadTiming)
                        Logger.Log(PlayerLabel(player) + ": waiting for the server to offer the avatar");
                }

                PumpDownload(download);
            }
        }

        private void PumpDownload(Download download)
        {
            if (download.Task != null && download.Task.IsCompleted)
            {
                if (download.Task.Status == TaskStatus.RanToCompletion && download.Task.Result != null)
                {
                    var received = download.Task.Result;
                    var beforeImport = FirstSeen.TryGetValue(download.Player, out var firstSeen)
                        ? firstSeen.Elapsed.TotalMilliseconds
                        : 0;
                    download.Installing = true;
                    VrmController.AttachSharedVrm(download.Player,
                        received.Bundle,
                        success =>
                        {
                            download.Installing = false;
                            if (success) download.LoadedHash = received.Info.Key;
                        },
                        download.Stop.Token,
                        received.Timing,
                        beforeImport);
                }
                else if (download.Task.IsFaulted)
                {
                    var error = download.Task.Exception.GetBaseException();
                    var terminal = error is SharingRpc.KeyUnavailable ||
                        error is System.Security.Cryptography.CryptographicException || error is InvalidDataException;
                    if (terminal)
                    {
                        download.BlockedHash = download.RequestedHash;
                        VrmController.RevealVanilla(download.Player);
                    }

                    Logger.LogOnce("receive-failed:" + download.Id + ":" + download.RequestedHash,
                        "Shared avatar for " + PlayerLabel(download.Player) + " is unavailable; last step: " +
                        download.LastStage +
                        "; " + error.Message + " Keeping the current model. " +
                        (terminal ? "Stopped for this avatar version." : "Trying again in 15 seconds."));
                }

                var failed = download.Task.IsFaulted || download.Task.IsCanceled;
                download.Task = null;
                // Retry only failed work. Availability comes from server events.
                download.NextCheck = failed ? Time.realtimeSinceStartup + 15 : 0;
            }

            if (download.Installing || download.Task != null || Time.realtimeSinceStartup < download.NextCheck) return;
            var info = SharingRpc.GetAvailable(download.Id);
            if (info == null || info.Key == download.LoadedHash || info.Key == download.BlockedHash) return;
            var client = _client;
            var cancellation = download.Stop.Token;
            long id = download.Id, epoch = SharingRpc.Epoch;
            var cache = Path.Combine(Constants.Vrm.Dir, "Shared");
            var lifecycle = Settings.LogLoadTiming && FirstSeen.TryGetValue(download.Player, out var seen)
                ? seen
                : null;
            var label = PlayerLabel(download.Player);
            if (lifecycle != null)
                Logger.Log(label + ": fetch scheduled at world+" + lifecycle.ElapsedMilliseconds + "ms");
            download.RequestedHash = info.Key;
            download.LastStage = "fetching from server";
            // A settings-only change must not re-read the model: the import cache is checked with the
            // new settings on the worker, and the model blob is skipped when it is already imported.
            Func<string, bool> modelAlreadyImported = settingsText =>
            {
                try
                {
                    return VrmAssetCache.IsImportedKey(VrmAssetCache.SharedKey(id,
                        info.Version,
                        new VrmSettings(label, settingsText)));
                }
                catch (Exception)
                {
                    return false;
                }
            };
            download.Task = Task.Run(async () =>
            {
                string timing = null;
                var bundle = await SharingRpc.ReceiveAsync(client,
                        id,
                        info,
                        cache,
                        cancellation,
                        epoch,
                        Settings.LogLoadTiming
                            ? (Action<string>)(text =>
                            {
                                timing = text;
                                download.LastStage = text;
                                Logger.Log(label + " world+" + (lifecycle?.ElapsedMilliseconds ?? 0) + "ms: " + text);
                            })
                            : null,
                        modelAlreadyImported)
                    .ConfigureAwait(false);
                if (cancellation.IsCancellationRequested)
                {
                    if (bundle.Vrm != null) Array.Clear(bundle.Vrm, 0, bundle.Vrm.Length);
                    return null;
                }

                return new Received { Info = info, Bundle = bundle, Timing = timing };
            });
        }

        internal static void ResetConnection()
        {
            if (_instance != null) _instance.StopSession();
        }

        internal static void ForgetCharacter(long id)
        {
            if (_instance == null) return;
            foreach (var pair in _instance._downloads.Where(p => p.Value.Id == id).ToArray())
            {
                CancelDownload(pair.Value);
                _instance._downloads.Remove(pair.Key);
                // The owner withdrew the avatar: a hidden body must not stay invisible.
                if (SharingRpc.GetAvailable(id) == null) VrmController.RevealVanilla(pair.Key);
            }
        }

        internal static void PlayerDestroyed(Player player)
        {
            if (ReferenceEquals(player, null)) return;
            FirstSeen.Remove(player);
            if (_instance == null) return;
            if (ReferenceEquals(player, _instance._localPlayer))
                _instance.ResetPublish();
            else if (_instance._downloads.TryGetValue(player, out var download))
            {
                CancelDownload(download);
                _instance._downloads.Remove(player);
            }
        }

        private static void CancelDownload(Download download)
        {
            download.Stop.Cancel();
            DiscardResult(download.Task);
            // The install coroutine may still read its token; disposal is not required
            // for a linked source after cancellation has unregistered it from its parent.
        }

        private static void DiscardResult(Task<Received> task)
        {
            if (task == null) return;
            task.ContinueWith(done =>
                {
                    if (done.Status == TaskStatus.RanToCompletion && done.Result?.Bundle.Vrm != null)
                        Array.Clear(done.Result.Bundle.Vrm, 0, done.Result.Bundle.Vrm.Length);
                    if (done.IsFaulted)
                    {
                        var observed = done.Exception;
                    }
                },
                TaskScheduler.Default);
        }

        private void StopSession()
        {
            ResetPublish();
            if (_session == null) return;
            SharingRpc.ClientStopped();
            _session.Cancel();
            var session = _session;
            var workers = new List<Task> { _upload ?? Task.FromResult<BundleInfo>(null) };
            foreach (var download in _downloads.Values)
            {
                CancelDownload(download);
                if (download.Task != null) workers.Add(download.Task);
            }

            Task.WhenAll(workers)
                .ContinueWith(done =>
                    {
                        var observed = done.Exception;
                        session.Dispose();
                    },
                    TaskScheduler.Default);
            _downloads.Clear();
            _session = null;
            _upload = null;
            _localPlayer = null;
            _key = null;
            _client = null;
            _localId = 0;
            _host = null;
            _installedAvatars = null;
            _inventoryRead = null;
            _nextInventory = 0;
        }

        private void OnDestroy()
        {
            StopSession();
            if (_instance == this) _instance = null;
        }
    }
}

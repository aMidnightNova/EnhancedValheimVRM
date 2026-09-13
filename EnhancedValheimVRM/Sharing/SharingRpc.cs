using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;

namespace EnhancedValheimVRM
{
    // Single owner of game peers, port discovery, publication and outfit routing.
    // The TCP worker only owns transfers/storage; Unity/RPC calls run in Tick/Receive.
    internal static class SharingRpc
    {
        private const int Protocol = 1;
        private const string RpcName = "EVRM_SharingControl1";
        private static readonly string ModVersion = typeof(SharingRpc).Assembly.GetName().Version.ToString();

        private static string LocalSharingVersion =>
            "EnhancedValheimVRM " + ModVersion + " (sharing protocol " + Protocol + ")";

        private enum Op
        {
            Subscribe = 0,
            Check = 1,
            Offer = 2,
            RequestKey = 4,
            KeyReply = 5,
            Stop = 6,
            Reply = 7,
            Available = 8,
            Absent = 9,
            AskKey = 10,
            Restart = 11,
            PortRequest = 12,
            Port = 13,
            SetOutfit = 14,
            Outfit = 15,
            SetOverride = 16,
            Override = 17,
            CancelKey = 18
        }

        private sealed class Message
        {
            internal Op Op;
            internal long Request, Id;
            internal string Version = "", Hash = "", Value = "", Ticket = "";
            internal string ProfileVersion = "", ProfileHash = "", ProfileTicket = "";

            internal BundleInfo Info =>
                new BundleInfo
                {
                    Version = Version, Hash = Hash, ProfileVersion = ProfileVersion, ProfileHash = ProfileHash
                };
        }

        private sealed class Peer
        {
            internal ZNetPeer GamePeer;
            internal string Session = Guid.NewGuid().ToString("N");
            internal long Id, CharacterId, OutfitId;
            internal bool Subscribed, PortRequested;
            internal int LastPort = -1, LastLimit = -1, LastUpload = -1;
            internal string Version, ProfileVersion, Outfit;
            internal BundleInfo Available;
            internal Message AwaitingCharacter, PendingOutfit;
            internal DateTime CharacterDeadline, OutfitDeadline;
            internal readonly Dictionary<string, Message> Overrides = new Dictionary<string, Message>();
        }

        private sealed class KeyRequest
        {
            internal Peer Owner, Recipient;
            internal string OwnerSession, RecipientSession;
            internal Message Original;
            internal DateTime Expires;
        }

        private sealed class Pending
        {
            internal TaskCompletionSource<Message> Completion;
            internal CancellationToken Token;
            internal long CharacterId;
            internal bool IsKeyRequest;
            internal DateTime Expires;
        }

        private static readonly ConcurrentQueue<Action> Dispatch = new ConcurrentQueue<Action>();
        private static readonly Dictionary<ZRpc, Peer> Peers = new Dictionary<ZRpc, Peer>();
        private static readonly HashSet<ZRpc> LivePeers = new HashSet<ZRpc>();
        private static readonly List<ZRpc> RemovedPeers = new List<ZRpc>();
        private static readonly List<long> Expired = new List<long>();
        private static readonly Dictionary<long, BundleInfo> Available = new Dictionary<long, BundleInfo>();

        private static readonly Dictionary<long, Dictionary<string, Message>> Overrides =
            new Dictionary<long, Dictionary<string, Message>>();

        private static readonly Dictionary<long, string> Outfits = new Dictionary<long, string>();
        private static readonly Dictionary<long, Pending> PendingCalls = new Dictionary<long, Pending>();
        private static readonly Dictionary<long, KeyRequest> KeyRequests = new Dictionary<long, KeyRequest>();
        private static ZNet _network;
        private static ZRpc _server;
        private static AvatarTcpServer _storage;
        private static long _sequence, _epoch, _localId, _sentOutfitId;
        private static string _localVersion, _localProfileVersion, _sentOutfit;
        private static bool _subscribed, _portReceived;
        private static int _port, _portAttempts;

        internal static int BundleLimitBytes { get; private set; } = SharingWire.DefaultBundleLimitBytes;

        // Server's per upload Mbps limit; 0 until the server announces it (or when it predates the limit).
        internal static int ServerUploadMbps { get; private set; }

        private static DateTime _nextPortRequest;
        private static System.Diagnostics.Stopwatch _timingClock;
        private static double _portStamp, _subscribeStamp;

        private static readonly ConcurrentDictionary<long, double> AvailabilityStamps =
            new ConcurrentDictionary<long, double>();

        private static readonly ConcurrentDictionary<long, long>
            AvailableSince = new ConcurrentDictionary<long, long>();

        // Milliseconds since the server last offered this character's avatar, or -1.
        internal static double MillisecondsSinceAvailable(long id)
        {
            return AvailableSince.TryGetValue(id, out var stamp)
                ? (System.Diagnostics.Stopwatch.GetTimestamp() - stamp) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency
                : -1;
        }

        private static void Trace(string text)
        {
            if (Settings.LogLoadTiming) Logger.Log(text);
        }

        internal static void Reset(ZNet network)
        {
            foreach (var peer in Peers.Values) _storage?.RevokeSession(peer.Session);
            Peers.Clear();
            LivePeers.Clear();
            KeyRequests.Clear();
            ClientStopped(false);
            _server = null;
            _storage = null;
            _network = network;
            _port = 0;
            BundleLimitBytes = SharingWire.DefaultBundleLimitBytes;
            ServerUploadMbps = 0;
            _portReceived = false;
            _portAttempts = 0;
            _nextPortRequest = default;
            Logger.ResetOnce();
            _timingClock = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            _portStamp = _subscribeStamp = 0;
        }

        internal static void RegisterPeer(ZNet network, ZNetPeer peer)
        {
            if (_network != network) Reset(network);
            if (Peers.ContainsKey(peer.m_rpc)) return;
            Peers[peer.m_rpc] = new Peer { GamePeer = peer, CharacterId = PeerCharacter.GetId(peer) };
            peer.m_rpc.Register<ZPackage>(RpcName, Receive);
        }

        private static bool Live(Peer peer)
        {
            return _network != null && _network == ZNet.instance &&
                Peers.TryGetValue(peer.GamePeer.m_rpc, out var current) &&
                ReferenceEquals(peer, current) && peer.GamePeer.IsReady();
        }

        internal static void RemovePeer(ZRpc rpc)
        {
            if (Peers.TryGetValue(rpc, out var peer))
            {
                ClearPeer(peer);
                Peers.Remove(rpc);
            }

            if (ReferenceEquals(_server, rpc))
            {
                ClientStopped(false);
                _server = null;
                _port = 0;
                _portReceived = false;
                _portAttempts = 0;
            }
        }

        private static void ClearPeer(Peer peer, bool clearOutfit = true)
        {
            // A publication change (clearOutfit=false) is not a connection reset.
            // Preserve this peer's requests/tickets for OTHER players' avatars.
            if (clearOutfit)
            {
                _storage?.RevokeSession(peer.Session);
                peer.Session = Guid.NewGuid().ToString("N");
            }

            _storage?.RevokeCharacter(peer.Id);
            if (peer.Available != null) Broadcast(new Message { Op = Op.Absent, Id = peer.Id });
            peer.Available = null;
            peer.Version = null;
            peer.Id = 0;
            peer.AwaitingCharacter = null;
            if (clearOutfit)
            {
                if (peer.OutfitId != 0) Broadcast(new Message { Op = Op.Outfit, Id = peer.OutfitId });
                peer.Overrides.Clear();
                peer.Outfit = null;
                peer.OutfitId = 0;
                peer.PendingOutfit = null;
            }

            foreach (var pair in KeyRequests.Where(p => p.Value.Owner == peer ||
                             (clearOutfit && p.Value.Recipient == peer))
                         .ToArray())
            {
                KeyRequests.Remove(pair.Key);
                // If a request really is invalidated, answer every still-connected
                // recipient immediately instead of stranding its 30-second RPC timer.
                if (Live(pair.Value.Recipient)) Reply(pair.Value.Recipient, pair.Value.Original);
            }
        }

        internal static void Tick(AvatarTcpServer storage)
        {
            if (_network != ZNet.instance) Reset(ZNet.instance);
            if (_network == null) return;
            LivePeers.Clear();
            foreach (var gamePeer in _network.GetPeers())
            {
                if (gamePeer.IsReady())
                {
                    LivePeers.Add(gamePeer.m_rpc);
                    RegisterPeer(_network, gamePeer);
                }
            }

            RemovedPeers.Clear();
            foreach (var rpc in Peers.Keys)
            {
                if (!LivePeers.Contains(rpc)) RemovedPeers.Add(rpc);
            }

            foreach (var rpc in RemovedPeers) RemovePeer(rpc);
            if (!ReferenceEquals(_storage, storage))
            {
                Broadcast(new Message { Op = Op.Restart });
                foreach (var peer in Peers.Values) ClearPeer(peer);
                _storage = storage;
            }

            foreach (var peer in Peers.Values)
            {
                peer.CharacterId = PeerCharacter.GetId(peer.GamePeer);
                if (peer.Id != 0 && peer.CharacterId != peer.Id) ClearPeer(peer);
                if (peer.OutfitId != 0 && peer.OutfitId != peer.CharacterId)
                {
                    Broadcast(new Message { Op = Op.Outfit, Id = peer.OutfitId });
                    peer.Overrides.Clear();
                    peer.Outfit = null;
                    peer.OutfitId = 0;
                }

                if (peer.AwaitingCharacter != null &&
                    (peer.CharacterId != 0 || peer.CharacterDeadline < DateTime.UtcNow))
                {
                    var request = peer.AwaitingCharacter;
                    peer.AwaitingCharacter = null;
                    if (peer.CharacterId != 0)
                        ReceiveServer(peer, request);
                    else
                        Reply(peer, request, value: "identity");
                }

                if (peer.PendingOutfit != null && (peer.CharacterId != 0 || peer.OutfitDeadline < DateTime.UtcNow))
                {
                    var request = peer.PendingOutfit;
                    peer.PendingOutfit = null;
                    if (peer.CharacterId != 0) ReceiveServer(peer, request);
                }

                var port = _storage?.Port ?? 0;
                if (_network.IsServer() && peer.PortRequested && (peer.LastPort != port ||
                        peer.LastLimit != (_storage?.BundleLimitBytes ?? SharingWire.DefaultBundleLimitBytes) ||
                        peer.LastUpload != (_storage?.UploadMbps ?? 0)))
                    AnnouncePort(peer, port);
            }

            if (!_network.IsServer())
            {
                var server = _network.GetServerPeer()?.m_rpc;
                if (!ReferenceEquals(_server, server))
                {
                    ClientStopped(false);
                    _server = server;
                    _port = 0;
                    _portReceived = false;
                    _portAttempts = 0;
                    _nextPortRequest = default;
                }

                if (_server != null && !_portReceived && Settings.EnableVrmSharing &&
                    DateTime.UtcNow >= _nextPortRequest)
                {
                    if (_portAttempts < 3)
                    {
                        _portAttempts++;
                        _nextPortRequest = DateTime.UtcNow.AddSeconds(5);
                        Send(_server,
                            new Message { Op = Op.PortRequest, Version = Protocol.ToString(), Hash = ModVersion });
                    }
                    else
                    {
                        _nextPortRequest = DateTime.MaxValue;
                        Logger.LogOnce("sharing-port-missing",
                            "VRM sharing is unavailable: the server did not answer. Install the same EnhancedValheimVRM version on the dedicated server and on every client, then restart them.");
                    }
                }
            }

            var count = 0;
            while (count++ < 64 && Dispatch.TryDequeue(out var action)) action();
            Expired.Clear();
            foreach (var pair in PendingCalls)
            {
                if (pair.Value.Token.IsCancellationRequested || pair.Value.Expires < DateTime.UtcNow)
                    Expired.Add(pair.Key);
            }

            foreach (var id in Expired)
            {
                var pending = PendingCalls[id];
                PendingCalls.Remove(id);
                if (pending.IsKeyRequest && _server != null)
                    Send(_server, new Message { Op = Op.CancelKey, Request = id, Id = pending.CharacterId });
                if (pending.Token.IsCancellationRequested)
                    pending.Completion.TrySetCanceled();
                else
                    pending.Completion.TrySetException(new TimeoutException("Sharing RPC timed out."));
            }

            Expired.Clear();
            foreach (var pair in KeyRequests)
            {
                if (pair.Value.Expires < DateTime.UtcNow) Expired.Add(pair.Key);
            }

            foreach (var id in Expired)
            {
                var request = KeyRequests[id];
                KeyRequests.Remove(id);
                if (Live(request.Recipient)) Reply(request.Recipient, request.Original);
            }
        }

        internal static bool TryGetServerPort(ZNet network, out int port)
        {
            port = _port;
            return network != null && network == _network && !network.IsServer() && _portReceived && port > 0 &&
                ReferenceEquals(network.GetServerPeer()?.m_rpc, _server);
        }

        private static void AnnouncePort(Peer peer, int port)
        {
            peer.LastPort = port;
            peer.LastLimit = _storage?.BundleLimitBytes ?? SharingWire.DefaultBundleLimitBytes;
            peer.LastUpload = _storage?.UploadMbps ?? 0;
            Send(peer.GamePeer.m_rpc,
                new Message
                {
                    Op = Op.Port,
                    Version = Protocol.ToString(),
                    Hash = ModVersion,
                    Value = port.ToString(),
                    Request = _storage?.BundleLimitBytes ?? SharingWire.DefaultBundleLimitBytes,
                    Id = peer.LastUpload
                });
        }

        internal static void ClientReady()
        {
            if (!Settings.EnableVrmSharing || !TryGetServerPort(ZNet.instance, out _) || _subscribed) return;
            _subscribed = true;
            _subscribeStamp = _timingClock?.Elapsed.TotalMilliseconds ?? 0;
            Send(_server, new Message { Op = Op.Subscribe });
            OutfitRpc.ClientReady();
        }

        internal static void ClientStopped(bool notify = true)
        {
            if (notify && _subscribed && _server != null && _network == ZNet.instance)
                Send(_server, new Message { Op = Op.Stop });
            Interlocked.Increment(ref _epoch);
            _subscribed = false;
            _localId = 0;
            _localVersion = null;
            _localProfileVersion = null;
            _sentOutfitId = 0;
            _sentOutfit = null;
            foreach (var pending in PendingCalls.Values) pending.Completion.TrySetCanceled();
            PendingCalls.Clear();
            Available.Clear();
            Outfits.Clear();
            Overrides.Clear();
            AvailabilityStamps.Clear();
            AvailableSince.Clear();
        }

        internal static BundleInfo GetAvailable(long id)
        {
            return Available.TryGetValue(id, out var value) ? value : null;
        }

        internal static string GetOutfit(long id)
        {
            return Outfits.TryGetValue(id, out var value) ? value : null;
        }

        internal static void SetOutfit(long id, string name)
        {
            ClientReady();
            if (!_subscribed || id == 0 || (_sentOutfitId == id &&
                    string.Equals(name, _sentOutfit, StringComparison.OrdinalIgnoreCase)))
                return;
            SharingWire.ValidateOutfitName(name);
            _sentOutfitId = id;
            _sentOutfit = name;
            Send(_server, new Message { Op = Op.SetOutfit, Id = id, Value = name });
        }

        internal static void SelectOutfit(long id, string name)
        {
            _sentOutfit = null;
            SetOutfit(id, name);
        }

        internal static void SetOverride(long id, string name, bool blend, float value)
        {
            if (!_subscribed || id == 0 || !SharingWire.IsValidOverride(name, blend, value)) return;
            Send(_server,
                new Message
                {
                    Op = Op.SetOverride,
                    Id = id,
                    Value = name,
                    Version = blend ? "blend" : "mesh",
                    Hash = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        internal static void ApplyOverrides(long id, Action<string, bool, float> apply)
        {
            if (!Overrides.TryGetValue(id, out var values)) return;
            foreach (var m in values.Values)
            {
                apply(m.Value,
                    m.Version == "blend",
                    float.Parse(m.Hash, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static bool ValidOverride(Message m)
        {
            return (m.Version == "mesh" || m.Version == "blend") &&
                float.TryParse(m.Hash,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value) &&
                SharingWire.IsValidOverride(m.Value, m.Version == "blend", value);
        }

        private static void Send(ZRpc rpc, Message message)
        {
            var p = new ZPackage();
            p.Write((int)message.Op);
            p.Write(message.Request);
            p.Write(message.Id);
            p.Write(message.Version);
            p.Write(message.Hash);
            p.Write(message.Value);
            p.Write(message.Ticket);
            p.Write(message.ProfileVersion);
            p.Write(message.ProfileHash);
            p.Write(message.ProfileTicket);
            rpc.Invoke(RpcName, p);
        }

        private static void Receive(ZRpc rpc, ZPackage package)
        {
            if (_network == null || _network != ZNet.instance || package.Size() > 2048 ||
                !Peers.TryGetValue(rpc, out var peer) || !Live(peer))
                return;
            try
            {
                var m = new Message
                {
                    Op = (Op)package.ReadInt(),
                    Request = package.ReadLong(),
                    Id = package.ReadLong(),
                    Version = package.ReadString(),
                    Hash = package.ReadString(),
                    Value = package.ReadString(),
                    Ticket = package.ReadString(),
                    ProfileVersion = package.ReadString(),
                    ProfileHash = package.ReadString(),
                    ProfileTicket = package.ReadString()
                };
                if (m.Version.Length > 64 || m.Hash.Length > 64 || m.Value.Length > 256 || m.Ticket.Length > 64 ||
                    m.ProfileVersion.Length > 64 || m.ProfileHash.Length > 64 || m.ProfileTicket.Length > 64)
                    return;
                if (_network.IsServer())
                {
                    if (!_network.IsDedicated()) return;
                    if (m.Op == Op.PortRequest)
                    {
                        peer.PortRequested = m.Version == Protocol.ToString();
                        if (!peer.PortRequested)
                        {
                            Logger.LogOnce("sharing-client-protocol:" + peer.Session,
                                "A client is running a different EnhancedValheimVRM version; sharing is disabled for it until both sides match.");
                        }

                        AnnouncePort(peer,
                            peer.PortRequested && Settings.EnableSharingServer ? _storage?.Port ?? 0 : 0);
                        return;
                    }

                    if (Settings.EnableSharingServer && _storage != null) ReceiveServer(peer, m);
                }
                else if (ReferenceEquals(_network.GetServerPeer()?.m_rpc, rpc))
                {
                    _server = rpc;
                    ReceiveClient(m);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException ||
                                       ex is FormatException)
            {
                /* Invalid control data is never logged with a key/payload. */
            }
        }

        private static void ReceiveServer(Peer peer, Message m)
        {
            if (m.Op == Op.Subscribe)
            {
                peer.Subscribed = true;
                foreach (var owner in Peers.Values)
                {
                    if (owner.Available != null) Send(peer.GamePeer.m_rpc, Metadata(owner));
                    if (owner.Outfit != null)
                    {
                        Send(peer.GamePeer.m_rpc,
                            new Message { Op = Op.Outfit, Id = owner.OutfitId, Value = owner.Outfit });
                    }

                    foreach (var change in owner.Overrides.Values) Send(peer.GamePeer.m_rpc, change);
                }

                return;
            }

            if (!peer.Subscribed) return;
            peer.CharacterId =
                PeerCharacter.GetId(peer.GamePeer); // Revalidate on a control event, not a second per-frame scan.
            if (m.Op == Op.Stop)
            {
                ClearPeer(peer);
                peer.Subscribed = false;
                return;
            }

            if (m.Op == Op.SetOutfit)
            {
                SharingWire.ValidateOutfitName(m.Value);
                if (peer.CharacterId == 0)
                {
                    peer.PendingOutfit = m;
                    peer.OutfitDeadline = DateTime.UtcNow.AddSeconds(20);
                    return;
                }

                if (peer.CharacterId != m.Id) return;
                peer.Overrides.Clear();
                peer.OutfitId = m.Id;
                peer.Outfit = m.Value;
                Broadcast(new Message { Op = Op.Outfit, Id = m.Id, Value = m.Value });
                return;
            }

            if (m.Op == Op.SetOverride)
            {
                if (m.Id == 0 || peer.CharacterId != m.Id || !ValidOverride(m)) return;
                var key = m.Version + ":" + m.Value;
                if (peer.Overrides.Count >= 512 && !peer.Overrides.ContainsKey(key)) return;
                peer.OutfitId = m.Id;
                m.Op = Op.Override;
                peer.Overrides[key] = m;
                Broadcast(m);
                return;
            }

            if (m.Op == Op.KeyReply)
            {
                if (!KeyRequests.TryGetValue(m.Request, out var request))
                {
                    Trace("Avatar request " + m.Request + ": owner answered after the request expired");
                    return;
                }

                if (request.Owner != peer || request.OwnerSession != peer.Session)
                {
                    Trace("Avatar request " + m.Request + ": owner answer ignored, connection changed");
                    return;
                }

                if (peer.CharacterId != peer.Id)
                {
                    Trace("Avatar request " + m.Request + ": owner answer ignored, owner character is " +
                        peer.CharacterId + " but published as " + peer.Id);
                    return;
                }

                KeyRequests.Remove(m.Request);
                if (!Live(request.Recipient) || request.Recipient.Session != request.RecipientSession)
                {
                    Trace("Avatar request " + m.Request + ": requester left");
                    return;
                }

                var original = request.Original;
                if (peer.Available == null || !peer.Available.SameAs(original.Info) ||
                    !BundleCrypto.IsValidKey(m.Value))
                {
                    Trace("Avatar request " + m.Request + ": owner answered " +
                        (m.Hash == "denied" ? "declined" : "not ready") +
                        (peer.Available == null ? " (nothing published)" : ""));
                    Reply(request.Recipient, original, value: m.Hash == "denied" ? "denied" : "not-ready");
                    return;
                }

                Trace("Avatar request " + m.Request + ": granted");

                Reply(request.Recipient,
                    original,
                    peer.Available,
                    m.Value,
                    _storage.AuthorizeTransfer(request.Recipient.Session,
                        peer.Id,
                        SharingWire.AvatarKind,
                        peer.Available,
                        false),
                    _storage.AuthorizeTransfer(request.Recipient.Session,
                        peer.Id,
                        SharingWire.ProfileKind,
                        peer.Available,
                        false));
                return;
            }

            if (m.Op == Op.CancelKey)
            {
                foreach (var pair in KeyRequests.Where(p => p.Value.Recipient == peer &&
                                 p.Value.Original.Request == m.Request && p.Value.Original.Id == m.Id)
                             .ToArray())
                    KeyRequests.Remove(pair.Key);
                return;
            }

            if (m.Op == Op.RequestKey)
            {
                // A retry replaces the same peer's older request for this character.
                foreach (var pair in KeyRequests.Where(p => p.Value.Recipient == peer &&
                                 p.Value.Original.Id == m.Id)
                             .ToArray())
                {
                    KeyRequests.Remove(pair.Key);
                    Reply(peer, pair.Value.Original, value: "superseded");
                }

                var owner = Peers.Values.FirstOrDefault(p =>
                    p.Id == m.Id && p.Available != null && p.Available.SameAs(m.Info));
                if (owner == null || KeyRequests.Count >= 128)
                {
                    Trace("Avatar request for " + m.Id + ": " + (owner == null
                        ? "no connected owner has that avatar published"
                        : "too many pending requests"));
                    Reply(peer, m);
                    return;
                }

                var id = Interlocked.Increment(ref _sequence);
                Trace("Avatar request " + id + " for " + m.Id + ": forwarded to the owner");
                KeyRequests[id] = new KeyRequest
                {
                    Owner = owner,
                    Recipient = peer,
                    OwnerSession = owner.Session,
                    RecipientSession = peer.Session,
                    Original = m,
                    Expires = DateTime.UtcNow.AddSeconds(20)
                };
                Send(owner.GamePeer.m_rpc,
                    new Message
                    {
                        Op = Op.AskKey,
                        Request = id,
                        Id = m.Id,
                        Version = m.Version,
                        Hash = m.Hash,
                        ProfileVersion = m.ProfileVersion,
                        ProfileHash = m.ProfileHash
                    });
                return;
            }

            SharingWire.ValidateHash(m.Version);
            if (m.Op == Op.Check && m.Id != 0 && peer.CharacterId == 0)
            {
                if (peer.AwaitingCharacter != null) Reply(peer, peer.AwaitingCharacter, value: "identity");
                peer.AwaitingCharacter = m;
                peer.CharacterDeadline = DateTime.UtcNow.AddSeconds(20);
                return;
            }

            if (m.Id == 0 || peer.CharacterId != m.Id)
            {
                Reply(peer, m, value: "identity");
                return;
            }

            if (m.Op == Op.Check)
            {
                SharingWire.ValidateHash(m.ProfileVersion);
                // An unchanged reload must not withdraw availability, outfits or tickets.
                if (peer.Id == m.Id && peer.Available?.Version == m.Version &&
                    peer.Available?.ProfileVersion == m.ProfileVersion)
                {
                    Reply(peer, m, peer.Available);
                    return;
                }

                var session = peer.Session;
                var storage = _storage;
                Task.Run(() => storage.ReadCurrent(m.Id))
                    .ContinueWith(done => Dispatch.Enqueue(() =>
                        {
                            if (!Live(peer) || peer.Session != session || !ReferenceEquals(storage, _storage))
                            {
                                var observed = done.Exception;
                                return;
                            }

                            if (done.Status != TaskStatus.RanToCompletion)
                            {
                                var observed = done.Exception;
                                Reply(peer, m, value: "storage");
                                return;
                            }

                            var info = done.Result;
                            if (peer.Id != m.Id || peer.Version != m.Version || peer.ProfileVersion != m.ProfileVersion)
                                ClearPeer(peer, false);
                            peer.Id = m.Id;
                            peer.Version = m.Version;
                            peer.ProfileVersion = m.ProfileVersion;
                            peer.Available = info != null && info.IsComplete && info.Version == m.Version &&
                                info.ProfileVersion == m.ProfileVersion
                                    ? info
                                    : null;
                            // Report whatever is stored, so the owner uploads only the missing blob.
                            Reply(peer, m, info);
                            if (peer.Available != null) Broadcast(Metadata(peer));
                        }),
                        TaskScheduler.Default);
            }
            else if (m.Op == Op.Offer && peer.Id == m.Id)
            {
                var kind = m.Value;
                SharingWire.ValidateKind(kind);
                var offered = m.Info;
                var declared = kind == SharingWire.AvatarKind ? peer.Version : peer.ProfileVersion;
                if (offered.VersionOf(kind) != declared)
                {
                    Reply(peer, m, value: "publication-state");
                    return;
                }

                Reply(peer, m, offered, ticket: _storage.AuthorizeTransfer(peer.Session, m.Id, kind, offered, true));
            }
            else
                Reply(peer, m, value: "publication-state");
        }

        internal static void UploadCompleted(AvatarTcpServer storage, string session, long id, BundleInfo info)
        {
            Dispatch.Enqueue(() =>
            {
                if (!ReferenceEquals(storage, _storage)) return;
                var peer = Peers.Values.FirstOrDefault(p => p.Session == session && p.Id == id && p.CharacterId == id);
                // Availability needs both blobs stored at the versions this peer declared.
                if (peer == null || !info.IsComplete || info.Version != peer.Version ||
                    info.ProfileVersion != peer.ProfileVersion)
                    return;
                peer.Available = info;
                Broadcast(Metadata(peer));
            });
        }

        private static Message Metadata(Peer p)
        {
            return new Message
            {
                Op = Op.Available,
                Id = p.Id,
                Version = p.Available.Version,
                Hash = p.Available.Hash,
                ProfileVersion = p.Available.ProfileVersion,
                ProfileHash = p.Available.ProfileHash
            };
        }

        private static void Broadcast(Message m)
        {
            foreach (var p in Peers.Values)
            {
                if (p.Subscribed) Send(p.GamePeer.m_rpc, m);
            }
        }

        private static void Reply(Peer p,
            Message original,
            BundleInfo info = null,
            string value = "",
            string ticket = "",
            string profileTicket = "")
        {
            Send(p.GamePeer.m_rpc,
                new Message
                {
                    Op = Op.Reply,
                    Request = original.Request,
                    Id = original.Id,
                    Version = info?.Version ?? "",
                    Hash = info?.Hash ?? "",
                    ProfileVersion = info?.ProfileVersion ?? "",
                    ProfileHash = info?.ProfileHash ?? "",
                    Value = value,
                    Ticket = ticket,
                    ProfileTicket = profileTicket
                });
        }

        private static void ReceiveClient(Message m)
        {
            if (m.Op == Op.Port)
            {
                int port;
                if (m.Version != Protocol.ToString())
                {
                    Logger.LogOnce("sharing-server-protocol",
                        "The server runs a different EnhancedValheimVRM version; update the server and clients to the same build. VRM sharing is disabled.");
                }

                if (m.Version != Protocol.ToString() || !int.TryParse(m.Value, out port) || port < 0 ||
                    port > 65535)
                    port = 0;
                var limit = m.Request >= 1048576 && m.Request <= SharingWire.MaxBundleBytes ? (int)m.Request : 0;
                if (limit == 0) port = 0;
                // Servers older than the upload limit send 0; anything above the hard limit is capped.
                var upload = m.Id <= 0 ? 0 : (int)Math.Min(m.Id, SharingUploadPolicy.HardLimitMbps);
                if (_portReceived && (_port != port || BundleLimitBytes != limit || ServerUploadMbps != upload))
                    FileTransferController.ResetConnection();
                BundleLimitBytes = limit;
                ServerUploadMbps = upload;
                _port = port;
                _portReceived = true;
                _portStamp = _timingClock?.Elapsed.TotalMilliseconds ?? 0;
                ClientReady();
                return;
            }

            if (!_subscribed) return;
            if (m.Op == Op.Restart)
            {
                FileTransferController.ResetConnection();
                ClientStopped(false);
                ClientReady();
            }
            else if (m.Op == Op.Reply && PendingCalls.TryGetValue(m.Request, out var pending))
            {
                PendingCalls.Remove(m.Request);
                if (pending.Token.IsCancellationRequested)
                    pending.Completion.TrySetCanceled();
                else
                    pending.Completion.TrySetResult(m);
            }
            else if (m.Op == Op.Available)
            {
                SharingWire.ValidateId(m.Id);
                var offered = m.Info;
                offered.Validate();
                if (Available.TryGetValue(m.Id, out var previousInfo) && !previousInfo.SameAs(offered))
                    FileTransferController.ForgetCharacter(m.Id);
                Available[m.Id] = offered;
                AvailableSince[m.Id] = System.Diagnostics.Stopwatch.GetTimestamp();
                Trace(FileTransferController.PlayerLabel(m.Id) + ": avatar available from the server");
                if (_timingClock != null) AvailabilityStamps[m.Id] = _timingClock.Elapsed.TotalMilliseconds;
            }
            else if (m.Op == Op.Absent)
            {
                Available.Remove(m.Id);
                AvailabilityStamps.TryRemove(m.Id, out _);
                AvailableSince.TryRemove(m.Id, out _);
                FileTransferController.ForgetCharacter(m.Id);
            }
            else if (m.Op == Op.Outfit)
            {
                Overrides.Remove(m.Id);
                if (m.Value == "")
                    Outfits.Remove(m.Id);
                else
                {
                    SharingWire.ValidateOutfitName(m.Value);
                    Outfits[m.Id] = m.Value;
                    OutfitRpc.SelectionReceived(m.Id);
                }
            }
            else if (m.Op == Op.Override)
            {
                if (m.Id == 0 || !ValidOverride(m)) return;
                if (!Overrides.TryGetValue(m.Id, out var values))
                    Overrides[m.Id] = values = new Dictionary<string, Message>();
                var key = m.Version + ":" + m.Value;
                if (values.Count >= 512 && !values.ContainsKey(key)) return;
                values[key] = m;
                OutfitRpc.SelectionReceived(m.Id);
            }
            else if (m.Op == Op.AskKey)
            {
                var player = Player.m_localPlayer;
                var avatar = VrmController.FindSharingInstance(player);
                var denied = !Settings.EnableVrmSharing || avatar?.GetSettings().AllowShare == false;
                var allow = !denied && player != null && player.GetPlayerID() == m.Id && m.Id == _localId &&
                    m.Version == _localVersion && m.ProfileVersion == _localProfileVersion && avatar != null;
                Trace("Another player asked for your avatar: " + (allow
                    ? "granted"
                    : denied
                        ? "declined (sharing is off)"
                        : player == null
                            ? "not ready (no local character yet)"
                            : player.GetPlayerID() != m.Id
                                ? "not ready (different character)"
                                : m.Id != _localId || m.Version != _localVersion ||
                                m.ProfileVersion != _localProfileVersion
                                    ? "not ready (your avatar is not published yet)"
                                    : "not ready (your avatar is not loaded)"));
                Send(_server,
                    new Message
                    {
                        Op = Op.KeyReply,
                        Request = m.Request,
                        Value = allow ? Settings.VrmKey : "",
                        Hash = denied ? "denied" : "not-ready"
                    });
            }
        }

        private static Task<Message> Call(Message m, CancellationToken cancellation, long epoch)
        {
            return CallTimed(m, cancellation, epoch, 30);
        }

        private static Task<Message> CallTimed(Message m,
            CancellationToken cancellation,
            long epoch,
            int timeoutSeconds)
        {
            var completion = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatch.Enqueue(() =>
            {
                if (cancellation.IsCancellationRequested || epoch != Epoch || !_subscribed || _server == null)
                {
                    completion.TrySetCanceled();
                    return;
                }

                m.Request = Interlocked.Increment(ref _sequence);
                if (m.Op == Op.Check)
                {
                    _localId = m.Id;
                    _localVersion = m.Version;
                    _localProfileVersion = m.ProfileVersion;
                }

                PendingCalls[m.Request] = new Pending
                {
                    Completion = completion,
                    Token = cancellation,
                    Expires = DateTime.UtcNow.AddSeconds(timeoutSeconds),
                    IsKeyRequest = m.Op == Op.RequestKey,
                    CharacterId = m.Id
                };
                if (m.Op == Op.RequestKey) Trace(FileTransferController.PlayerLabel(m.Id) + ": fetching from server");
                Send(_server, m);
            });
            return completion.Task;
        }

        internal sealed class PublishFailure : IOException
        {
            internal readonly string Stage;

            internal PublishFailure(string stage, Exception cause) : base("Sharing failed during " + stage + ".", cause)
            {
                Stage = stage;
            }
        }

        // Returns what the server stores for this character (possibly only one blob), or null.
        internal static async Task<BundleInfo> CheckVersionAsync(long id,
            string version,
            string profileVersion,
            CancellationToken token,
            long epoch)
        {
            var result = await Call(
                    new Message { Op = Op.Check, Id = id, Version = version, ProfileVersion = profileVersion },
                    token,
                    epoch)
                .ConfigureAwait(false);
            if (result.Value != "") throw new IOException("The server could not verify this character.");
            return result.Hash == "" && result.ProfileHash == "" ? null : result.Info;
        }

        // Uploads only what the server lacks. The model is packed lazily because a remembered
        // version usually means it is already stored; the settings blob is small and always ready.
        internal static async Task<BundleInfo> PublishAsync(AvatarTcpClient client,
            long id,
            string avatarVersion,
            Func<byte[]> packAvatar,
            byte[] profile,
            string key,
            CancellationToken token,
            long epoch)
        {
            var stage = "checking the stored version";
            byte[] packed = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var profileVersion = BundleCrypto.Version(profile, key);
                var existing = await CheckVersionAsync(id, avatarVersion, profileVersion, token, epoch)
                    .ConfigureAwait(false);
                var needAvatar = existing == null || !existing.HasAvatar || existing.Version != avatarVersion;
                // The settings file is named after the model, so a new model needs it again too.
                var needProfile = needAvatar || !existing.HasProfile || existing.ProfileVersion != profileVersion;
                if (!needAvatar && !needProfile) return existing;
                var info = new BundleInfo
                {
                    Version = avatarVersion, Hash = existing?.Hash ?? "", ProfileVersion = profileVersion
                };
                if (needAvatar)
                {
                    stage = "preparing the model";
                    packed = packAvatar();
                    info.Hash = await UploadAsync(client,
                            id,
                            SharingWire.AvatarKind,
                            info,
                            packed,
                            key,
                            token,
                            epoch,
                            text => stage = text)
                        .ConfigureAwait(false);
                    packed = null;
                }

                info.ProfileHash = await UploadAsync(client,
                        id,
                        SharingWire.ProfileKind,
                        info,
                        profile,
                        key,
                        token,
                        epoch,
                        text => stage = text)
                    .ConfigureAwait(false);
                return info; // The server announces availability once both blobs are stored.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PublishFailure(stage, ex);
            }
            finally
            {
                BundleCrypto.ClearBytes(ref packed);
                BundleCrypto.ClearBytes(ref profile);
            }
        }

        private static async Task<string> UploadAsync(AvatarTcpClient client,
            long id,
            string kind,
            BundleInfo info,
            byte[] plaintext,
            string key,
            CancellationToken token,
            long epoch,
            Action<string> stage)
        {
            var label = kind == SharingWire.AvatarKind ? "model" : "settings";
            stage("checking the " + label + " size");
            if (plaintext.Length > client.BundleLimitBytes - 64)
            {
                throw new InvalidDataException("Bundle exceeds the server limit of " +
                    client.BundleLimitBytes / 1048576 + " MiB.");
            }

            stage("packing the " + label);
            byte[] encrypted;
            try
            {
                encrypted = BundleCrypto.Encrypt(plaintext, key, info.VersionOf(kind), id);
            }
            finally
            {
                BundleCrypto.ClearBytes(ref plaintext);
            }

            var hash = BundleCrypto.Hash(encrypted);
            var offered = info.Clone();
            if (kind == SharingWire.AvatarKind)
                offered.Hash = hash;
            else
                offered.ProfileHash = hash;
            stage("requesting permission to upload the " + label);
            var offer = await Call(new Message
                    {
                        Op = Op.Offer,
                        Id = id,
                        Value = kind,
                        Version = offered.Version,
                        Hash = offered.Hash,
                        ProfileVersion = offered.ProfileVersion,
                        ProfileHash = offered.ProfileHash
                    },
                    token,
                    epoch)
                .ConfigureAwait(false);
            if (offer.Ticket == "") throw new IOException("Upload not authorized.");
            stage("uploading the " + label);
            client.UploadBlob(id, offer.Ticket, encrypted, token);
            return hash;
        }

        // Retry numbers 1..20 follow one immediate request. Zero means the budget is exhausted.
        internal static int KeyRetryDelaySeconds(int retry)
        {
            return retry < 1 || retry > 20 ? 0 : retry <= 10 ? 2 : retry <= 15 ? 5 : 10;
        }

        internal sealed class KeyUnavailable : IOException
        {
            internal KeyUnavailable(string message) : base(message) { }
        }

        private static async Task<Message> RequestKeyAsync(long id,
            BundleInfo info,
            CancellationToken token,
            long epoch,
            Action<string> timing)
        {
            for (var attempt = 0; attempt <= 20; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var waitSeconds = KeyRetryDelaySeconds(Math.Min(attempt + 1, 20));
                var attemptClock = System.Diagnostics.Stopwatch.StartNew();
                timing?.Invoke("fetching from server (attempt " + (attempt + 1) + ")");
                Message grant = null;
                var timedOut = false;
                try
                {
                    grant = await CallTimed(new Message
                            {
                                Op = Op.RequestKey,
                                Id = id,
                                Version = info.Version,
                                Hash = info.Hash,
                                ProfileVersion = info.ProfileVersion,
                                ProfileHash = info.ProfileHash
                            },
                            token,
                            epoch,
                            waitSeconds)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    timedOut = true;
                }

                token.ThrowIfCancellationRequested();
                if (grant != null)
                {
                    if (grant.Value == "denied") throw new KeyUnavailable("The avatar owner has disabled sharing.");
                    if (BundleCrypto.IsValidKey(grant.Value))
                    {
                        if (!grant.Info.SameAs(info) || grant.Ticket == "" || grant.ProfileTicket == "")
                        {
                            throw new System.Security.Cryptography.CryptographicException(
                                "The server's response did not match the requested avatar.");
                        }

                        return grant;
                    }

                    if (grant.Value != "" && grant.Value != "not-ready" && grant.Value != "superseded")
                        throw new KeyUnavailable("The server declined the request.");
                }

                timing?.Invoke(timedOut
                    ? "no answer from the server within " + waitSeconds + "s"
                    : "server answered: " + (grant.Value == "" ? "no owner has it yet" :
                        grant.Value == "not-ready" ? "owner not ready yet" : "request replaced"));
                if (attempt == 20) break;
                // A definitive answer arrives in a fraction of a second; ask again soon instead of
                // sleeping out the window. Only silence, or repeated refusals, use the slower cadence.
                var remaining = !timedOut && attempt < 3
                    ? 250
                    : Math.Max(0, waitSeconds * 1000 - (int)attemptClock.ElapsedMilliseconds);
                if (remaining != 0) await Task.Delay(remaining, token).ConfigureAwait(false);
            }

            throw new KeyUnavailable(
                "The server did not provide this avatar after repeated requests; giving up until it changes or the owner reconnects.");
        }

        internal static long Epoch => Interlocked.Read(ref _epoch);

        internal static async Task<AvatarBundle> ReceiveAsync(AvatarTcpClient client,
            long id,
            BundleInfo info,
            string cache,
            CancellationToken token,
            long epoch,
            Action<string> timing = null,
            Func<string, bool> modelAlreadyImported = null)
        {
            var clock = timing == null ? null : _timingClock;
            var keySent = clock?.Elapsed.TotalMilliseconds ?? 0;
            AvailabilityStamps.TryGetValue(id, out var availableAt);
            var grant = await RequestKeyAsync(id, info, token, epoch, timing).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!BundleCrypto.IsValidKey(grant.Value) || !grant.Info.SameAs(info) || grant.Ticket == "" ||
                grant.ProfileTicket == "")
                throw new IOException("The avatar owner has not made it available.");
            var granted = clock?.Elapsed.TotalMilliseconds ?? 0;
            timing?.Invoke("server accepted the request in " + (granted - keySent).ToString("F0") + "ms");
            timing?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "since connect: ready={0:F0}ms, available={1:F0}ms, requested={2:F0}ms, accepted={3:F0}ms",
                _subscribeStamp,
                availableAt,
                keySent,
                granted));
            return client.Receive(id,
                info,
                grant.Value,
                cache,
                token,
                grant.Ticket,
                grant.ProfileTicket,
                timing,
                modelAlreadyImported);
        }
    }
}

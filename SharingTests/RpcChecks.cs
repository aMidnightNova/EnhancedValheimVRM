using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM;
using EnhancedValheimVRM.Sharing;

internal static class RpcChecks
{
    // Wire operation numbers are part of protocol 1; the production handlers are linked into this project.
    internal sealed class Message
    {
        internal int Op;
        internal long Request, Id;
        internal string Version, Hash, Value, Ticket, ProfileVersion, ProfileHash, ProfileTicket;
    }

    internal static ZPackage Packet(int op,
        long request = 0,
        long id = 0,
        string version = "",
        string hash = "",
        string value = "",
        string ticket = "",
        string profileVersion = "",
        string profileHash = "",
        string profileTicket = "")
    {
        var packet = new ZPackage();
        packet.Write(op);
        packet.Write(request);
        packet.Write(id);
        packet.Write(version);
        packet.Write(hash);
        packet.Write(value);
        packet.Write(ticket);
        packet.Write(profileVersion);
        packet.Write(profileHash);
        packet.Write(profileTicket);
        return packet;
    }

    internal static Message Read(ZPackage packet)
    {
        packet.Rewind();
        return new Message
        {
            Op = packet.ReadInt(),
            Request = packet.ReadLong(),
            Id = packet.ReadLong(),
            Version = packet.ReadString(),
            Hash = packet.ReadString(),
            Value = packet.ReadString(),
            Ticket = packet.ReadString(),
            ProfileVersion = packet.ReadString(),
            ProfileHash = packet.ReadString(),
            ProfileTicket = packet.ReadString()
        };
    }

    private static ZPackage Check(long request, long id, BundleInfo info)
    {
        return Packet(1, request, id, info.Version, profileVersion: info.ProfileVersion);
    }

    private static ZPackage KeyRequest(long request, long id, BundleInfo info)
    {
        return Packet(4,
            request,
            id,
            info.Version,
            info.Hash,
            profileVersion: info.ProfileVersion,
            profileHash: info.ProfileHash);
    }

    private static Message Last(ZNetPeer peer, int op)
    {
        return peer.m_rpc.Sent.Select(Read).Last(m => m.Op == op);
    }

    private static async Task Drain(AvatarTcpServer storage, Func<bool> until)
    {
        var end = DateTime.UtcNow.AddSeconds(3);
        do
        {
            SharingRpc.Tick(storage);
            if (until()) return;
            await Task.Delay(5);
        } while (DateTime.UtcNow < end);

        throw new Exception("RPC completion did not arrive");
    }

    private static ZNetPeer Add(ZNet net, long id)
    {
        // The real current game leaves m_playerID at zero. Identity arrives in its owned ZDO.
        var peer = new ZNetPeer { m_playerID = 0, m_uid = id + 10000, m_characterID = new ZDOID(id + 90000) };
        ZDOMan.instance.Objects[peer.m_characterID] = new ZDO { Owner = peer.m_uid, CharacterId = id };
        net.Peers.Add(peer);
        SharingRpc.RegisterPeer(net, peer);
        peer.m_rpc.Deliver(Packet(0));
        return peer;
    }

    internal static async Task Run(string root, Action<bool, string> check)
    {
        using (var stop = new CancellationTokenSource())
        using (var storage = new AvatarTcpServer(Path.Combine(root, "rpc"),
                   IPAddress.Loopback,
                   0,
                   new SharingDownloadPolicy(25, 4)))
        {
            storage.Uploaded += (session, id, info) => SharingRpc.UploadCompleted(storage, session, id, info);
            var serving = storage.RunAsync(stop.Token);
            var net = new ZNet();
            ZNet.instance = net;
            SharingRpc.Reset(net);
            SharingRpc.Tick(storage);
            var owner = Add(net, 3001);
            owner.m_rpc.Deliver(Packet(12, version: "1"));
            check(Last(owner, 13).Request == 384L * 1048576,
                "Server did not announce its default compressed bundle limit");
            var recipient = Add(net, 3002);
            var key = BundleCrypto.GenerateKey();
            var packed = BundleCrypto.PackAvatar(new byte[64]);
            var profile = BundleCrypto.PackProfile("", "[Default]\nDefault=True");
            var encrypted = TestBundle.Encrypt(packed, key, 3001);
            var encryptedProfile = TestBundle.Encrypt(profile, key, 3001);
            var info = new BundleInfo
            {
                Version = BundleCrypto.Version(packed, key),
                Hash = BundleCrypto.Hash(encrypted),
                ProfileVersion = BundleCrypto.Version(profile, key),
                ProfileHash = BundleCrypto.Hash(encryptedProfile)
            };
            recipient.m_rpc.Deliver(Check(1, 3001, info));
            check(Last(recipient, 7).Hash == "", "RPC allowed another peer to publish owner's character");
            check(owner.m_playerID == 0 && PeerCharacter.GetId(owner) == 3001,
                "Stable ID must come from the owned player ZDO");
            var character = ZDOMan.instance.Objects[owner.m_characterID];
            character.Owner = recipient.m_uid;
            check(PeerCharacter.GetId(owner) == 0, "Another peer's ZDO accepted as the owner's character");
            character.Owner = owner.m_uid;
            ZDOMan.instance.Objects.Remove(owner.m_characterID);
            owner.m_rpc.Deliver(Check(2, 3001, info));
            SharingRpc.Tick(storage);
            check(!owner.m_rpc.Sent.Select(Read).Any(m => m.Request == 2),
                "Version check rejected instead of waiting for the first ZDO update");
            ZDOMan.instance.Objects[owner.m_characterID] = character;
            await Drain(storage, () => owner.m_rpc.Sent.Select(Read).Any(m => m.Request == 2));
            check(Last(owner, 7).Hash == "", "Unuploaded blob advertised");
            owner.m_rpc.Deliver(Packet(2, 3, 3001, info.Version, info.Hash, SharingWire.AvatarKind));
            var ticket = Last(owner, 7).Ticket;
            check(ticket.Length == 32, "RPC did not issue upload ticket");
            var client = new AvatarTcpClient("127.0.0.1", storage.Port, 750000);
            await Task.Run(() => client.UploadBlob(3001, ticket, encrypted, default));
            await Drain(storage, () => true);
            check(!recipient.m_rpc.Sent.Select(Read).Any(m => m.Op == 8),
                "Model advertised before its settings were stored");
            owner.m_rpc.Deliver(Packet(2,
                4,
                3001,
                info.Version,
                info.Hash,
                SharingWire.ProfileKind,
                profileVersion: info.ProfileVersion,
                profileHash: info.ProfileHash));
            var profileTicket = Last(owner, 7).Ticket;
            check(profileTicket.Length == 32, "RPC did not issue the settings upload ticket");
            await Task.Run(() => client.UploadBlob(3001, profileTicket, encryptedProfile, default));
            check(!recipient.m_rpc.Sent.Select(Read).Any(m => m.Op == 8),
                "Blob advertised before worker completion was dispatched");
            await Drain(storage, () => recipient.m_rpc.Sent.Select(Read).Any(m => m.Op == 8));
            check(Last(recipient, 8).Hash == info.Hash && Last(recipient, 8).ProfileHash == info.ProfileHash,
                "RPC did not announce committed availability");
            recipient.m_rpc.Deliver(Packet(2, 50, 3002, info.Version, info.Hash, SharingWire.AvatarKind));
            check(Last(recipient, 7).Request == 50 && Last(recipient, 7).Value == "publication-state",
                "Invalid upload state left the RPC unanswered");
            owner.m_rpc.Deliver(Packet(14, id: 3001, value: "Default"));
            check(Last(recipient, 15).Value == "Default", "Outfit selection did not use shared RPC session");
            owner.m_rpc.Deliver(Packet(16, id: 3001, version: "mesh", hash: "0", value: "Paci_3"));
            check(Last(recipient, 17).Value == "Paci_3" && Last(recipient, 17).Hash == "0",
                "Mesh change did not propagate immediately");
            var changes = recipient.m_rpc.Sent.Select(Read).Count(m => m.Op == 17);
            recipient.m_rpc.Deliver(Packet(16, id: 3001, version: "blend", hash: "50", value: "Smile"));
            owner.m_rpc.Deliver(Packet(16, id: 3001, version: "blend", hash: "NaN", value: "Smile"));
            check(recipient.m_rpc.Sent.Select(Read).Count(m => m.Op == 17) == changes,
                "Unowned or invalid shape override accepted");
            var late = Add(net, 3003);
            check(Last(late, 17).Value == "Paci_3", "Late joiner missed mesh override");
            owner.m_rpc.Deliver(Packet(14, id: 3001, value: "Default"));
            var resetViewer = Add(net, 3010);
            check(!resetViewer.m_rpc.Sent.Select(Read).Any(m => m.Op == 17),
                "Reselecting outfit retained manual overrides");
            check(Last(late, 8).Hash == info.Hash, "Late joiner missed available blob");
            recipient.m_rpc.Deliver(KeyRequest(5, 3001, info));
            var keyRequest = Last(owner, 10).Request;
            // First world entry publishes our own avatar while we await someone else's key.
            recipient.m_rpc.Deliver(Packet(1, 80, 3002, new string('a', 64), profileVersion: new string('a', 64)));
            await Drain(storage, () => recipient.m_rpc.Sent.Select(Read).Any(m => m.Request == 80));
            late.m_rpc.Deliver(Packet(5, keyRequest, value: key));
            check(!recipient.m_rpc.Sent.Select(Read).Any(m => m.Request == 5), "Wrong peer supplied owner's password");
            owner.m_rpc.Deliver(Packet(5, keyRequest, value: key));
            var grant = Last(recipient, 7);
            check(grant.Request == 5 && grant.Value == key && grant.Ticket.Length == 32 &&
                grant.ProfileTicket.Length == 32,
                "RPC key response did not reach requester with both transfer tickets");
            check(!late.m_rpc.Sent.Select(Read).Any(m => m.Value == key), "Key broadcast to unrelated peer");
            var absentBefore = recipient.m_rpc.Sent.Count(p => Read(p).Op == 9);
            owner.m_rpc.Deliver(Check(70, 3001, info));
            await Drain(storage, () => owner.m_rpc.Sent.Select(Read).Any(m => m.Request == 70));
            check(recipient.m_rpc.Sent.Count(p => Read(p).Op == 9) == absentBefore &&
                Last(recipient, 15).Value == "Default",
                "Unchanged reload withdrew availability or outfit");
            // A later local revision must also retain already-issued downloads of other avatars.
            recipient.m_rpc.Deliver(Packet(1, 81, 3002, new string('b', 64), profileVersion: new string('b', 64)));
            await Drain(storage, () => recipient.m_rpc.Sent.Select(Read).Any(m => m.Request == 81));
            var bundle = await Task.Run(() =>
                client.Receive(3001,
                    info,
                    grant.Value,
                    Path.Combine(root, "rpc-cache"),
                    default,
                    grant.Ticket,
                    grant.ProfileTicket,
                    null));
            check(bundle.Outfits.Contains("[Default]"), "Encrypted blob lost outfits");
            recipient.m_rpc.Deliver(KeyRequest(6, 3001, info));
            var staleRequest = Last(owner, 10).Request;
            net.Peers.Remove(owner);
            SharingRpc.RemovePeer(owner.m_rpc);
            check(Last(recipient, 9).Id == 3001, "Disconnect did not withdraw avatar availability");
            check(Last(recipient, 7).Request == 6 && Last(recipient, 7).Value == "",
                "Disconnect did not release pending key request");
            owner.m_rpc.Deliver(Packet(5, staleRequest, value: key));
            check(Last(recipient, 7).Value == "", "Disconnected owner response accepted");
            check(storage.ReadCurrent(3001).Hash == info.Hash, "Disconnect deleted durable encrypted blob");
            storage.BundleLimitBytes = 64;
            check(storage.ReadCurrent(3001) == null, "Stored bundle exceeding changed server limit remained available");
            storage.BundleLimitBytes = SharingWire.DefaultBundleLimitBytes;
            var reconnect = Add(net, 3001);
            reconnect.m_rpc.Deliver(Check(7, 3001, info));
            await Drain(storage, () => reconnect.m_rpc.Sent.Select(Read).Any(m => m.Request == 7));
            check(Last(reconnect, 7).Hash == info.Hash && Last(reconnect, 7).ProfileHash == info.ProfileHash,
                "Reconnect reuploaded unchanged blob");
            // A settings-only change reports the stored model so only the settings blob is re-sent.
            var changed = info.Clone();
            changed.ProfileVersion = new string('d', 64);
            reconnect.m_rpc.Deliver(Check(71, 3001, changed));
            await Drain(storage, () => reconnect.m_rpc.Sent.Select(Read).Any(m => m.Request == 71));
            check(Last(reconnect, 7).Hash == info.Hash && Last(reconnect, 7).ProfileVersion == info.ProfileVersion,
                "Settings change did not report the stored model");
            check(Last(recipient, 9).Id == 3001, "Settings change did not withdraw the old availability");
            reconnect.m_rpc.Deliver(Check(72, 3001, info));
            await Drain(storage, () => reconnect.m_rpc.Sent.Select(Read).Any(m => m.Request == 72));
            recipient.m_rpc.Deliver(KeyRequest(84, 3001, info));
            var cancelledRequest = Last(reconnect, 10).Request;
            recipient.m_rpc.Deliver(Packet(18, 84, 3001));
            reconnect.m_rpc.Deliver(Packet(5, cancelledRequest, value: key));
            check(!recipient.m_rpc.Sent.Select(Read).Any(m => m.Op == 7 && m.Request == 84),
                "Cancelled key request accepted a stale owner reply");
            recipient.m_rpc.Deliver(KeyRequest(82, 3001, info));
            reconnect.m_rpc.Deliver(Packet(1, 83, 3001, new string('c', 64), profileVersion: new string('c', 64)));
            await Drain(storage, () => reconnect.m_rpc.Sent.Select(Read).Any(m => m.Request == 83));
            check(recipient.m_rpc.Sent.Select(Read)
                    .Any(m => m.Op == 7 && m.Request == 82 && m.Value == "" && m.Hash == ""),
                "Changing the owner's publication silently discarded a pending key request");
            check(net.Peers.SelectMany(p => p.m_rpc.Sent).All(p => p.Size() < 1024), "Avatar bytes entered RPC");
            var leaving = Add(net, 3004);
            var leavingCharacter = ZDOMan.instance.Objects[leaving.m_characterID];
            ZDOMan.instance.Objects.Remove(leaving.m_characterID);
            leaving.m_rpc.Deliver(Check(60, 3004, info));
            net.Peers.Remove(leaving);
            SharingRpc.RemovePeer(leaving.m_rpc);
            ZDOMan.instance.Objects[leaving.m_characterID] = leavingCharacter;
            SharingRpc.Tick(storage);
            check(!leaving.m_rpc.Sent.Select(Read).Any(m => m.Request == 60),
                "Disconnected avatar retained its deferred version request");
            SharingRpc.Reset(null);
            ZNet.instance = null;
            ZDOMan.instance.Objects.Clear();
            stop.Cancel();
            try
            {
                await serving;
            }
            catch (OperationCanceledException) { }
        }
    }

    internal static async Task RetryChecks(Action<bool, string> check)
    {
        var delays = Enumerable.Range(1, 20).Select(SharingRpc.KeyRetryDelaySeconds).ToArray();
        check(delays.Take(10).All(n => n == 2) && delays.Skip(10).Take(5).All(n => n == 5) &&
            delays.Skip(15).All(n => n == 10),
            "Key retry cadence differs from 10x2s, 5x5s, 5x10s");
        check(delays.Sum() == 95 && SharingRpc.KeyRetryDelaySeconds(21) == 0,
            "Key retry schedule does not terminate after 20 retries");
        var net = new ZNet { Server = false };
        ZNet.instance = net;
        var server = new ZNetPeer { m_uid = 1 };
        net.Peers.Add(server);
        SharingRpc.Reset(net);
        SharingRpc.RegisterPeer(net, server);
        server.m_rpc.Deliver(Packet(13, SharingWire.DefaultBundleLimitBytes, version: "1", value: "6067"));
        using (var stop = new CancellationTokenSource())
        {
            var receive = SharingRpc.ReceiveAsync(new AvatarTcpClient("unused.invalid", 1, 750000),
                3001,
                new BundleInfo
                {
                    Version = new string('a', 64),
                    Hash = new string('b', 64),
                    ProfileVersion = new string('a', 64),
                    ProfileHash = new string('b', 64)
                },
                "unused",
                stop.Token,
                SharingRpc.Epoch);
            await Drain(null, () => server.m_rpc.Sent.Select(Read).Any(m => m.Op == 4));
            var first = Last(server, 4).Request;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // No first response: it must time out at 2s, cancel, and send a new request.
            await Drain(null, () => server.m_rpc.Sent.Select(Read).Count(m => m.Op == 4) >= 2);
            var second = Last(server, 4).Request;
            check(second != first && clock.Elapsed.TotalSeconds >= 1.8 && clock.Elapsed.TotalSeconds < 3,
                "First key retry retained the old 30-second timeout or retried too soon");
            check(server.m_rpc.Sent.Select(Read).Any(m => m.Op == 18 && m.Request == first),
                "Timed-out key request was not cancelled on the server");
            // An expired response must not complete the new request or start TCP.
            server.m_rpc.Deliver(Packet(7,
                first,
                3001,
                new string('a', 64),
                new string('b', 64),
                BundleCrypto.GenerateKey(),
                "stale-ticket",
                new string('a', 64),
                new string('b', 64),
                "stale-ticket"));
            SharingRpc.Tick(null);
            check(!receive.IsCompleted, "A stale grant completed the new key request");
            stop.Cancel();
            await Drain(null, () => receive.IsCompleted);
            var cancelled = false;
            try
            {
                await receive;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            check(cancelled && server.m_rpc.Sent.Select(Read).Any(m => m.Op == 18 && m.Request == second),
                "Disconnect cancellation left a live key attempt");
        }

        SharingRpc.Reset(null);
        ZNet.instance = null;
    }
}

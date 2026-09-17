using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM;
using EnhancedValheimVRM.Sharing;

internal static class FaceChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        const long owner = 1001, viewer = 1002;
        var key = BundleCrypto.GenerateKey();
        var values = new byte[FaceWire.ValueCount];
        for (var i = 0; i < values.Length; i++) values[i] = (byte)(i * 4);

        // packets
        using (var sender = new FaceCipher(key))
        using (var receiver = new FaceCipher(key))
        using (var stranger = new FaceCipher(BundleCrypto.GenerateKey()))
        {
            var packet = sender.Seal(owner, 30, 7, 3, values);
            var opened = new byte[FaceWire.ValueCount];
            check(packet.Length == FaceWire.PacketBytes && receiver.Open(packet, packet.Length, opened) &&
                Same(values, opened),
                "Face packet round trip");
            check(FaceWire.TryPeek(packet, packet.Length, out var peekId, out var keepalive) && peekId == owner &&
                !keepalive && FaceWire.ReadNonce(packet) == 7 && FaceWire.ReadSequence(packet) == 3 &&
                FaceWire.ReadRate(packet) == 30,
                "Relay can read the plain header");
            check(!stranger.Open(packet, packet.Length, opened), "Wrong key rejected");
            var tampered = (byte[])packet.Clone();
            tampered[FaceWire.HeaderBytes + 5] ^= 1;
            check(!receiver.Open(tampered, tampered.Length, opened), "Tampered value rejected");
            var relabelled = (byte[])packet.Clone();
            Array.Copy(BitConverter.GetBytes(viewer), 0, relabelled, 0, 8);
            check(!receiver.Open(relabelled, relabelled.Length, opened), "Relabelled sender rejected");
            var second = sender.Seal(owner, 30, 7, 4, values);
            var differ = false;
            for (var i = FaceWire.HeaderBytes; i < FaceWire.HeaderBytes + FaceWire.CipherBytes; i++)
                differ |= second[i] != packet[i];
            check(differ, "Same values in the next packet encrypt differently");
            var keep = FaceWire.BuildKeepalive(owner);
            check(FaceWire.TryPeek(keep, keep.Length, out peekId, out keepalive) && keepalive && peekId == owner,
                "Keepalive recognised");
            check(FaceWire.Quantize(0f) == 0 && FaceWire.Quantize(1f) == 255 && FaceWire.Quantize(2f) == 255 &&
                FaceWire.Quantize(0.5f) == 128 && FaceWire.IndexOf("jawOpen") == 17 && FaceWire.IndexOf("nope") == -1,
                "Quantize and catalogue lookup");
        }

        // replay guard
        var guard = new FaceReplayGuard();
        check(guard.Accept(5, 0) && guard.Accept(5, 1) && !guard.Accept(5, 1) && !guard.Accept(5, 0) &&
            guard.Accept(5, 10) && guard.Accept(5, 9) && !guard.Accept(5, 9) && guard.Accept(5, 200) &&
            !guard.Accept(5, 100) && guard.Accept(5, 150),
            "Replay guard: duplicates and packets older than the reorder window are rejected, reordered ones inside it pass");
        var wrapping = new FaceReplayGuard();
        check(wrapping.Accept(5, uint.MaxValue - 1) && wrapping.Accept(5, 1) && wrapping.Accept(5, uint.MaxValue) &&
            !wrapping.Accept(5, uint.MaxValue) && !wrapping.Accept(5, uint.MaxValue - 1),
            "Sequence wrap counts as newer, and a reordered packet across the wrap still counts once");
        check(guard.Accept(6, 0) && !guard.Accept(5, 100) && guard.Accept(6, 1),
            "A new session nonce starts over, the old nonce is dead");

        // relay
        using (var stop = new CancellationTokenSource())
        using (var relay = new FaceRelay(IPAddress.Loopback, 0))
        using (var a = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var b = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var c = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            var serving = relay.RunAsync(stop.Token);
            var server = new IPEndPoint(IPAddress.Loopback, relay.Port);
            try
            {
                var ticketA = relay.AuthorizeStream("sa", owner);
                var ticketB = relay.AuthorizeStream("sb", viewer);
                var hello = FaceWire.BuildHello(owner, ticketA);
                await a.SendAsync(hello, hello.Length, server);
                var ack = await Receive(a, 2000);
                check(ack != null && FaceWire.IsAck(ack, ack.Length), "Hello with a good ticket is acked");
                await a.SendAsync(hello, hello.Length, server);
                check(await Receive(a, 300) == null, "A used ticket is ignored");
                hello = FaceWire.BuildHello(viewer, ticketB);
                await b.SendAsync(hello, hello.Length, server);
                check(await Receive(b, 2000) != null, "Second client registered");
                var badHello = FaceWire.BuildHello(viewer, relay.AuthorizeStream("sc", owner));
                await c.SendAsync(badHello, badHello.Length, server);
                check(await Receive(c, 300) == null, "Ticket for another character is refused");
                check(relay.Streams == 2, "Two streams registered");

                using (var cipher = new FaceCipher(key))
                {
                    var packet = cipher.Seal(owner, 60, 1, 0, values);
                    await a.SendAsync(packet, packet.Length, server);
                    var got = await Receive(b, 2000);
                    check(got != null && Same(got, packet), "Face packet relayed to the other client");
                    check(await Receive(a, 300) == null, "Never relayed back to the sender");
                    var relabelled = cipher.Seal(viewer, 60, 1, 1, values);
                    await a.SendAsync(relabelled, relabelled.Length, server);
                    check(await Receive(b, 300) == null, "Packet claiming another character is dropped");
                    await c.SendAsync(packet, packet.Length, server);
                    check(await Receive(b, 300) == null && await Receive(a, 300) == null,
                        "Unregistered endpoint is dropped");
                    var keep = FaceWire.BuildKeepalive(owner);
                    await a.SendAsync(keep, keep.Length, server);
                    check(await Receive(b, 300) == null, "Keepalive is not relayed");
                    var before = relay.Dropped;
                    for (uint i = 2; i < 2 + FaceRelay.MaxPacketsPerSecond + 20; i++)
                    {
                        var burst = cipher.Seal(owner, 60, 1, i, values);
                        await a.SendAsync(burst, burst.Length, server);
                    }

                    var received = 0;
                    while (await Receive(b, 300) != null) received++;
                    check(received <= FaceRelay.MaxPacketsPerSecond && relay.Dropped > before,
                        "Per sender rate cap drops the extras");
                    relay.RevokeCharacter(owner);
                    check(relay.Streams == 1, "Revoke forgets the stream");
                    var after = cipher.Seal(owner, 60, 1, 500, values);
                    await a.SendAsync(after, after.Length, server);
                    check(await Receive(b, 300) == null, "Revoked sender is dropped");
                }
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

    private static async Task<byte[]> Receive(UdpClient client, int milliseconds)
    {
        var receive = client.ReceiveAsync();
        if (await Task.WhenAny(receive, Task.Delay(milliseconds)) != receive) return null;
        return receive.Result.Buffer;
    }

    private static bool Same(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    // the rpc side of the handshake: once a subscribed peer's character is verified the server sends a
    // ticket with its port announcement, a hello with it is acked by the relay, and a peer can still
    // ask for another one
    internal static async Task TicketRpcChecks(string root, Action<bool, string> check)
    {
        using (var stop = new CancellationTokenSource())
        using (var storage = new AvatarTcpServer(System.IO.Path.Combine(root, "face-rpc"),
                   IPAddress.Loopback,
                   0,
                   new SharingDownloadPolicy(25, 4)))
        using (var relay = new FaceRelay(IPAddress.Loopback, 0))
        {
            var serving = Task.WhenAll(relay.RunAsync(stop.Token), storage.RunAsync(stop.Token));
            try
            {
                var net = new ZNet();
                ZNet.instance = net;
                SharingRpc.Reset(net);
                SharingRpc.Tick(storage);
                SharingRpc.RelayStarted(relay);
                SharingRpc.Tick(storage);

                RpcChecks.Message LastPort(ZNetPeer p)
                {
                    return p.m_rpc.Sent.Select(RpcChecks.Read).LastOrDefault(m => m.Op == 13);
                }

                RpcChecks.Message Reply(ZNetPeer p, long request)
                {
                    return p.m_rpc.Sent.Select(RpcChecks.Read).LastOrDefault(m => m.Op == 7 && m.Request == request);
                }

                var peer = new ZNetPeer
                {
                    m_playerID = 0, m_uid = 4101 + 10000, m_characterID = new ZDOID(4101 + 90000)
                };
                ZDOMan.instance.Objects[peer.m_characterID] = new ZDO { Owner = peer.m_uid, CharacterId = 4101 };
                net.Peers.Add(peer);
                SharingRpc.RegisterPeer(net, peer);
                peer.m_rpc.Deliver(RpcChecks.Packet(0));
                peer.m_rpc.Deliver(RpcChecks.Packet(12, version: SharingRpc.Protocol.ToString()));
                SharingRpc.Tick(storage);
                var port = LastPort(peer);
                check(port != null && port.FacePort == relay.Port.ToString(),
                    "Server did not announce the face port with the relay running");
                check(port != null && port.Ticket.Length == 32,
                    "Port announcement to a known character carried no face ticket");
                if (port != null && port.Ticket.Length == 32)
                {
                    using (var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                    {
                        var hello = FaceWire.BuildHello(4101, port.Ticket);
                        await client.SendAsync(hello, hello.Length, new IPEndPoint(IPAddress.Loopback, relay.Port));
                        var ack = await Receive(client, 2000);
                        check(ack != null && FaceWire.IsAck(ack, ack.Length),
                            "Hello with the announced ticket was not acked");
                    }
                }

                var sentBefore = peer.m_rpc.Sent.Count;
                SharingRpc.Tick(storage);
                check(peer.m_rpc.Sent.Count == sentBefore,
                    "Server kept re-announcing the port after the ticket was sent");
                peer.m_rpc.Deliver(RpcChecks.Packet(19, 77, 4101));
                var asked = Reply(peer, 77);
                check(asked != null && asked.Ticket.Length == 32 && asked.Ticket != port.Ticket,
                    "A later face ticket request was not answered with a fresh ticket");

                // character verified after the first announcement: a second announcement brings the ticket
                var late = new ZNetPeer { m_playerID = 0, m_uid = 4102 + 10000, m_characterID = ZDOID.None };
                net.Peers.Add(late);
                SharingRpc.RegisterPeer(net, late);
                late.m_rpc.Deliver(RpcChecks.Packet(0));
                late.m_rpc.Deliver(RpcChecks.Packet(12, version: SharingRpc.Protocol.ToString()));
                SharingRpc.Tick(storage);
                var early = LastPort(late);
                check(early != null && early.Ticket == "",
                    "Port announcement before the character was known carried a ticket");
                late.m_rpc.Deliver(RpcChecks.Packet(19, 78, 4102));
                var refused = Reply(late, 78);
                check(refused != null && refused.Ticket == "",
                    "A face ticket was issued before the character was known");
                late.m_characterID = new ZDOID(4102 + 90000);
                ZDOMan.instance.Objects[late.m_characterID] = new ZDO { Owner = late.m_uid, CharacterId = 4102 };
                SharingRpc.Tick(storage);
                var announced = LastPort(late);
                check(announced != null && announced.Ticket.Length == 32,
                    "No ticket was announced once the character arrived");
                SharingRpc.Reset(null);
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

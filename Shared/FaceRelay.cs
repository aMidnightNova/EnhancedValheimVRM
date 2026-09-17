using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EnhancedValheimVRM.Sharing
{
    // Relays face packets to UDP endpoints. Clients send keepalives while idle
    // and reregister automatically after a 60 second timeout. The relay forwards
    // packets immediately and stores neither face data nor avatar keys.
    public sealed class FaceRelay : IDisposable
    {
        private sealed class Stream
        {
            internal string Session;
            internal long Id;
            internal IPEndPoint Endpoint;
            internal DateTime LastSeen, RateWindow;
            internal int RateCount;
        }

        private sealed class Ticket
        {
            internal string Session;
            internal long Id;
            internal DateTime Expires;
        }

        public const int MaxPacketsPerSecond = 60;
        public static readonly TimeSpan Silence = TimeSpan.FromSeconds(60);

        private readonly UdpClient _socket;
        private readonly ConcurrentDictionary<string, Ticket> _tickets = new ConcurrentDictionary<string, Ticket>();
        private readonly Dictionary<IPEndPoint, Stream> _byEndpoint = new Dictionary<IPEndPoint, Stream>();
        private readonly Dictionary<long, Stream> _byCharacter = new Dictionary<long, Stream>();
        private readonly object _lock = new object();
        private long _relayed, _dropped;

        public long Relayed => Interlocked.Read(ref _relayed);
        public long Dropped => Interlocked.Read(ref _dropped);
        public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint).Port;

        public FaceRelay(IPAddress address, int port)
        {
            _socket = new UdpClient(new IPEndPoint(address, port));
        }

        public int Streams
        {
            get
            {
                lock (_lock) return _byCharacter.Count;
            }
        }

        // the host hooks this to its log, the relay itself has none
        public Action<string> Notice;

        public string AuthorizeStream(string session, long id)
        {
            SharingWire.ValidateId(id);
            foreach (var pair in _tickets)
            {
                if (pair.Value.Expires < DateTime.UtcNow) _tickets.TryRemove(pair.Key, out _);
            }

            if (_tickets.Count >= 256) throw new System.IO.IOException("Too many pending face streams.");
            var ticket = Guid.NewGuid().ToString("N");
            _tickets[ticket] = new Ticket { Session = session, Id = id, Expires = DateTime.UtcNow.AddMinutes(2) };
            return ticket;
        }

        public void RevokeSession(string session)
        {
            Forget(stream => stream.Session == session, ticket => ticket.Session == session);
        }

        public void RevokeCharacter(long id)
        {
            if (id != 0) Forget(stream => stream.Id == id, ticket => ticket.Id == id);
        }

        private void Forget(Func<Stream, bool> streams, Func<Ticket, bool> tickets)
        {
            foreach (var pair in _tickets)
            {
                if (tickets(pair.Value)) _tickets.TryRemove(pair.Key, out _);
            }

            lock (_lock)
            {
                var gone = new List<Stream>();
                foreach (var stream in _byCharacter.Values)
                {
                    if (streams(stream)) gone.Add(stream);
                }

                foreach (var stream in gone) Remove(stream);
            }
        }

        private void Remove(Stream stream)
        {
            _byCharacter.Remove(stream.Id);
            _byEndpoint.Remove(stream.Endpoint);
        }

        public async Task RunAsync(CancellationToken cancellation)
        {
            using (cancellation.Register(() => _socket.Close()))
            {
                while (!cancellation.IsCancellationRequested)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await _socket.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        // a peer that vanished can surface as a reset on the next receive, keep serving
                        if (cancellation.IsCancellationRequested) break;
                        continue;
                    }

                    Handle(received.Buffer, received.RemoteEndPoint);
                }
            }
        }

        private void Handle(byte[] packet, IPEndPoint from)
        {
            if (FaceWire.TryParseHello(packet, packet.Length, out var helloId, out var ticket))
            {
                if (!_tickets.TryRemove(ticket, out var pending) || pending.Expires < DateTime.UtcNow ||
                    pending.Id != helloId)
                {
                    Interlocked.Increment(ref _dropped);
                    Notice?.Invoke("Face stream hello from " + from + " for avatar " + helloId + " refused, no matching ticket.");
                    return;
                }

                lock (_lock)
                {
                    if (_byCharacter.TryGetValue(helloId, out var previous)) Remove(previous);
                    if (_byEndpoint.TryGetValue(from, out var squatter)) Remove(squatter);
                    var stream = new Stream
                    {
                        Session = pending.Session, Id = helloId, Endpoint = from, LastSeen = DateTime.UtcNow
                    };
                    _byCharacter[helloId] = stream;
                    _byEndpoint[from] = stream;
                }

                Send(FaceWire.Ack, FaceWire.Ack.Length, from);
                Notice?.Invoke("Face stream for avatar " + helloId + " registered from " + from + ".");
                return;
            }

            if (!FaceWire.TryPeek(packet, packet.Length, out var id, out var keepalive))
            {
                Interlocked.Increment(ref _dropped);
                return;
            }

            List<IPEndPoint> targets = null;
            lock (_lock)
            {
                if (!_byEndpoint.TryGetValue(from, out var sender) || sender.Id != id)
                {
                    Interlocked.Increment(ref _dropped);
                    return;
                }

                var now = DateTime.UtcNow;
                sender.LastSeen = now;
                Expire(now);
                if (keepalive)
                {
                    Send(FaceWire.Ack, FaceWire.Ack.Length, from);
                    return;
                }

                if (now - sender.RateWindow >= TimeSpan.FromSeconds(1))
                {
                    sender.RateWindow = now;
                    sender.RateCount = 0;
                }

                if (++sender.RateCount > MaxPacketsPerSecond)
                {
                    Interlocked.Increment(ref _dropped);
                    return;
                }

                foreach (var stream in _byCharacter.Values)
                {
                    if (stream == sender) continue;
                    if (targets == null) targets = new List<IPEndPoint>();
                    targets.Add(stream.Endpoint);
                }
            }

            if (targets == null) return;
            foreach (var target in targets) Send(packet, packet.Length, target);
            Interlocked.Increment(ref _relayed);
        }

        private void Expire(DateTime now)
        {
            List<Stream> gone = null;
            foreach (var stream in _byCharacter.Values)
            {
                if (now - stream.LastSeen > Silence)
                {
                    if (gone == null) gone = new List<Stream>();
                    gone.Add(stream);
                }
            }

            if (gone == null) return;
            foreach (var stream in gone) Remove(stream);
        }

        private void Send(byte[] packet, int length, IPEndPoint to)
        {
            try
            {
                _socket.Send(packet, length, to);
            }
            catch (SocketException)
            {
                // an unreachable viewer is not the senders problem
            }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _socket.Close();
        }
    }
}

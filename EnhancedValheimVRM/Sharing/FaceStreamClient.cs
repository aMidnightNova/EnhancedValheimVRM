using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // one udp socket to the servers face relay. registers with a ticket, sends our own face at
    // the configured rate, receives everyone elses and keeps the latest frame per character for
    // the FaceApplier on that avatar.
    public sealed class FaceStreamClient : MonoBehaviour
    {
        internal sealed class Frame
        {
            internal readonly byte[] Values = new byte[FaceWire.ValueCount];
            internal double Arrived, Released;
            internal byte Rate;
            internal uint Sequence;
        }

        // one senders packets, held back by the jitter buffer and handed out in sequence order.
        // a packet that turns up after a newer one was already shown is dropped
        internal sealed class Stream
        {
            internal readonly SortedDictionary<uint, Frame> Waiting =
                new SortedDictionary<uint, Frame>(SequenceOrder.Instance);

            internal Frame Current;
            internal uint Nonce, LastReleased;
            internal bool Started;
        }

        private sealed class SequenceOrder : IComparer<uint>
        {
            internal static readonly SequenceOrder Instance = new SequenceOrder();

            public int Compare(uint a, uint b)
            {
                return (int)(a - b);
            }
        }

        private static FaceStreamClient _instance;
        private static readonly ConcurrentDictionary<long, Stream> Streams = new ConcurrentDictionary<long, Stream>();

        private UdpClient _socket;
        private IPEndPoint _server;
        private CancellationTokenSource _stop;
        private string _host, _key;
        private int _port, _facePort;
        private long _id;
        private bool _wantSend, _wantReceive;
        private volatile bool _registered;
        private double _lastHello, _lastAck, _lastSent, _lastKeepalive, _lastReceived;
        private float _helloBackoff = 5;
        private float _nextCheck;
        private uint _nonce, _sequence;
        private FaceCipher _own;
        private readonly float[] _local = new float[FaceWire.ValueCount];
        private readonly byte[] _quantized = new byte[FaceWire.ValueCount];
        private readonly Dictionary<long, FaceCipher> _ciphers = new Dictionary<long, FaceCipher>();
        private readonly Dictionary<long, string> _cipherKeys = new Dictionary<long, string>();
        private readonly Dictionary<long, FaceReplayGuard> _guards = new Dictionary<long, FaceReplayGuard>();
        private readonly byte[] _opened = new byte[FaceWire.ValueCount];
        private Task _ticketRequest;

        // the frame a character should show now: packets wait in the jitter buffer for their turn,
        // then come out in order. false when nothing has arrived for two seconds
        internal static bool TryGetFrame(long id, byte[] values, out double received, out byte rate)
        {
            received = 0;
            rate = 0;
            if (!Streams.TryGetValue(id, out var stream)) return false;
            var now = Time.realtimeSinceStartupAsDouble;
            var hold = Settings.FaceJitterMs / 1000.0;
            lock (stream)
            {
                while (stream.Waiting.Count > 0)
                {
                    var next = stream.Waiting.First();
                    if (next.Value.Arrived + hold > now) break;
                    stream.Waiting.Remove(next.Key);
                    next.Value.Released = now;
                    stream.Current = next.Value;
                    stream.LastReleased = next.Key;
                    stream.Started = true;
                }

                var frame = stream.Current;
                if (frame == null || now - frame.Arrived > 2) return false;
                Array.Copy(frame.Values, values, values.Length);
                received = frame.Released;
                rate = frame.Rate;
            }

            return true;
        }

        internal static bool Registered => _instance != null && _instance._registered;

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (Time.realtimeSinceStartup >= _nextCheck)
            {
                _nextCheck = Time.realtimeSinceStartup + 1;
                Reconcile();
            }

            if (_socket == null) return;
            if (!_registered)
            {
                if (now - _lastHello >= _helloBackoff) Hello();
                return;
            }

            if (now - _lastAck > 60 && now - _lastReceived > 60)
            {
                // the mapping is gone or the server restarted, start over with a new ticket
                _registered = false;
                _helloBackoff = 5;
                return;
            }

            if (_wantSend && VmcReceiver.Live && now - _lastSent >= 1.0 / Settings.FaceSendRate)
            {
                _lastSent = now;
                SendFace();
            }

            // face packets are never acked, so a sender keeps alive too or it would think the
            // relay went away after a minute of streaming
            if (now - _lastKeepalive >= 15)
            {
                _lastKeepalive = now;
                Send(FaceWire.BuildKeepalive(_id));
            }
        }

        // opens or closes the session so it matches the settings and the server we are on
        private void Reconcile()
        {
            var network = ZNet.instance;
            var wantSend = Settings.FaceEnabled;
            var wantReceive = Settings.ReceiveFaceStreams;
            Player.m_localPlayer.TryGetPlayerId(out var id);
            string host = null;
            int port = 0, facePort = 0;
            var usable = network != null && !network.IsServer() && !network.IsDedicated() &&
                Settings.EnableVrmSharing &&
                (wantSend || wantReceive) && id != 0 && SharingRpc.TryGetServerPort(network, out port) &&
                SharingRpc.FacePort > 0;
            if (usable)
            {
                facePort = SharingRpc.FacePort;
                host = FileTransferController.ServerAddress(network, port);
                usable = !string.IsNullOrEmpty(host);
            }

            if (!usable)
            {
                if (network != null && !network.IsServer() && (wantSend || wantReceive) && id != 0 &&
                    SharingRpc.TryGetServerPort(network, out _) && SharingRpc.FacePort == 0)
                    Logger.LogOnce("face-relay-missing",
                        "The server has no face relay running, faces are off. Its log says why.");
                Stop();
                return;
            }

            if (_socket != null && _host == host && _port == port && _facePort == facePort && _id == id &&
                _key == Settings.VrmKey && _wantSend == wantSend && _wantReceive == wantReceive)
                return;
            Stop();
            _host = host;
            _port = port;
            _facePort = facePort;
            _id = id;
            _key = Settings.VrmKey;
            _wantSend = wantSend;
            _wantReceive = wantReceive;
            _nonce = (uint)new System.Random().Next() ^ (uint)Environment.TickCount;
            _sequence = 0;
            try
            {
                _own = new FaceCipher(_key);
                _socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                _stop = new CancellationTokenSource();
                var socket = _socket;
                var token = _stop.Token;
                Task.Run(() => Receive(socket, token));
                Logger.Log("Face stream session to " + host + " udp " + facePort +
                    (wantSend ? ", sending at " : ", receive only") +
                    (wantSend ? Settings.FaceSendRate + " fps" : ""),
                    Logger.LogLevel.Debug);
            }
            catch (Exception error) when (error is SocketException ||
                                          error is System.Security.Cryptography.CryptographicException)
            {
                Logger.LogWarning("Face stream could not start: " + error.Message);
                Stop();
                _nextCheck = Time.realtimeSinceStartup + 15;
            }
        }

        private void Hello()
        {
            _lastHello = Time.realtimeSinceStartupAsDouble;
            _helloBackoff = Math.Min(30, _helloBackoff * 1.5f);
            if (_ticketRequest != null && !_ticketRequest.IsCompleted) return;
            var host = _host;
            var facePort = _facePort;
            var id = _id;
            var socket = _socket;
            var token = _stop.Token;
            _ticketRequest = Task.Run(async () =>
            {
                try
                {
                    var ticket = SharingRpc.TakePushedFaceTicket();
                    if (ticket == null) ticket = await SharingRpc.RequestFaceTicketAsync(token).ConfigureAwait(false);

                    if (token.IsCancellationRequested) return;
                    if (ticket == null)
                    {
                        Logger.LogOnce("face-no-ticket",
                            "The server gave no face ticket, it has no relay running or does not know this character yet.",
                            Logger.LogLevel.Debug);
                        return;
                    }

                    if (!IPAddress.TryParse(host, out var address))
                    {
                        foreach (var candidate in await Dns.GetHostAddressesAsync(host).ConfigureAwait(false))
                        {
                            if (candidate.AddressFamily != AddressFamily.InterNetwork) continue;
                            address = candidate;
                            break;
                        }
                    }

                    if (address == null || token.IsCancellationRequested) return;
                    var server = new IPEndPoint(address, facePort);
                    var hello = FaceWire.BuildHello(id, ticket);
                    _server = server;
                    socket.Send(hello, hello.Length, server);
                    Logger.LogOnce("face-hello",
                        "Face hello sent to " + server + ", waiting for the relay to answer.",
                        Logger.LogLevel.Debug);
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    // a failed attempt is retried on the backoff, but never silently
                    Logger.LogOnce("face-hello-failed:" + error.GetType().Name,
                        "Face hello did not go out: " + error.Message + " Retrying.",
                        Logger.LogLevel.Debug);
                }
            });
        }

        private void SendFace()
        {
            if (!VmcReceiver.TryCopy(_local, out _, out _)) return;
            for (var i = 0; i < _quantized.Length; i++) _quantized[i] = FaceWire.Quantize(_local[i]);
            try
            {
                Send(_own.Seal(_id, (byte)Settings.FaceSendRate, _nonce, _sequence++, _quantized));
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is System.Security.Cryptography.CryptographicException)
            {
                Logger.LogOnce("face-seal", "Face packet could not be built: " + error.Message, Logger.LogLevel.Debug);
            }
        }

        private void Send(byte[] packet)
        {
            var server = _server;
            var socket = _socket;
            if (server == null || socket == null) return;
            try
            {
                socket.Send(packet, packet.Length, server);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task Receive(UdpClient socket, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (token.IsCancellationRequested) return;
                    continue;
                }

                var packet = received.Buffer;
                try
                {
                    Handle(packet);
                }
                catch (Exception error) when (!(error is ObjectDisposedException))
                {
                    Logger.LogOnce("face-receive-failed:" + error.GetType().Name,
                        "A face packet could not be handled: " + error.Message,
                        Logger.LogLevel.Debug);
                }
            }
        }

        private void Handle(byte[] packet)
        {
            if (FaceWire.IsAck(packet, packet.Length))
            {
                if (!_registered) Logger.Log("Face relay accepted the session.", Logger.LogLevel.Debug);
                _lastAck = _lastReceived = Time.realtimeSinceStartupAsDouble;
                _registered = true;
                _helloBackoff = 5;
                return;
            }

            if (!FaceWire.TryPeek(packet, packet.Length, out var id, out var keepalive) || keepalive) return;
            _lastReceived = Time.realtimeSinceStartupAsDouble;
            if (!_wantReceive || id == _id) return;
            if (!SharingRpc.TryGetOwnerKey(id, out var key))
            {
                Logger.LogOnce("face-not-granted-" + id,
                    "Face packets arrive for avatar " + id + " before this client was granted it, asking the server.",
                    Logger.LogLevel.Debug);
                AskForKey(id);
                return;
            }

            var cipher = Cipher(id, key);
            if (cipher == null || !cipher.Open(packet, packet.Length, _opened))
            {
                Logger.LogOnce("face-bad-packet-" + id,
                    "Face packets for avatar " + id +
                    " are not readable here, the sender is on a different build or republished the avatar.",
                    Logger.LogLevel.Debug);
                return;
            }

            Logger.LogOnce("face-first-" + id, "Receiving face frames for avatar " + id + ".", Logger.LogLevel.Debug);
            if (!_guards.TryGetValue(id, out var guard)) _guards[id] = guard = new FaceReplayGuard();
            var nonce = FaceWire.ReadNonce(packet);
            var sequence = FaceWire.ReadSequence(packet);
            if (!guard.Accept(nonce, sequence)) return;
            var stream = Streams.GetOrAdd(id, _ => new Stream());
            lock (stream)
            {
                if (stream.Nonce != nonce)
                {
                    // the sender started over, so does the order
                    stream.Nonce = nonce;
                    stream.Waiting.Clear();
                    stream.Started = false;
                }

                // outside its window: a newer packet was already shown
                if (stream.Started && (int)(sequence - stream.LastReleased) <= 0) return;
                if (stream.Waiting.ContainsKey(sequence)) return;
                var frame = new Frame
                {
                    Arrived = _lastReceived, Rate = FaceWire.ReadRate(packet), Sequence = sequence
                };
                Array.Copy(_opened, frame.Values, frame.Values.Length);
                stream.Waiting[sequence] = frame;
            }
        }

        private readonly Dictionary<long, DateTime> _keyAsked = new Dictionary<long, DateTime>();

        private void AskForKey(long id)
        {
            var now = DateTime.UtcNow;
            if (_keyAsked.TryGetValue(id, out var last) && now - last < TimeSpan.FromSeconds(30)) return;
            _keyAsked[id] = now;
            var token = _stop?.Token ?? CancellationToken.None;
            Task.Run(async () =>
            {
                try
                {
                    await SharingRpc.EnsureOwnerKeyAsync(id, token).ConfigureAwait(false);
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    Logger.LogOnce("face-key-ask-failed:" + id,
                        "Could not get avatar " + id + " for its face stream: " + error.Message,
                        Logger.LogLevel.Debug);
                }
            });
        }

        // one cipher per owner, rebuilt when their key changes. only the receive task touches these
        private FaceCipher Cipher(long id, string key)
        {
            if (_ciphers.TryGetValue(id, out var cipher) && _cipherKeys[id] == key) return cipher;
            cipher?.Dispose();
            try
            {
                cipher = new FaceCipher(key);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }

            _ciphers[id] = cipher;
            _cipherKeys[id] = key;
            return cipher;
        }

        private void Stop()
        {
            _stop?.Cancel();
            _socket?.Close();
            _socket = null;
            _stop = null;
            _server = null;
            _registered = false;
            _helloBackoff = 5;
            _lastHello = _lastAck = _lastReceived = _lastSent = _lastKeepalive = 0;
            _own?.Dispose();
            _own = null;
            foreach (var cipher in _ciphers.Values) cipher.Dispose();
            _ciphers.Clear();
            _cipherKeys.Clear();
            _guards.Clear();
            Streams.Clear();
        }

        private void OnDestroy()
        {
            Stop();
            if (_instance == this) _instance = null;
        }
    }
}

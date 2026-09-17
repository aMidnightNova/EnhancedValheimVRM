using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // listens for vmc on the loopback udp port and keeps the latest arkit blendshape values.
    // only /VMC/Ext/Blend/Val and /VMC/Ext/Blend/Apply are read, everything else is ignored
    public sealed class VmcReceiver : MonoBehaviour
    {
        private static VmcReceiver _instance;

        private UdpClient _socket;
        private CancellationTokenSource _stop;
        private int _port;
        private readonly float[] _pending = new float[Sharing.FaceWire.ValueCount];
        private readonly float[] _values = new float[Sharing.FaceWire.ValueCount];
        private readonly object _lock = new object();
        private double _lastFrame = double.NegativeInfinity;
        private float _frameSeconds = 1f / 30f;
        private float _nextCheck;

        // true when a frame arrived in the last two seconds
        internal static bool Live => _instance != null && Time.realtimeSinceStartupAsDouble - _instance._lastFrame < 2;

        // copies the latest frame into values with when it arrived and how far apart frames come,
        // false when nothing has arrived yet
        internal static bool TryCopy(float[] values, out double arrived, out float frameSeconds)
        {
            arrived = 0;
            frameSeconds = 1f / 30f;
            var receiver = _instance;
            if (receiver == null || double.IsNegativeInfinity(receiver._lastFrame)) return false;
            lock (receiver._lock)
            {
                Array.Copy(receiver._values, values, values.Length);
                arrived = receiver._lastFrame;
                frameSeconds = receiver._frameSeconds;
            }

            return true;
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + 1;
            var wanted = Settings.FaceEnabled ? Settings.VmcPort : 0;
            if (_socket != null && wanted == _port) return;
            Stop();
            if (wanted == 0) return;
            try
            {
                _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, wanted));
                _port = wanted;
                _stop = new CancellationTokenSource();
                var socket = _socket;
                var token = _stop.Token;
                Task.Run(() => Listen(socket, token));
                Logger.Log("Listening for VMC on 127.0.0.1:" + wanted + ".");
            }
            catch (SocketException error)
            {
                _socket = null;
                _nextCheck = Time.realtimeSinceStartup + 15;
                Logger.LogOnce("vmc-port", "Cannot listen for VMC on port " + wanted + ": " + error.Message);
            }
        }

        private void Stop()
        {
            _stop?.Cancel();
            _socket?.Close();
            _socket = null;
            _stop = null;
            _port = 0;
            _lastFrame = double.NegativeInfinity;
        }

        private void OnDestroy()
        {
            Stop();
            if (_instance == this) _instance = null;
        }

        private async Task Listen(UdpClient socket, CancellationToken token)
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

                if (!IPAddress.IsLoopback(received.RemoteEndPoint.Address)) continue;
                try
                {
                    Read(received.Buffer, 0, received.Buffer.Length);
                }
                catch (Exception error) when (error is IndexOutOfRangeException || error is ArgumentException)
                {
                    // a malformed packet is just skipped
                }
            }
        }

        // osc: a bundle is "#bundle", a time tag and size prefixed elements. a message is an address,
        // a type tag string and big endian arguments, every string padded to four bytes
        private void Read(byte[] data, int offset, int length)
        {
            var end = offset + length;
            if (length >= 16 && data[offset] == '#')
            {
                var cursor = offset + 16;
                while (cursor + 4 <= end)
                {
                    var size = ReadInt(data, cursor);
                    cursor += 4;
                    if (size < 0 || cursor + size > end) return;
                    Read(data, cursor, size);
                    cursor += size;
                }

                return;
            }

            var address = ReadString(data, ref offset, end);
            if (address == "/VMC/Ext/Blend/Apply")
            {
                var now = Time.realtimeSinceStartupAsDouble;
                lock (_lock)
                {
                    Array.Copy(_pending, _values, _values.Length);
                    // how often the tracker sends, so the avatar can move between frames instead of stepping
                    if (!double.IsNegativeInfinity(_lastFrame))
                        _frameSeconds = Mathf.Clamp((float)(now - _lastFrame), 1f / 60f, 0.25f);
                    _lastFrame = now;
                }

                return;
            }

            if (address != "/VMC/Ext/Blend/Val") return;
            var tags = ReadString(data, ref offset, end);
            if (tags.Length < 3 || tags[1] != 's' || tags[2] != 'f') return;
            var name = ReadString(data, ref offset, end);
            var value = ReadFloat(data, offset);
            var index = Sharing.FaceWire.IndexOf(name);
            if (index >= 0) _pending[index] = value;
        }

        private static string ReadString(byte[] data, ref int offset, int end)
        {
            var start = offset;
            while (offset < end && data[offset] != 0) offset++;
            var text = Encoding.ASCII.GetString(data, start, offset - start);
            offset = (offset + 4) & ~3;
            return text;
        }

        private static int ReadInt(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static float ReadFloat(byte[] data, int offset)
        {
            return BitConverter.ToSingle(new[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] },
                0);
        }
    }
}

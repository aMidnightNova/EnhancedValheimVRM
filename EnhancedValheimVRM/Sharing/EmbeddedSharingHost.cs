using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM.Sharing;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // The server worker owns its listener, filesystem and socket teardown.
    public sealed class EmbeddedSharingHost : MonoBehaviour
    {
        private ZNet _network;
        private CancellationTokenSource _stop;
        private Task _serving;
        private Task<int> _listening;
        private volatile AvatarTcpServer _storage;
        internal AvatarTcpServer Storage => ListeningPort != 0 ? _storage : null;

        internal int ListeningPort =>
            _network != null && _network == ZNet.instance &&
            _network.IsServer() && _network.IsDedicated() && Settings.EnableSharingServer &&
            _serving != null && !_serving.IsCompleted &&
            _listening?.Status == TaskStatus.RanToCompletion
                ? _listening.Result
                : 0;

        private Task _stopping = Task.CompletedTask;
        private Task _addressCheck;
        private float _nextCheck, _nextAddressCheck = 3600;
        private int _port, _facePort, _bytesPerSecond, _slots, _bundleLimit, _uploadMbps;
        private string _bindAddress;
        private bool _dedicated;

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + 1;
            var network = ZNet.instance;
            if (network != null && network.IsServer() && !network.IsDedicated() && ZNet.IsOpenServer() &&
                Settings.EnableSharingServer)
            {
                Logger.LogOnce("sharing-player-host",
                    "Player-hosted VRM sharing is disabled: no verified TCP hole-punch path is implemented. Dedicated-server sharing remains available.");
            }

            if (network == null || !SharingEndpoint.ShouldHost(network.IsServer(),
                    network.IsDedicated(),
                    Settings.EnableSharingServer))
            {
                StopHost();
                return;
            }

            // If for whatever reason the server is on a dynamic ip this will update its ip once per hour.
            if (Time.realtimeSinceStartup >= _nextAddressCheck && (_addressCheck == null || _addressCheck.IsCompleted))
            {
                _nextAddressCheck = Time.realtimeSinceStartup + 3600;
                _addressCheck = Task.Run(() => SharingRpc.SetPublicAddress(LookUpPublicAddress()));
            }

            var policy = Settings.GetDownloadPolicy();
            if (_serving != null && !_serving.IsCompleted && _bytesPerSecond == policy.BytesPerSecond &&
                _bundleLimit == Settings.BundleLimitBytes && _slots == policy.Slots && _network == network &&
                _uploadMbps == Settings.DedicatedUploadMbps &&
                _port == Settings.SharingPort && _facePort == Settings.FacePort &&
                _bindAddress == Settings.SharingBindAddress && _dedicated == network.IsDedicated())
                return;
            var failed = _serving?.IsFaulted ?? false;
            StopHost();
            if (failed)
            {
                _nextCheck = Time.realtimeSinceStartup + 15;
                Logger.LogOnce("sharing-listen-failed",
                    "Cannot run embedded VRM TCP server. Check Sharing.BindAddress and ServerPort; retrying.");
                return;
            }

            _network = network;
            _port = Settings.SharingPort;
            _facePort = Settings.FacePort;
            _bindAddress = Settings.SharingBindAddress;
            _dedicated = network.IsDedicated();
            _bytesPerSecond = policy.BytesPerSecond;
            _slots = policy.Slots;
            _bundleLimit = Settings.BundleLimitBytes;
            _uploadMbps = Settings.DedicatedUploadMbps;
            _stop = new CancellationTokenSource();
            var cancellation = _stop.Token;
            string bindAddress = _bindAddress, path = Path.Combine(Constants.Vrm.Dir, "Server");
            int port = _port, facePort = _facePort, bundleLimit = _bundleLimit, uploadMbps = _uploadMbps;
            var stopped = _stopping;
            var listening = new TaskCompletionSource<int>();
            _listening = listening.Task;
            _serving = Task.Run(async () =>
            {
                await stopped.ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (!IPAddress.TryParse(bindAddress, out var address))
                    throw new ArgumentException("Invalid sharing bind address.");
                using (var server = new AvatarTcpServer(path, address, port, policy)
                       {
                           BundleLimitBytes = bundleLimit, UploadMbps = uploadMbps
                       })
                {
                    server.Uploaded += (session, id, info) => SharingRpc.UploadCompleted(server, session, id, info);
                    _storage = server;
                    listening.TrySetResult(server.Port);
                    Logger.Log("Avatar sharing started on port " + port + ".");
                    // Face relay failure does not stop avatar sharing.
                    FaceRelay relay = null;
                    var relaying = Task.CompletedTask;
                    try
                    {
                        relay = new FaceRelay(address, facePort)
                        {
                            Notice = text => Logger.Log(text, Logger.LogLevel.Debug)
                        };
                        SharingRpc.RelayStarted(relay);
                        relaying = relay.RunAsync(cancellation);
                        Logger.Log("Face stream relay listening on udp port " + relay.Port + ".");
                    }
                    catch (System.Net.Sockets.SocketException error)
                    {
                        Logger.LogWarning("Face stream relay could not listen on udp port " + facePort + ": " +
                            error.Message + ". Faces are off, avatar sharing still works.");
                    }

                    try
                    {
                        await server.RunAsync(cancellation).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (relay != null)
                        {
                            SharingRpc.RelayStopped(relay);
                            relay.Dispose();
                            try
                            {
                                await relaying.ConfigureAwait(false);
                            }
                            catch (Exception) { }
                        }
                    }
                }
            });
        }

        private static readonly string[] PublicAddressServices =
        {
            "https://ipv4.icanhazip.com/", "https://api.ipify.org", "https://ipv4.myip.wtf/text",
            "https://checkip.amazonaws.com/", "https://ipinfo.io/ip/"
        };

        // based on valheims ZNet.GetPublicIP and its ipv4 sites. random order like its random pick, 500ms each
        // and the list runs twice, so this blocks the caller about 5s at worst. null when none answered
        internal static string LookUpPublicAddress()
        {
            var random = new System.Random();
            var order = PublicAddressServices.OrderBy(service => random.Next()).ToArray();
            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(500) })
            {
                foreach (var service in order.Concat(order))
                {
                    // on a worker so the request never needs the main thread this is blocking
                    var request = Task.Run(async () =>
                    {
                        try
                        {
                            return await client.GetStringAsync(service);
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    });
                    var answer = request.Wait(500) ? request.Result : null;
                    if (!IPAddress.TryParse(answer?.Trim(), out var address) ||
                        address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        continue;
                    return address.ToString();
                }
            }

            Logger.LogWarning(
                "Could not look up this servers public address. Players might need to set ServerHost under [Sharing] in their config.");
            return null;
        }

        private void StopHost()
        {
            if (_stop != null)
            {
                var stop = _stop;
                var serving = _serving ?? Task.CompletedTask;
                _stopping = Task.Run(async () =>
                {
                    try
                    {
                        stop.Cancel();
                        await serving.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        /* Update reports failures; cancellation is normal. */
                    }
                    finally
                    {
                        stop.Dispose();
                    }
                });
            }

            _storage = null;
            _stop = null;
            _serving = null;
            _listening = null;
            _network = null;
        }

        private void OnDestroy()
        {
            StopHost();
        }

        private void OnApplicationQuit()
        {
            StopHost();
        }
    }
}

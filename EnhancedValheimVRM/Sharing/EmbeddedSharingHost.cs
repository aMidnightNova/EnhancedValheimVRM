using System;
using System.IO;
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
        private float _nextCheck;
        private int _port, _bytesPerSecond, _slots, _bundleLimit, _uploadMbps;
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

            var policy = Settings.GetDownloadPolicy();
            if (_serving != null && !_serving.IsCompleted && _bytesPerSecond == policy.BytesPerSecond &&
                _bundleLimit == Settings.BundleLimitBytes && _slots == policy.Slots && _network == network &&
                _uploadMbps == Settings.DedicatedUploadMbps &&
                _port == Settings.SharingPort &&
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
            _bindAddress = Settings.SharingBindAddress;
            _dedicated = network.IsDedicated();
            _bytesPerSecond = policy.BytesPerSecond;
            _slots = policy.Slots;
            _bundleLimit = Settings.BundleLimitBytes;
            _uploadMbps = Settings.DedicatedUploadMbps;
            _stop = new CancellationTokenSource();
            var cancellation = _stop.Token;
            string bindAddress = _bindAddress, path = Path.Combine(Constants.Vrm.Dir, "Server");
            int port = _port, bundleLimit = _bundleLimit, uploadMbps = _uploadMbps;
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
                    await server.RunAsync(cancellation).ConfigureAwait(false);
                }
            });
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

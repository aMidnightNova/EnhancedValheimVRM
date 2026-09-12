using UnityEngine;

namespace EnhancedValheimVRM
{
    // Unity lifecycle adapter; all peer/port/outfit state belongs to SharingRpc.
    public sealed class SharingPortDiscovery : MonoBehaviour
    {
        private EmbeddedSharingHost _host;
        private void Awake() => _host = GetComponent<EmbeddedSharingHost>();
        private void Update() => SharingRpc.Tick(_host != null ? _host.Storage : null);
        private void OnDestroy() => SharingRpc.Reset(null);
    }
}
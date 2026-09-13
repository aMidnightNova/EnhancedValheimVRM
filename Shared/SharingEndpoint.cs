using System;

namespace EnhancedValheimVRM.Sharing
{
    public static class SharingEndpoint
    {
        public static bool ShouldHost(bool isServer, bool isDedicated, bool enabled)
        {
            // No raw-TCP NAT traversal/probe is implemented yet. An open game
            // session or known public IP is not proof that this listener is reachable.
            // Fail closed for player hosts; dedicated servers keep their direct path.
            return enabled && isServer && isDedicated;
        }

        public static string ResolveClientHost(string configuredHost, string gameServer)
        {
            if (!string.IsNullOrWhiteSpace(configuredHost)) return configuredHost.Trim();
            if (string.IsNullOrEmpty(gameServer)) return null;
            // Valheim exposes socket/host:port, steam/id/host:port, or playfab/id.
            // A relay identity alone is not a publicly reachable TCP address.
            if (!gameServer.StartsWith("socket/", StringComparison.Ordinal) &&
                !gameServer.StartsWith("steam/", StringComparison.Ordinal))
                return null;
            var endpoint = gameServer.Substring(gameServer.LastIndexOf('/') + 1);
            var colon = endpoint.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(endpoint.Substring(colon + 1), out var port) || port <= 0 ||
                port > 65535)
                return null;
            var host = endpoint.Substring(0, colon).Trim('[', ']');
            return Uri.CheckHostName(host) == UriHostNameType.Unknown ? null : host;
        }
    }
}

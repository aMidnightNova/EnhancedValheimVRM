using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace EnhancedValheimVRM.Sharing
{
    public static class SharingEndpoint
    {
        public sealed class Candidate
        {
            public string Host, Source;
        }

        public static bool ShouldHost(bool isServer, bool isDedicated, bool enabled)
        {
            // No raw-TCP NAT traversal/probe is implemented yet. An open game
            // session or known public IP is not proof that this listener is reachable.
            // Fail closed for player hosts; dedicated servers keep their direct path.
            return enabled && isServer && isDedicated;
        }

        // the addresses a client tries. ServerHost from its config, the address it joined with,
        // then the public address the server looked up for itself
        public static List<Candidate> Candidates(string configuredHost, string joinedHost, string serverAddress)
        {
            var candidates = new List<Candidate>();
            Add(candidates, configuredHost, "ServerHost in your config");
            Add(candidates, joinedHost, "the address you joined with");
            Add(candidates, serverAddress, "the servers public address");
            return candidates;
        }

        private static void Add(List<Candidate> candidates, string host, string source)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            candidates.Add(new Candidate { Host = host.Trim(), Source = source });
        }

        // pings each candidate on the sharing port, one at a time, and returns the first ip that answers.
        // a hostname is looked up first. tried lists every attempt for the log
        public static string FindReachableAddress(IEnumerable<Candidate> candidates,
            int port,
            int timeoutMs,
            out string tried)
        {
            var attempts = new List<string>();
            var pinged = new HashSet<string>();
            string found = null;
            foreach (var candidate in candidates)
            {
                var address = Resolve(candidate.Host);
                var label = candidate.Host +
                    (address != null && address != candidate.Host ? " (" + address + ")" : "") +
                    " from " + candidate.Source;
                if (address == null)
                    attempts.Add(label + ": no ip for that name");
                else if (!pinged.Add(address))
                    attempts.Add(label + ": same ip as above");
                else if (AvatarTcpClient.Ping(address, port, timeoutMs))
                {
                    attempts.Add(label + ": answered");
                    found = address;
                    break;
                }
                else
                    attempts.Add(label + ": no answer");
            }

            tried = string.Join(", ", attempts);
            return found;
        }

        // ipv4 first, the sharing port listens on ipv4 unless the server changed BindAddress
        private static string Resolve(string host)
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host.Trim('[', ']'));
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ??
                    addresses.FirstOrDefault();
                return address?.ToString();
            }
            catch (Exception error) when (error is SocketException || error is ArgumentException)
            {
                return null;
            }
        }
    }
}

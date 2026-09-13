using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Configuration;
using System.Text.RegularExpressions;
using EnhancedValheimVRM.Sharing;

namespace EnhancedValheimVRM
{
    public static class Settings
    {
        private static ConfigEntry<bool> _smoothAvatarTurning;
        private static ConfigEntry<bool> _showSocketGizmos, _logLoadTiming;
        private static ConfigEntry<bool> _useDefaultVrm;
        private static ConfigEntry<bool> _enableVrmSharing;
        private static ConfigEntry<string> _shaderBundle;

        private static ConfigEntry<bool> _enableProfileCode;
        private static ConfigEntry<int> _profileLogThresholdMs;
        private static ConfigEntry<int> _callThreshold;
        private static ConfigEntry<int> _timeWindowMs;
        private static ConfigEntry<Logger.LogLevel> _logLevel;
        private static ConfigEntry<string> _vrmKey;

        private static ConfigEntry<string> _sharingHost;
        private static ConfigEntry<int> _sharingPort, _bundleLimitMiB;
        private static ConfigEntry<bool> _enableSharingServer;
        private static ConfigEntry<string> _sharingBindAddress;
        private static ConfigEntry<int> _uploadMbps;
        private static ConfigEntry<int> _dedicatedDownloadMbps;
        private static ConfigEntry<int> _dedicatedDownloadSlots;


        public static class ShaderOptions
        {
            public static string Current => "current";
            public static string Old => "old";
        }

        public static void Init(ConfigFile config)
        {
            _showSocketGizmos = config.Bind("Diagnostics",
                "ShowSocketGizmos",
                false,
                "Show labeled attachment sockets for testing.");
            _logLoadTiming = config.Bind("Diagnostics",
                "LogLoadTiming",
                false,
                "Log each avatar loading stage and its duration, without keys or transfer tickets.");
            _smoothAvatarTurning = config.Bind("Animation",
                "SmoothAvatarTurning",
                true,
                "Interpolate VRM visual rotation between physics ticks. Adds up to one physics tick of visual delay; does not change player movement or combat.");


            _useDefaultVrm = config.Bind("General",
                "UseDefaultVrm",
                false,
                "Use the Default VRM file. ___Default.");

            _enableVrmSharing = config.Bind("General",
                "EnableVrmSharing",
                true,
                "Share your VRM to and receive VRM files from other players.");

            _shaderBundle = config.Bind("General",
                "ShaderBundle",
                ShaderOptions.Current,
                "Use the current or old shader bundle. Options are: [old, current]. you probably don't need to change this.");

            _enableProfileCode = config.Bind("General",
                "EnableProfileCode",
                false,
                "Enable Profiling code for the Unity Update Loops.");

            _profileLogThresholdMs = config.Bind("General",
                "ProfileLogThresholdMs",
                20,
                "the amount of time in ms to alert on if exceeded.");

            _callThreshold = config.Bind("General",
                "CallThreshold",
                20,
                "how many times does the method need to be called in TimeWindowMs for it to log.");

            _timeWindowMs = config.Bind("General",
                "TimeWindowMs",
                100,
                "the time frame in which to count how many time a method was called.");

            _logLevel = config.Bind("General",
                "LogLevel",
                Logger.LogLevel.Info,
                "Level of log output.");

            _sharingHost = config.Bind("Sharing",
                "ServerHost",
                string.Empty,
                "Optional TCP hostname override. Empty uses the connected Valheim server address. Relay-only connections require a reachable hostname.");
            _enableSharingServer = config.Bind("Sharing",
                "EnableServer",
                true,
                "Automatically host the embedded TCP service on dedicated servers. Player-hosted sharing is disabled until a direct NAT traversal path can be verified.");
            _sharingBindAddress = config.Bind("Sharing",
                "BindAddress",
                "0.0.0.0",
                "IP address on which the embedded server listens. 0.0.0.0 listens on all IPv4 interfaces.");
            _sharingPort = config.Bind("Sharing",
                "ServerPort",
                6067,
                new ConfigDescription(
                    "Server-only TCP listen port. Clients ignore this local setting and receive the port from the game server over RPC.",
                    new AcceptableValueRange<int>(1, 65535)));
            _bundleLimitMiB = config.Bind("Sharing",
                "MaxBundleMiB",
                384,
                new ConfigDescription(
                    "Server-only maximum encrypted ZIP size in MiB (1048576 bytes). Announced to clients over RPC. Expanded VRM has a separate 1023 MiB bound.",
                    new AcceptableValueRange<int>(1, 1024)));
            _uploadMbps = config.Bind("Sharing",
                "UploadMbps",
                6,
                new ConfigDescription("Maximum Mbps when uploading your avatar. Mbps means megabits per second.",
                    new AcceptableValueRange<int>(1, 10000)));

            _dedicatedDownloadMbps = config.Bind("Sharing",
                "DedicatedDownloadMbps",
                25,
                new ConfigDescription("Maximum Mbps for EACH download served by a dedicated server.",
                    new AcceptableValueRange<int>(1, 10000)));
            _dedicatedDownloadSlots = config.Bind("Sharing",
                "DedicatedDownloadSlots",
                4,
                new ConfigDescription("Simultaneous downloads served by a dedicated server.",
                    new AcceptableValueRange<int>(1, 32)));
            _vrmKey = config.Bind("Security",
                "VrmKey",
                string.Empty,
                "Automatically generated sharing key used by all your characters. Keep private. Clearing it rotates the key on restart.");
            if (!BundleCrypto.IsValidKey(_vrmKey.Value))
            {
                // The old unfinished key+IV format was unusable; replace it once.
                _vrmKey.Value = BundleCrypto.GenerateKey();
                config.Save();
            }
        }

        internal static bool ShowSocketGizmos => _showSocketGizmos.Value;
        internal static bool LogLoadTiming => _logLoadTiming.Value;
        internal static bool SmoothAvatarTurning => _smoothAvatarTurning.Value;
        internal static bool UseDefaultVrm => _useDefaultVrm.Value;
        internal static bool EnableVrmSharing => _enableVrmSharing.Value;
        internal static string ShaderBundle => _shaderBundle.Value;
        internal static bool EnableProfileCode => _enableProfileCode.Value;
        internal static int ProfileLogThresholdMs => _profileLogThresholdMs.Value;
        internal static int CallThreshold => _callThreshold.Value;
        internal static int TimeWindowMs => _timeWindowMs.Value;
        internal static Logger.LogLevel LogLevel => _logLevel.Value;
        internal static string VrmKey => _vrmKey.Value;
        internal static int UploadBytesPerSecond => SharingDownloadPolicy.ToBytesPerSecond(_uploadMbps.Value);

        internal static SharingDownloadPolicy GetDownloadPolicy()
        {
            return new SharingDownloadPolicy(_dedicatedDownloadMbps.Value, _dedicatedDownloadSlots.Value);
        }

        internal static bool EnableSharingServer => _enableSharingServer.Value;
        internal static string SharingBindAddress => _sharingBindAddress.Value;
        internal static string SharingHost => _sharingHost.Value;
        internal static int BundleLimitBytes => _bundleLimitMiB.Value * 1048576;
        internal static int SharingPort => _sharingPort.Value;
    }
}

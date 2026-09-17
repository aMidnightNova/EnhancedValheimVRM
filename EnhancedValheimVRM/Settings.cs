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
        private static ConfigEntry<int> _dedicatedUploadMbps;
        private static ConfigEntry<int> _dedicatedDownloadSlots;
        private static ConfigEntry<int> _facePort, _vmcPort, _faceSendRate;
        private static ConfigEntry<int> _faceJitterMs;
        private static ConfigEntry<bool> _faceEnabled, _receiveFaceStreams;


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
                SharingUploadPolicy.DefaultClientMbps,
                new ConfigDescription(
                    "Maximum Mbps when uploading your avatar. Mbps means megabits per second. Capped at 100; the server announces its own per-upload limit (default 60) and the lower of the two is used.",
                    new AcceptableValueRange<int>(1, SharingUploadPolicy.HardLimitMbps)));
            _dedicatedUploadMbps = config.Bind("Sharing",
                "DedicatedUploadMbps",
                SharingUploadPolicy.DefaultServerMbps,
                new ConfigDescription(
                    "Server-only maximum Mbps for EACH avatar upload it receives. Announced to clients over RPC and enforced by the server. Hard limit 100.",
                    new AcceptableValueRange<int>(1, SharingUploadPolicy.HardLimitMbps)));

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
            _facePort = config.Bind("Sharing",
                "FacePort",
                0,
                new ConfigDescription(
                    "Server only UDP port for the face stream relay. 0 uses ServerPort on UDP. Open it as UDP, TCP is not enough. Any free port works, valheim itself holds the game port and the one above it on UDP.",
                    new AcceptableValueRange<int>(0, 65535)));
            _faceEnabled = config.Bind("Face",
                "Enabled",
                false,
                "Listen for VMC steam on localhost, drive your own avatar with it and publish your face to the server.");
            _receiveFaceStreams = config.Bind("Face",
                "ReceiveFaceStreams",
                true,
                "Show other players faces. false opens no stream session and other faces stay neutral.");
            _vmcPort = config.Bind("Face",
                "VmcPort",
                39539,
                new ConfigDescription("UDP port your VMC sender talks to. localhost only, the sender is on this PC.",
                    new AcceptableValueRange<int>(1, 65535)));
            _faceSendRate = config.Bind("Face",
                "SendRate",
                30,
                new ConfigDescription(
                    "Face frames per second sent to the server. 30 is plenty, 60 is for trackers that actually run at 60, some run tracking independent of render FPS",
                    new AcceptableValueRange<int>(15, 60)));
            _faceJitterMs = config.Bind("Face",
                "JitterBufferMs",
                20,
                new ConfigDescription(
                    "How long a face packet from another player may run late and still be shown, in milliseconds. Packets later than that are dropped instead of played out of order. Raise it on a bad connection, it adds that much delay.",
                    new AcceptableValueRange<int>(0, 200)));
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
        internal static int UploadMbps => _uploadMbps.Value;
        internal static int DedicatedUploadMbps => SharingUploadPolicy.ClampServer(_dedicatedUploadMbps.Value);

        internal static SharingDownloadPolicy GetDownloadPolicy()
        {
            return new SharingDownloadPolicy(_dedicatedDownloadMbps.Value, _dedicatedDownloadSlots.Value);
        }

        internal static bool EnableSharingServer => _enableSharingServer.Value;
        internal static string SharingBindAddress => _sharingBindAddress.Value;
        internal static string SharingHost => _sharingHost.Value;
        internal static int BundleLimitBytes => _bundleLimitMiB.Value * 1048576;
        internal static int SharingPort => _sharingPort.Value;
        internal static int FacePort => _facePort.Value == 0 ? _sharingPort.Value : _facePort.Value;
        internal static bool FaceEnabled => _faceEnabled.Value;
        internal static bool ReceiveFaceStreams => _receiveFaceStreams.Value;
        internal static int VmcPort => _vmcPort.Value;
        internal static int FaceSendRate => _faceSendRate.Value;
        internal static int FaceJitterMs => _faceJitterMs.Value;
    }
}

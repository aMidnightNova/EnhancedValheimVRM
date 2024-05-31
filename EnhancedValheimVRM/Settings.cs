using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Configuration;
using System.Text.RegularExpressions;

namespace EnhancedValheimVRM
{
    public static class Settings
    {
        private static ConfigEntry<bool> _reloadInMenu;
        private static ConfigEntry<bool> _useDefaultVrm;
        private static ConfigEntry<bool> _enableVrmSharing;
        private static ConfigEntry<string> _shaderBundle;
        
        private static ConfigEntry<bool> _enableProfileCode;
        private static ConfigEntry<int> _profileLogThresholdMs;
        private static ConfigEntry<int> _callThreshold;
        private static ConfigEntry<int> _timeWindowMs;
        private static ConfigEntry<Logger.LogLevel> _logLevel;
        private static ConfigEntry<string> _vrmKey;

        private static string _key;
        private static string _iv;


        public static readonly string CharacterFile = $"{Constants.Settings.Dir}/characters";

        public static class ShaderOptions
        {
            public static string Current => "current";
            public static string Old => "old";
        }

        public static void Init(ConfigFile config)
        {
            _reloadInMenu = config.Bind("General",
                "ReloadInMenu",
                false,
                "Reload your VRM in the menu.");
            
            _useDefaultVrm = config.Bind("General",
                "UseDefaultVrm",
                false,
                "Use the Default VRM file. ___Default.");
            
            _enableVrmSharing = config.Bind("General",
                "EnableVrmSharing",
                false,
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
            
            _vrmKey = config.Bind("Security",
                "VrmKey",
                string.Empty,
                "Random key for VRM. This should NOT be changed. Invalid keys will regenerate. To invalidate, type something random or delete the VrmKey line.");

            ValidateOrGenerateKey(config);
            SplitKeyParts();
        }

        private static void ValidateOrGenerateKey(ConfigFile config)
        {
            if (string.IsNullOrEmpty(_vrmKey.Value) || !IsValidKeyAndIv(_vrmKey.Value))
            {
                _vrmKey.Value = GenerateRandomKey();
                config.Save();
            }
        }

        private static bool IsValidKeyAndIv(string key)
        {
            string pattern = @"^[0-9a-fA-F]{48}$";
            return Regex.IsMatch(key, pattern);
        }

        private static void SplitKeyParts()
        {
            string keyAndIv = _vrmKey.Value;
            _key = keyAndIv.Substring(0, 32); // First 16 bytes (32 hex characters) is the key
            _iv = keyAndIv.Substring(32, 16); // Next 8 bytes (16 hex characters) is the IV
        }

        private static string GenerateRandomKey()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] data = new byte[24]; // 128-bit key and 64-bit IV
                rng.GetBytes(data);
                return BitConverter.ToString(data).Replace("-", "").ToLower();  
            }
        }

        internal static bool ReloadInMenu => _reloadInMenu.Value;
        internal static bool UseDefaultVrm => _useDefaultVrm.Value;
        internal static bool EnableVrmSharing => _enableVrmSharing.Value;
        internal static string ShaderBundle => _shaderBundle.Value;
        internal static bool EnableProfileCode => _enableProfileCode.Value;
        internal static int ProfileLogThresholdMs => _profileLogThresholdMs.Value;
        internal static int CallThreshold => _callThreshold.Value;
        internal static int TimeWindowMs => _timeWindowMs.Value;
        internal static Logger.LogLevel LogLevel => _logLevel.Value;
        internal static string VrmKey => _key;
        internal static string VrmIv => _iv;
    }
}

using System;
using System.IO;
using BepInEx.Configuration;

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
 

        public static class ShaderOptions
        {
            public static string Current => "current";
            public static string Old => "old";
        }
 
        public static class Constants
        {
            public static readonly string VrmGoName = "VRM_Visual";
            public static readonly string DefaultVrmName = "___Default.vrm";
            public static readonly string DefaultVrmSettings = "settings____Default.vrm";
            public static readonly string VrmDir = Path.Combine(Environment.CurrentDirectory, "EnhancedValheimVRM");
            public static readonly string DefaultVrmPath = Path.Combine(VrmDir, DefaultVrmName);
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
        }

        internal static bool ReloadInMenu => _reloadInMenu.Value;
        
        internal static bool UseDefaultVrm => _useDefaultVrm.Value;

        internal static bool EnableVrmSharing => _enableVrmSharing.Value;

        internal static string ShaderBundle => _shaderBundle.Value;
        internal static bool EnableProfileCode => _enableProfileCode.Value;
        internal static int ProfileLogThresholdMs => _profileLogThresholdMs.Value;
        internal static int CallThreshold => _callThreshold.Value;
        internal static int TimeWindowMs => _timeWindowMs.Value;
    }
}
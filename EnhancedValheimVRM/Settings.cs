using BepInEx.Configuration;

namespace EnhancedValheimVRM
{
    public static class Settings
    {
        private static ConfigEntry<bool> _reloadInMenu;
        private static ConfigEntry<bool> _enableVrmSharing;
        private static ConfigEntry<string> _shaderBundle;


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
            _enableVrmSharing = config.Bind("General",
                "EnableVrmSharing",
                false,
                "Share your VRM to and receive VRM files from other players.");
            _shaderBundle = config.Bind("General",
                "ShaderBundle",
                ShaderOptions.Current,
                "Use the current or old shader bundle. Options are: [old, current]. you probably don't need to change this.");
        }

        internal static bool ReloadInMenu => _reloadInMenu.Value;

        internal static bool EnableVrmSharing => _enableVrmSharing.Value;

        internal static string ShaderBundle => _shaderBundle.Value;
    }
}
using System;
using System.IO;

namespace EnhancedValheimVRM
{
    public static class Constants
    {
        public const string PluginName = "EnhancedValheimVRM";
        private static readonly string PluginDir = Path.Combine(Environment.CurrentDirectory, PluginName);

        public static class Vrm
        {
            public static readonly string GoName = "VRM_Visual";
            public static readonly string DefaultName = "___Default.vrm";
            public static readonly string DefaultSettings = "settings____Default.vrm";
            public static readonly string Dir = PluginDir;
            public static readonly string DefaultPath = Path.Combine(PluginDir, DefaultName);
        }
        public static class Shaders
        {
            public static readonly string Dir = Path.Combine(PluginDir, "shaders");

        }
        public static class Keys
        {
            private const string Prefix = "evv_";
            public static readonly string PlayerName = $"{Prefix}PlayerName";
        }
    }
}
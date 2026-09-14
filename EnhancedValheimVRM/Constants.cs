using System;
using System.Collections.Generic;
using System.IO;

namespace EnhancedValheimVRM
{
    public static class Constants
    {
        public const string PluginName = "EnhancedValheimVRM";
        private static readonly string PluginDir = Path.Combine(Environment.CurrentDirectory, PluginName);
        private const string Prefix = "evv_";

        public static class Vrm
        {
            public static readonly string GoName = $"{Prefix}_vrm";
            public static readonly string DefaultName = "___Default.vrm";
            public static readonly string Dir = PluginDir;
            public static readonly string DefaultPath = Path.Combine(PluginDir, DefaultName);

            // case-insensitive filenames
            public static string Find(string fileName)
            {
                var exact = Path.Combine(Dir, fileName);
                if (File.Exists(exact) || !Directory.Exists(Dir)) return exact;
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.EnumerateFiles(Dir)) files[Path.GetFileName(path)] = path;
                return files.TryGetValue(fileName, out var found) ? found : exact;
            }
        }

        public static class Shaders
        {
            // r2modman flattens the zip so the bundles end up next to the dll.
            public static readonly string Dir = Resolve();

            private static string Resolve()
            {
                var dllDir = Path.GetDirectoryName(typeof(Constants).Assembly.Location);
                if (dllDir != null)
                {
                    if (File.Exists(Path.Combine(dllDir, "UniVrm.shaders"))) return dllDir;
                    var sub = Path.Combine(dllDir, "shaders");
                    if (File.Exists(Path.Combine(sub, "UniVrm.shaders"))) return sub;
                }

                return Path.Combine(PluginDir, "shaders");
            }
        }

        public static class Keys
        {
            public static readonly string PlayerName = $"{Prefix}PlayerName";
        }
    }
}

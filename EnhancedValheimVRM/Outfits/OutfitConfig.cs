using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EnhancedValheimVRM
{
    public sealed class OutfitDefinition
    {
        public string Name;
        public bool IsDefault;
        public readonly Dictionary<string, bool> Meshes = new Dictionary<string, bool>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Blendshapes = new Dictionary<string, float>(StringComparer.Ordinal);
    }

    public sealed class OutfitConfig
    {
        public readonly List<OutfitDefinition> Outfits = new List<OutfitDefinition>();

        // the [Blendshapes] section, blendshapes to keep loaded even if nothing uses them
        public readonly HashSet<string> KeepBlendshapes = new HashSet<string>(StringComparer.Ordinal);

        public const string KeepSection = "Blendshapes";
        public OutfitDefinition Default => Outfits.Find(outfit => outfit.IsDefault);

        public OutfitDefinition Find(string name)
        {
            return Outfits.Find(outfit => string.Equals(outfit.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static OutfitConfig Parse(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > Sharing.SharingWire.MaxSettingsBytes)
                throw new InvalidDataException("Outfit file exceeds 128 KiB.");
            var config = new OutfitConfig();
            OutfitDefinition current = null;
            var keepList = false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string raw;
                while ((raw = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//") || line.StartsWith(";"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        var name = line.Substring(1, line.Length - 2).Trim();
                        if (name.Equals(KeepSection, StringComparison.OrdinalIgnoreCase))
                        {
                            current = null;
                            keepList = true;
                            continue;
                        }

                        keepList = false;
                        Sharing.SharingWire.ValidateOutfitName(name);
                        if (config.Find(name) != null || config.Outfits.Count >= 64)
                            throw new InvalidDataException("Duplicate outfit name or too many outfits.");
                        current = new OutfitDefinition { Name = name };
                        config.Outfits.Add(current);
                        keys.Clear();
                        continue;
                    }

                    var equals = line.IndexOf('=');
                    if (keepList)
                    {
                        // One blendshape name per line
                        if (equals >= 0 || config.KeepBlendshapes.Count >= 1024)
                            throw new InvalidDataException("Invalid blendshape entry at line " + lineNumber);
                        config.KeepBlendshapes.Add(line);
                        continue;
                    }

                    if (current == null || equals < 1)
                        throw new InvalidDataException("Invalid outfit entry at line " + lineNumber);
                    var key = line.Substring(0, equals).Trim();
                    var value = line.Substring(equals + 1).Trim();
                    if (!keys.Add(key.Equals("Default", StringComparison.OrdinalIgnoreCase) ? "Default" : key))
                        throw new InvalidDataException("Duplicate outfit entry at line " + lineNumber);
                    if (key.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                        bool.TryParse(value, out var isDefault))
                        current.IsDefault = isDefault;
                    else if (key.StartsWith("mesh:") && key.Length > 5 && bool.TryParse(value, out var visible))
                        current.Meshes.Add(key.Substring(5), visible);
                    else if (key.StartsWith("blendshape:") && key.Substring(11).IndexOf(':') > 0 &&
                             !key.EndsWith(":") &&
                             float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) &&
                             !float.IsNaN(weight) && weight >= 0 && weight <= 100)
                        current.Blendshapes.Add(key.Substring(11), weight);
                    else
                        throw new InvalidDataException("Invalid outfit entry at line " + lineNumber + ": " + key);
                }
            }

            // a file with only a [Blendshapes] section is fine
            if (config.Outfits.Count > 0 && config.Outfits.Count(outfit => outfit.IsDefault) != 1)
                throw new InvalidDataException("Set Default=True in exactly one outfit section.");
            if (config.Outfits.Count == 0 && config.KeepBlendshapes.Count == 0)
                throw new InvalidDataException("Set Default=True in exactly one outfit section.");
            return config;
        }
    }
}

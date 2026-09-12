using System.Linq;

namespace EnhancedValheimVRM
{
    public static class BoneNames
    {
        private static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        public static bool Matches(string humanoidName, string candidate)
        {
            string full = Normalize(humanoidName), name = Normalize(candidate);
            foreach (string prefix in new[] { "mixamorig", "jbip", "bip001", "bip01", "armature" })
                if (name.StartsWith(prefix)) { name = name.Substring(prefix.Length); break; }
            string shortSide = full.Replace("left", "l").Replace("right", "r");
            string endSide = full.StartsWith("left") ? full.Substring(4) + "l" : full.StartsWith("right") ? full.Substring(5) + "r" : full;
            string legacy = full.Replace("upperleg", "upleg").Replace("lowerleg", "leg").Replace("upperarm", "arm").Replace("lowerarm", "forearm");
            string finger = full;
            foreach (string digit in new[] { "thumb", "index", "middle", "ring", "little" })
                finger = finger.Replace(digit + "proximal", "hand" + digit + "1").Replace(digit + "intermediate", "hand" + digit + "2").Replace(digit + "distal", "hand" + digit + "3");
            if (name == full || name == shortSide || name == endSide || name == legacy || name == legacy.Replace("left", "l").Replace("right", "r") || name == finger || name == finger.Replace("little", "pinky")) return true;
            if (!full.StartsWith("left") && !full.StartsWith("right") && name == "c" + full) return true;
            return (full == "hips" && name == "pelvis") || (full == "chest" && (name == "spine1" || name == "spine01")) ||
                (full == "upperchest" && (name == "spine2" || name == "spine02"));
        }
    }
}

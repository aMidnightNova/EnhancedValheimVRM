using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnhancedValheimVRM;
using EnhancedValheimVRM.Sharing;

internal static class OutfitChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        // A three-outfit file in the shape a real export produces: a flagged default, a mesh
        // turned off, a fractional blendshape weight and a partial one.
        var importedText = string.Join("\n",
            new[]
            {
                "[Default]", "Default=True", "mesh:Body=True", "mesh:Shorts=True", "mesh:Jacket=True", "",
                "[Casual]", "Default=False", "mesh:Jacket=False", "blendshape:Body:Tuck=0.92", "", "[Beach]",
                "Default=False", "mesh:Shorts=False", "mesh:Jacket=False", "blendshape:Body:Tuck=67", ""
            });
        var imported = OutfitConfig.Parse(importedText);
        check(imported.Outfits.Count == 3 && imported.Default.Name == "Default",
            "Three-outfit file with a flagged default");
        check(imported.Find("Beach").Meshes["Shorts"] == false, "Outfit mesh visibility");
        check(imported.Find("Casual").Blendshapes["Body:Tuck"] == 0.92f, "Fractional blendshape weight preserved");
        check(imported.Find("Beach").Blendshapes["Body:Tuck"] == 67, "Partial blendshape weight");
        var renamed =
            OutfitConfig.Parse("[Anything]\nDefault=False\n[My usual clothes]\nDefault=True\nmesh:Body=True\n");
        check(renamed.Default.Name == "My usual clothes", "Default must come from flag, not section name or order");
        // the [Blendshapes] section keeps names, it is not an outfit, and it can exist without outfit sections
        var kept = OutfitConfig.Parse(
            "[Look]\nDefault=True\nmesh:Body=True\n\n[Blendshapes]\njawOpen\nBlink\n#Wink\n#Sad\n");
        check(kept.Outfits.Count == 1 && kept.KeepBlendshapes.SetEquals(new[] { "jawOpen", "Blink" }),
            "Blendshapes section lists names to keep without becoming an outfit");
        var keepOnly = OutfitConfig.Parse("[blendshapes]\njawOpen\n");
        check(keepOnly.Outfits.Count == 0 && keepOnly.Default == null &&
            keepOnly.KeepBlendshapes.Contains("jawOpen"),
            "A file with only a Blendshapes section parses");
        foreach (var invalid in new[]
                 {
                     "[A]\nDefault=False", "[A]\nDefault=True\n[B]\nDefault=True",
                     "[A]\nDefault=True\n[a]\nDefault=False", "[A]\nDefault=True\nblendshape:Body:Test=NaN",
                     "[A]\nDefault=True\nblendshape:Body:Test=101", "[A]\nDefault=True\nmesh:Body=perhaps",
                     "[A]\nDefault=True\nmesh:Body=True\nmesh:Body=False", "[A]\nDefault=True\ndefault=False",
                     "[A]\nDefault=True\nblendshape:Body:=25", "[Blendshapes]\n",
                     "[A]\nDefault=False\n[Blendshapes]\nX\n", "[Blendshapes]\nBlink=True\n"
                 })
        {
            var rejected = false;
            try
            {
                OutfitConfig.Parse(invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            check(rejected, "Malformed outfit accepted: " + invalid);
        }

        var key = BundleCrypto.GenerateKey();
        var packed = BundleCrypto.PackProfile("", importedText);
        BundleCrypto.UnpackProfile(packed, out _, out var outfits);
        check(outfits == importedText, "Outfits lost from bundle");
        var originalVersion = BundleCrypto.Version(packed, key);
        check(BundleCrypto.Version(BundleCrypto.PackProfile("", importedText.Replace("Default=True", "Default=False")),
                key) != originalVersion,
            "Changing outfits did not invalidate the settings blob version");
        BundleCrypto.UnpackProfile(BundleCrypto.PackProfile("", ""), out _, out outfits);
        check(outfits == "", "Empty outfit file");
        var stripped = SharingWire.StripComments(
            "# top\r\n[Look]\r\n  Default=True  \r\n\r\n// note\r\n; note\r\nmesh:Body=True\r\n#mesh:Hat=True\r\n");
        check(stripped == "[Look]\nDefault=True\nmesh:Body=True\n" && SharingWire.StripComments(null) == "" &&
            SharingWire.StripComments("# only\n") == "",
            "Comments and blank lines are stripped before the outfit text gets transmitted");
        check(OutfitConfig.Parse(stripped).Default.Name == "Look", "Stripped outfit text still parses");

        await Task.CompletedTask;
    }
}

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
        foreach (var invalid in new[]
                 {
                     "[A]\nDefault=False", "[A]\nDefault=True\n[B]\nDefault=True",
                     "[A]\nDefault=True\n[a]\nDefault=False", "[A]\nDefault=True\nblendshape:Body:Test=NaN",
                     "[A]\nDefault=True\nblendshape:Body:Test=101", "[A]\nDefault=True\nmesh:Body=perhaps",
                     "[A]\nDefault=True\nmesh:Body=True\nmesh:Body=False", "[A]\nDefault=True\ndefault=False",
                     "[A]\nDefault=True\nblendshape:Body:=25"
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

        await Task.CompletedTask;
    }
}

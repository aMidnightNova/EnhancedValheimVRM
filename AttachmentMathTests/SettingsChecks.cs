using System;
using System.IO;
using EnhancedValheimVRM;

internal static class SettingsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "evrm-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Constants.Vrm.Dir = directory;
        var path = Path.Combine(directory, "settings_Test.txt");
        try
        {
            File.WriteAllText(path,
                "ModelScale=2\nSpringBoneStiffness=1.5\nHelmetVisible=True\nRightHandBackItemPos=(0,0.13,0)\n");
            var first = new VrmSettings("Test");
            check(first.ModelScale == 2 && first.SpringBoneStiffness == 1.5f && first.HelmetVisible,
                "Initial settings load");
            check(Math.Abs(first.RightHandBackItemPos.y - 0.13f) < 0.000001f, "Position setting parse");
            File.WriteAllText(path, "ModelScale=1\n");
            var replacement = new VrmSettings("Test");
            check(replacement.SpringBoneStiffness == 1 && !replacement.HelmetVisible,
                "Fresh reload candidate must restore omitted settings to defaults");
            check(first.ModelScale == 2 && first.HelmetVisible,
                "Candidate reload mutated previous settings before successful swap");
            // Per-class and per-weapon lines: class applies to every member, a named line replaces it,
            // and the lines survive the shared-settings round trip.
            File.WriteAllText(path,
                "WeaponScale=1.1\nWeaponScale=BowDraugrFang,1.2\nBowPos=<0,0.02,0>\nBowPos=Draugrfang,<0,0.03,0>\nBowRot=(0,0,15)\nKnifeHandRot=<0,10,0>\nMacePos=<0,0,-0.01>\nRightHandItemPos=<1,0,0>\n");
            var weapons = new VrmSettings("Test");
            check(Math.Abs(weapons.GetWeaponScale("BowFineWood") - 1.1f) < 0.000001f,
                "Global weapon scale did not apply");
            check(Math.Abs(weapons.GetWeaponScale("bowdraugrfang") - 1.2f) < 0.000001f,
                "Named weapon scale did not replace the global value case-insensitively");
            check(weapons.TryGetItemAdjustment("Bow", "Bow", false, out var p1, out var r1) && p1.y == 0.02f &&
                r1.z == 15,
                "Class line did not apply to a plain bow");
            check(weapons.TryGetItemAdjustment("Draugrfang", "Bow", false, out var p2, out var r2) && p2.y == 0.03f &&
                r2.z == 15,
                "Named line did not replace the class position or lost the class rotation");
            check(!weapons.TryGetItemAdjustment("Bow", "Bow", true, out _, out _), "Back line applied in hand");
            check(weapons.TryGetItemAdjustment("KnifeCopper", "Knife", true, out _, out var r3) && r3.y == 10,
                "Hand line did not apply");
            check(weapons.TryGetItemAdjustment("MaceBronze", "Club", false, out var p4, out _) && p4.z == -0.01f,
                "Mace alias did not map to Club");
            check(!weapons.TryGetItemAdjustment("SwordIron", "Sword", false, out _, out _),
                "Adjustment leaked to an unrelated class");
            check(weapons.RightHandItemPos.x == 1, "Slot setting parsed alongside weapon lines");
            var shared = new VrmSettings("Test", weapons.Serialize());
            check(shared.TryGetItemAdjustment("Draugrfang", "Bow", false, out var p5, out _) && p5.y == 0.03f &&
                shared.TryGetItemAdjustment("KnifeCopper", "Knife", true, out _, out var r5) && r5.y == 10,
                "Weapon lines lost in shared settings");
            check(Math.Abs(shared.GetWeaponScale("BowFineWood") - 1.1f) < 0.000001f &&
                Math.Abs(shared.GetWeaponScale("BowDraugrFang") - 1.2f) < 0.000001f,
                "Weapon scale lines lost in shared settings");
            File.WriteAllText(path, "BowPos=Draugrfang\n");
            var badWeapon = false;
            try
            {
                new VrmSettings("Test");
            }
            catch (InvalidDataException)
            {
                badWeapon = true;
            }

            check(badWeapon, "Weapon line without a vector accepted");
            foreach (var bad in new[]
                     {
                         "ModelScale=0", "ModelScale=banana", "ModelScale=NaN", "WeaponScale=0",
                         "WeaponScale=BowDraugrFang,NaN", "WeaponScale=,1.2", "SpringBoneStiffness=-1",
                         "InteractionDistanceScale=Infinity", "RightHandBackItemPos=(0,NaN,0)"
                     })
            {
                File.WriteAllText(path, bad);
                var rejected = false;
                try
                {
                    new VrmSettings("Test");
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                check(rejected, "Invalid avatar settings accepted: " + bad);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        foreach (var pair in new[]
                 {
                     ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"), ("RightUpperArm", "RightArm"),
                     ("RightLowerArm", "RightForeArm"), ("RightHand", "J_Bip_R_Hand"), ("Head", "J_Bip_C_Head"),
                     ("LeftFoot", "mixamorig:LeftFoot"), ("LeftUpperLeg", "mixamorig:LeftUpLeg"),
                     ("LeftHand", "Hand.L"), ("LeftLittleDistal", "LeftHandPinky3"),
                     ("LeftMiddleProximal", "LeftHandMiddle1"), ("Chest", "Spine1"), ("Hips", "Pelvis")
                 })
            check(BoneNames.Matches(pair.Item1, pair.Item2), "Common rig bone not recognized: " + pair);
        check(!BoneNames.Matches("LeftHand", "LeftHand_Attach"), "Socket mistaken for hand bone");
        check(!BoneNames.Matches("RightHand", "LeftHand"), "Opposite hand matched");
    }
}

// Replace logging and the filesystem root only; exercise the production settings parser.
namespace EnhancedValheimVRM
{
    public static class Constants
    {
        public static class Vrm
        {
            public static string Dir;
        }
    }

    public static class Logger
    {
        public static void Log(string value) { }
        public static void LogWarning(string value) { }
    }
}

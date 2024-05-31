using System.Collections;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class EnhancedValheimVrmPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.rawrtastic.plugins.enhancedvalheimvrm";
        private const string PluginName = "EnhancedValheimVRM";
        private const string PluginVersion = "1.0.0.0";

        private static Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            // avoid float parsing error on computers with different cultures
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Settings.Init(Config);

            // this make it so that the VRM patch is applied after the game loads a lot of itself.
            PatchFejdStartup.Apply(_harmony);
            if (Settings.EnableProfileCode) PatchAllUpdateMethods.ApplyPatches(_harmony);
        }

        internal static void PatchAll()
        {
            _harmony.PatchAll();
        }
    }
}
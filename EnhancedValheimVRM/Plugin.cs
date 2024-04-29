using System.Globalization;
using BepInEx;
using HarmonyLib;
using System.Reflection;


namespace EnhancedValheimVRM
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class Initialization : BaseUnityPlugin
    {
        private const string PluginGuid = "com.rawrtastic.plugins.enhancedvalheimvrm";
        private const string PluginName = "EnhancedValheimVRM";
        private const string PluginVersion = "1.0.0.0";

        private void Awake()
        {
            // avoid float parsing error on computers with different cultures
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Settings.Init(Config);

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
        }
    }
}
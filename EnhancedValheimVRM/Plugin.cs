using System.Collections;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

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

            // Start the coroutine to delay patching
            StartCoroutine(DelayedPatch(10f));
        }
        
        IEnumerator DelayedPatch(float delay)
        {
            yield return new WaitForSeconds(delay);

            // Apply Harmony patches after the delay
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
 
        }
    }
}
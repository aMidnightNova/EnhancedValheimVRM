using System.Collections;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class EnhancedValheimVrmPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.rawrtastic.plugins.enhancedvalheimvrm";
        private const string PluginName = "EnhancedValheimVRM";
        private const string PluginVersion = "1.0.0";

        private static EnhancedValheimVrmPlugin _instance;
        private static bool _clientInitialized;

        private static Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            // avoid float parsing error on computers with different cultures
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            _instance = this;
            Settings.Init(Config);
            gameObject.AddComponent<EmbeddedSharingHost>();
            gameObject.AddComponent<SharingPortDiscovery>();

            // this make it so that the VRM patch is applied after the game loads a lot of itself.
            PatchFejdStartup.Apply(_harmony);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void InitializeClient()
        {
            if (_clientInitialized) return;
            _clientInitialized = true;
            _harmony.PatchAll();
            _instance.gameObject.AddComponent<FileTransferController>();
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ConsoleCommands).TypeHandle);
            if (Settings.EnableProfileCode) PatchAllUpdateMethods.ApplyPatches(_harmony);
        }

        internal static void PatchAll()
        {
            if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;
            InitializeClient();
        }
    }
}
using System;
using System.Collections;
using System.IO;
using System.Globalization;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnhancedValheimVRM
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class EnhancedValheimVrmPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "com.rawrtastic.plugins.enhancedvalheimvrm";
        private const string PluginName = "EnhancedValheimVRM";
        private const string PluginVersion = "1.1.2";

        private static EnhancedValheimVrmPlugin _instance;
        private static bool _clientInitialized;

        private static Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            // avoid float parsing error on computers with different cultures
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            _instance = this;
            Settings.Init(Config);
            EnsureDataFolder();
            gameObject.AddComponent<EmbeddedSharingHost>();
            gameObject.AddComponent<SharingPortDiscovery>();

            // this make it so that the VRM patch is applied after the game loads a lot of itself.
            PatchFejdStartup.Apply(_harmony);
        }

        // make <game>/EnhancedValheimVRM and keep the .example files in it current, so people see
        // when a version adds settings. r2modman leaves the examples next to the dll, a hand install
        // has none there.
        private static void EnsureDataFolder()
        {
            try
            {
                Directory.CreateDirectory(Constants.Vrm.Dir);
                var dllDir = Path.GetDirectoryName(typeof(EnhancedValheimVrmPlugin).Assembly.Location);
                if (dllDir == null) return;
                foreach (var example in Directory.GetFiles(dllDir, "*.example"))
                {
                    var target = Path.Combine(Constants.Vrm.Dir, Path.GetFileName(example));
                    if (!File.Exists(target) || !SameContent(example, target)) File.Copy(example, target, true);
                }
            }
            catch (Exception error)
            {
                EnhancedValheimVRM.Logger.LogWarning("Could not set up " + Constants.Vrm.Dir + ": " + error.Message);
            }
        }

        private static bool SameContent(string a, string b)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fa = File.OpenRead(a))
            using (var fb = File.OpenRead(b))
            {
                var ha = sha.ComputeHash(fa);
                var hb = sha.ComputeHash(fb);
                for (var i = 0; i < ha.Length; i++)
                    if (ha[i] != hb[i])
                        return false;
                return true;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void InitializeClient()
        {
            if (_clientInitialized) return;
            _clientInitialized = true;
            FrameClock.Install();
            VrmAnimator.InstallPhysicsPose();
            PatchVisEquipmentUpdateLodgroup.InstallSwingingParts();
            _harmony.PatchAll();
            _instance.gameObject.AddComponent<FileTransferController>();
            _instance.gameObject.AddComponent<VmcReceiver>();
            _instance.gameObject.AddComponent<FaceStreamClient>();
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ConsoleCommands).TypeHandle);
            if (Settings.EnableProfileCode) PatchAllUpdateMethods.ApplyPatches(_harmony);
        }

        internal static void PatchAll()
        {
            // ZNet does not exist yet when the menu boots, so:
            // a dedicated server is a headless build with no graphics device.

            // the server needs none of the client patches and has no univrm dlls to scan them against anyways
            // the dll on the server should be standalone
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;
            InitializeClient();
        }
    }
}

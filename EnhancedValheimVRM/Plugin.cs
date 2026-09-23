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
        internal const string PluginVersion = "1.4.0";

        private static EnhancedValheimVrmPlugin _instance;
        private static bool _clientInitialized, _serverInitialized;

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
            // server and client both need the version handshake, so it is not part of the client patches
            PatchVersionCheck.Apply(_harmony);
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
            PatchImageBitDepth.Apply(_harmony);
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
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                (ZNet.instance != null && ZNet.instance.IsDedicated()))
            {
                InitializeServer();
                return;
            }

            InitializeClient();
        }

        // the dedicated server side of startup. avatar sharing needs the public address before anyone joins,
        // so the server waits for the lookup here
        private static void InitializeServer()
        {
            if (_serverInitialized) return;
            _serverInitialized = true;
            if (Settings.EnableSharingServer) SharingRpc.SetPublicAddress(EmbeddedSharingHost.LookUpPublicAddress());
        }
    }
}

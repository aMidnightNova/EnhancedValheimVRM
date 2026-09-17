using System.Runtime.CompilerServices;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    // a dedicated server turns away players on a different version of the mod after the password step.
    // players without the mod send nothing and get in like before
    internal static class PatchVersionCheck
    {
        // never rename this or change what it carries, older versions read it to show the popup
        private const string RpcName = Sharing.SharingWire.VersionRpc;

        private static readonly ConditionalWeakTable<ZRpc, string> ClientVersions =
            new ConditionalWeakTable<ZRpc, string>();

        private static string _serverVersion;

        public static void Apply(Harmony harmony)
        {
            var connection = AccessTools.Method(typeof(ZNet), "OnNewConnection");
            var peerInfo = AccessTools.Method(typeof(ZNet), "RPC_PeerInfo");
            if (connection == null || peerInfo == null)
            {
                Logger.LogWarning("Version check is off, this game version changed the join handshake.");
                return;
            }

            harmony.Patch(connection, postfix: new HarmonyMethod(typeof(PatchVersionCheck), nameof(Connected)));
            harmony.Patch(peerInfo, new HarmonyMethod(typeof(PatchVersionCheck), nameof(PeerInfo)));
        }

        private static void Connected(ZNet __instance, ZNetPeer peer)
        {
            peer.m_rpc.Register<string>(RpcName, Received);
            if (__instance.IsServer()) return;
            _serverVersion = null;
            peer.m_rpc.Invoke(RpcName, EnhancedValheimVrmPlugin.PluginVersion);
        }

        private static void Received(ZRpc rpc, string version)
        {
            if (ZNet.instance == null || version == null || version.Length > 32) return;
            if (ZNet.instance.IsServer())
            {
                ClientVersions.Remove(rpc);
                ClientVersions.Add(rpc, version);
                return;
            }

            _serverVersion = version;
            Logger.LogWarning("The server runs EnhancedValheimVRM " + version + ", you have " +
                EnhancedValheimVrmPlugin.PluginVersion + ".");
        }

        private static bool PeerInfo(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer() || !__instance.IsDedicated() ||
                !ClientVersions.TryGetValue(rpc, out var theirs) || theirs == EnhancedValheimVrmPlugin.PluginVersion)
                return true;
            Logger.LogWarning("Turned away " + rpc.GetSocket().GetHostName() + ", they have EnhancedValheimVRM " +
                theirs + " and this server runs " + EnhancedValheimVrmPlugin.PluginVersion + ".");
            rpc.Invoke(RpcName, EnhancedValheimVrmPlugin.PluginVersion);
            rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
            return false;
        }

        // client only, patched with the rest of the client patches. valheim shows this after the kick
        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        internal static class Popup
        {
            private static void Postfix(FejdStartup __instance)
            {
                if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorVersion || _serverVersion == null ||
                    _serverVersion == EnhancedValheimVrmPlugin.PluginVersion)
                    return;
                __instance.m_connectionFailedError.text = "EnhancedValheimVRM version mismatch.\nYou have " +
                    EnhancedValheimVrmPlugin.PluginVersion + ", the server is running " + _serverVersion + ".";
            }
        }
    }
}

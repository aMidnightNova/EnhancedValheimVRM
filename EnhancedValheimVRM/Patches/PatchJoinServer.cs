using HarmonyLib;

namespace EnhancedValheimVRM
{
    // the hostname or ip from the join by ip field, kept when join is pressed. valheim can swap it for a
    // crossplay id afterwards. empty for steam and join code joins
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.JoinServer))]
    internal static class PatchJoinServer
    {
        internal static string Host { get; private set; }

        private static void Prefix(FejdStartup __instance)
        {
            var join = __instance.GetServerToJoin();
            Host = join.m_type == ServerJoinDataType.Dedicated ? join.Dedicated.m_host : null;
        }
    }
}

using HarmonyLib;

namespace EnhancedValheimVRM
{
    // Extend only the local initial loading screen. Remote loads and live settings
    // reloads keep rendering and never block a game/network update or wait on a Task.
    [HarmonyPatch(typeof(Hud), "UpdateBlackScreen")]
    internal static class PatchAvatarLoadingScreen
    {
        private static void Prefix(ref Player player)
        {
            if (VrmController.InitialLocalAvatarPending) player = null;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.WaitingForRespawn))]
    internal static class PatchAvatarLoadingProgress
    {
        private static void Postfix(ref bool __result)
        {
            __result |= VrmController.InitialLocalAvatarPending;
        }
    }
}

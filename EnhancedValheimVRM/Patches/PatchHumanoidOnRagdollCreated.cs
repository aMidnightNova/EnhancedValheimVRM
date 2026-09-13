using HarmonyLib;

namespace EnhancedValheimVRM
{
    // The game only calls this on the client that owns the dying character.
    [HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
    internal static class PatchHumanoidOnRagdollCreated
    {
        private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
        {
            if (!(__instance is Player player) || ragdoll == null) return;
            VrmController.TransferToRagdoll(player.GetVrmInstance(), ragdoll);
        }
    }

    // Other players' corpses arrive through the network without that call. Claim them here.
    [HarmonyPatch(typeof(Ragdoll), "Awake")]
    internal static class PatchRagdollAwake
    {
        private static void Postfix(Ragdoll __instance)
        {
            VrmController.OnRagdollAppeared(__instance);
        }
    }
}

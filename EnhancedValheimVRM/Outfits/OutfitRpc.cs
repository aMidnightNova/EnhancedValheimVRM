using System;

namespace EnhancedValheimVRM
{
    // Outfit application only. SharingRpc owns peers, selections and network lifecycle.
    internal static class OutfitRpc
    {
        internal static void ClientReady()
        {
            var player = Player.m_localPlayer;
            var avatar = player != null ? player.GetVrmInstance() : null;
            var outfits = avatar?.GetGameObject()?.GetComponent<OutfitController>();
            if (avatar == null || !avatar.IsDisplayed || avatar.IsCorpse || outfits == null ||
                string.IsNullOrEmpty(outfits.CurrentName)) return;
            SharingRpc.SetOutfit(player.GetPlayerID(), outfits.CurrentName);
        }

        internal static void LocalChanged(Player player, OutfitController outfits)
        {
            if (player != null && player == Player.m_localPlayer &&
                player.GetVrmInstance()?.GetGameObject()?.GetComponent<OutfitController>() == outfits)
                SharingRpc.SelectOutfit(player.GetPlayerID(), outfits.CurrentName);
        }

        internal static void AvatarReady(Player player)
        {
            if (player == Player.m_localPlayer) ClientReady();
            else ApplyPending(player);
        }

        internal static void SelectionReceived(long id)
        {
            foreach (var player in Player.GetAllPlayers())
                if (player != null && player != Player.m_localPlayer && player.GetPlayerID() == id)
                    ApplyPending(player);
        }

        private static void ApplyPending(Player player)
        {
            if (player == null || player.IsDead()) return;
            string name = SharingRpc.GetOutfit(player.GetPlayerID());
            var avatar = player.GetVrmInstance();
            if (avatar == null || !avatar.IsDisplayed || avatar.IsCorpse) return;
            var outfits = avatar.GetGameObject()?.GetComponent<OutfitController>();
            if (outfits == null) return;
            outfits.Apply(name ?? outfits.CurrentName);
            SharingRpc.ApplyOverrides(player.GetPlayerID(), (part, blend, value) => outfits.Override(part, blend, value));
        }
    }
}
using System;
using System.Collections.Generic;

namespace EnhancedValheimVRM
{
    internal static class PlayerExtensions
    {
        public static string GetPlayerDisplayName(this Player player)
        {
            // cannot patch GetPlayerName - it causes the game to crash, use this in its place.

            var playerName = "";

            // see comments in PatchPlayerAwake for the reason this exists.
            if (player.m_customData.TryGetValue(Constants.Keys.PlayerName, out playerName)) return playerName;

            if (Game.instance != null)
            {
                playerName = player.GetPlayerName();
                if (playerName == "" || playerName == "...")
                {
                    playerName = Game.instance.GetPlayerProfile().GetName();
                    return playerName;
                }
            }
            else
            {
                var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
                var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
                if (index >= 0 && index < profiles.Count)
                {
                    playerName = profiles[index].GetName();
                    return playerName;
                }
            }

            return playerName;
        }

        // the character id lives in the players zdo and is not there right after the player spawns,
        // the same story as the name above. false until the game has filled it in, so callers keep
        // asking until it gets a real one
        public static bool TryGetPlayerId(this Player player, out long id)
        {
            id = player != null ? player.GetPlayerID() : 0;
            return id != 0;
        }

        public static bool IsInStartMenu(this Player player)
        {
            // i wonder if player.InIntro() is effectively the same as this.
            return player.gameObject.scene.name == "start";
        }

        public static VrmInstance GetVrmInstance(this Player player)
        {
            return VrmController.FindInstance(player);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    internal static class PlayerExtensions
    {
        public static string GetPlayerDisplayName(this Player player)
        {
            // cannot patch GetPlayerName - it causes the game to crash, use this in its place.

            var playerName = "";

            // see comments in PatchPlayerAwake for the reason this exists.
            if (player.m_customData.TryGetValue(Constants.Keys.PlayerName, out playerName))
            {
                return playerName;
            }

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

        public static bool IsInStartMenu(this Player player)
        {
            // i wonder if player.InIntro() is effectively the same as this.
            return player.gameObject.scene.name == "start";
        }

        public static SkinnedMeshRenderer GetSmrBody(this Player player)
        {
            var visual = player.GetField<Player, GameObject>("m_visual");
            return visual?.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(smr => smr.name == "body");
        }

        public static Animator GetVrmGoAnimator(this Player player)
        {
            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance != null)
            {
                return vrmInstance.GetVrmGoAnimator();
            }

            return null;
        }

        public static VrmAnimator GetVrmAnimator(this Player player)
        {
            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance != null)
            {
                var vrmGo = vrmInstance.GetGameObject();
                if (vrmGo != null)
                {
                    return vrmGo.GetComponent<VrmAnimator>();
                }
            }

            return null;
        }
    }
}

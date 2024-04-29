using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmController
    {
        private static Dictionary<Player, VrmInstance> VrmInstances = new Dictionary<Player, VrmInstance>();

        public void AttachVrmToPlayer(Player player)
        {
            var vrmInstance = new VrmInstance(player.name, Player.m_localPlayer.name == player.name);
        }

        public static VrmInstance GetVrmInstance(Player player)
        {
            return VrmInstances[player];
        }

        public static GameObject GetVrmInstanceGameObject(Player player)
        {
            return VrmInstances[player].GetGameObject();
        }
    }
}
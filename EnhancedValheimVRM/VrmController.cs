using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class VrmController
    {
        
        private static Dictionary<Player, VrmInstance> _vrmInstances = new Dictionary<Player, VrmInstance>();

        public static void AttachVrmToPlayer(Player player)
        {
            VrmInstance vrmInstance = null;

            try
            {
                vrmInstance = new VrmInstance(player);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
            }


            if (vrmInstance != null)
            {
                _vrmInstances.Add(player,vrmInstance);
                
            }
        }

 

        public static VrmInstance GetVrmInstance(this Player player)
        {
            if (_vrmInstances.TryGetValue(player, out var instance))
            {
                return instance;
            }

            return null;
        }

        public static bool HasVrmForPlayer(Player player)
        {
            return _vrmInstances.ContainsKey(player);
        }

        public static GameObject GetVrmInstanceGameObject(Player player)
        {
            if (_vrmInstances.TryGetValue(player, out var instance))
            {
                return instance.GetGameObject();
            }
            return null;
        }
    }
}
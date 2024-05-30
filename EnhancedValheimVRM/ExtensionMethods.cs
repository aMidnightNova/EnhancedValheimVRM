using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    internal static class ExtensionMethods
    {
        public static FieldInfo GetFieldValue<T>(this object instance, string fieldName)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            return instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
        
        public static Tout GetField<Tin, Tout>(this Tin self, string fieldName)
        {
            return AccessTools.FieldRefAccess<Tin, Tout>(fieldName).Invoke(self);
        }

        public static bool TryGetField<Tin, Tout>(this Tin self, string fieldName, out Tout result)
        {
            try
            {
                result = GetField<Tin, Tout>(self, fieldName);
                if (result == null)
                {
                   // Logger.Log($"Field '{fieldName}' exists but is null");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to access field '{fieldName}': {ex.Message}");
            }
            result = default(Tout);
            return false;
        }

        
        public static void SetVisible(this GameObject obj, bool flag)
        {
            foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = flag;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = flag;
        }
        
        
        public static void Set<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 value)
        {
            if (value == null)
            {
                dict.Remove(key);
            }
            else
            {
                dict[key] = value;
            }
        }

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
        
        
    }
}
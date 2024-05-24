using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "GetPlayerName")]
    public class PatchPlayerGetPlayerName
    {
        static bool Prefix(Player __instance, ref string __result)
        {
 
            // see comments in PatchPlayerAwake for the reason this exists.
            if (__instance.m_customData.TryGetValue(Constants.Keys.NameKey, out __result))
            {
                return false;
            }
     
            if (Game.instance != null)
            {
                __result = __instance.GetPlayerName();
                if (__result == "" || __result == "...")
                {
                    __result = Game.instance.GetPlayerProfile().GetName();
                    return false;
                }
            }
            else
            {
                var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
                var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
                if (index >= 0 && index < profiles.Count)
                {
                    __result = profiles[index].GetName();
                    return false;
                }
            }

            return true;
        }
    }
}
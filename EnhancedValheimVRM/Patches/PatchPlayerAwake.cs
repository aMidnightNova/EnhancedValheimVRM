using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class PatchPlayerAwake
    {
        // private static void Prefix(Player __instance)
        // {
        //
        // }
        private static void Postfix(Player __instance)
        {
            // this insures the name is set to the object, and can be accessed later. 
            // the reason for this is Game.instance.GetPlayerProfile().GetName() ise used to get a name
            // and this name is the current character instance name
            // so when you switch characters and teh destroy method is called and the keys are the name. its wrong 
            // this insures access to the correct player name always.
            
            __instance.m_customData.Set(Constants.Keys.PlayerName, __instance.GetPlayerDisplayName());
            Logger.Log($"________ Player({__instance.GetPlayerDisplayName()}) awake ");
            VrmController.AttachVrmToPlayer(__instance);

            // Utils.Debounce(() =>
            // {
            //     Logger.Log($"________ Player({__instance.GetPlayerDisplayName()}) ATTACH VRM TO PLAYER ");
            //
            //     VrmController.AttachVrmToPlayer(__instance);
            // }, __instance.GetPlayerDisplayName(), 0.5f);
        }
    }
}
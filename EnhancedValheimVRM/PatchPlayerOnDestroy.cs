using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Player), "OnDestroy")]
    public class PatchPlayerOnDestroy
    {
        static void Prefix(Player player)
        {
            var vrmGo = VrmController.GetVrmInstanceGameObject(player);
            
            if (vrmGo != null)
            {
                vrmGo.transform.parent = null;
            }
        }
    }
}
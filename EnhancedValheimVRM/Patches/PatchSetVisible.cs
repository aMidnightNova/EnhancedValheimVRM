using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Character), "SetVisible")]
    internal static class PatchSetVisible
    {
        private static void Postfix(Character __instance,bool visible)
        {
            if (!__instance.IsPlayer()) return;

            var player = __instance as Player;
            
            var vrmInstance = player.GetVrmInstance();
            
            if (vrmInstance != null)
            {
                var vrmGo = vrmInstance.GetGameObject();
                if (vrmGo == null)
                {
                    Logger.LogError("VrmGo Is Null SetVisible");
                    return;
                }
                var lodGroup = vrmGo.GetComponent<LODGroup>();
                if (lodGroup != null)
                {
                    if (visible)
                    {
                        lodGroup.localReferencePoint = __instance.GetField<Character, Vector3>("m_originalLocalRef");
                    }
                    else
                    {
                        lodGroup.localReferencePoint = new Vector3(999999f, 999999f, 999999f);
                    }
                }
                else
                {
                    Logger.LogError("LODGroup is null for vrmInstance");
                }
            }
            else
            {
                Logger.LogError("vrmInstance Is Null");
            }
        }
    }
}




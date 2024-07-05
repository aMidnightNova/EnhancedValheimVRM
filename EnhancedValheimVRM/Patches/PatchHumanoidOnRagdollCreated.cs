using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
    internal static class PatchHumanoidOnRagdollCreated
    {
        private static void Prefix(Humanoid __instance, Ragdoll ragdoll)
        {
            if (!__instance.IsPlayer()) return;

            var player = __instance as Player;

            var vrmInstance = player.GetVrmInstance();
            var vrmGo = vrmInstance.GetGameObject();
            if (vrmGo == null) return;

            foreach (var smr in ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.forceRenderingOff = true;
                smr.updateWhenOffscreen = true;
            }

            var ragdollAnimator = ragdoll.gameObject.AddComponent<Animator>();
            ragdollAnimator.keepAnimatorStateOnDisable = true;
            ragdollAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; 
            
            
            if (ragdollAnimator == null)
            {
                Logger.Log("____________________ ragdollAnimator Null");
            }

            var characterAnimator = player.GetField<Player, Animator>("m_animator");
            if (characterAnimator == null)
            {
                Logger.Log("____________________ characterAnimator Null");
            }
            


            ragdollAnimator.avatar = characterAnimator.avatar;

 
            vrmGo.transform.SetParent(ragdoll.transform, false);

            vrmGo.GetComponent<VrmAnimator>().Setup(player, ragdollAnimator, vrmInstance, true);
        }
    }
}
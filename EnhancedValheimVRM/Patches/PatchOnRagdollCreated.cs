using HarmonyLib;
using UnityEngine;


namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
    internal static class PatchOnRagdollCreated
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

            var characterAnimator = __instance.GetField<Character, Animator>("m_animator");

            ragdollAnimator.avatar = characterAnimator.avatar;

            ragdollAnimator.keepAnimatorStateOnDisable = true;
            ragdollAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;


            vrmGo.transform.SetParent(ragdoll.transform, false);

            vrmGo.GetComponent<VrmAnimator>().Setup(player, ragdollAnimator, vrmInstance);
        }
    }
}
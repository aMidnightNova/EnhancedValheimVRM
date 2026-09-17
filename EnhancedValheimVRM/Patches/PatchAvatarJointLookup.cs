using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using GameUtils = Utils;

namespace EnhancedValheimVRM
{
    // animation props like the forge hammer and wearables like the hip lantern only look for their attach
    // point inside Visual. those are on the vrm avatar now, so search it too when that comes up empty
    internal static class PatchAvatarJointLookup
    {
        private static Transform FindJoint(Transform root, string name, GameUtils.IterativeSearchType searchType)
        {
            var found = GameUtils.FindChild(root, name, searchType);
            if (found != null || root == null) return found;
            var player = root.GetComponentInParent<Player>();
            var avatar = player != null ? player.GetVrmInstance()?.GetGameObject() : null;
            return avatar != null ? GameUtils.FindChild(avatar.transform, name, searchType) : null;
        }

        private static IEnumerable<CodeInstruction> SwapLookup(IEnumerable<CodeInstruction> instructions,
            MethodBase method)
        {
            var original = AccessTools.Method(typeof(GameUtils),
                nameof(GameUtils.FindChild),
                new[] { typeof(Transform), typeof(string), typeof(GameUtils.IterativeSearchType) });
            var replacement = AccessTools.Method(typeof(PatchAvatarJointLookup), nameof(FindJoint));
            var swapped = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    swapped++;
                }

                yield return instruction;
            }

            if (swapped == 0)
                Logger.LogWarning("Attach point lookup not found in " + method.DeclaringType?.Name + "." + method.Name +
                    ", props on the avatar may be missing there. The game changed.");
        }

        [HarmonyPatch(typeof(AnimationEffect), nameof(AnimationEffect.Attach))]
        private static class AnimationProps
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
                MethodBase __originalMethod)
            {
                return SwapLookup(instructions, __originalMethod);
            }
        }

        [HarmonyPatch(typeof(VisEquipment), "AttachArmor")]
        private static class NamedJointWearables
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
                MethodBase __originalMethod)
            {
                return SwapLookup(instructions, __originalMethod);
            }
        }
    }
}

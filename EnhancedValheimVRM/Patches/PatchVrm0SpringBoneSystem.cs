using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UniGLTF.SpringBoneJobs;
using UniGLTF.SpringBoneJobs.Blittables;
using Unity.Mathematics;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // vrm 0.x springs have no angle limit of their own. each joints tail goes through the vrm 1.0 limit after
    // the colliders and before the spring stores it, measured from the springs own rest pose and bone axis.
    // written against univrm 0.131.2, if UpdateProcess looks different the limit is skipped
    [HarmonyPatch]
    internal static class PatchVrm0SpringBoneSystem
    {
        // every limited vrm 0.x bone of every avatar, the limit in radians
        internal static readonly Dictionary<Transform, float> Limits = new Dictionary<Transform, float>();

        // a missing method would throw inside PatchAll and take every other patch with it
        private static bool Prepare()
        {
            if (TargetMethod() != null) return true;
            Logger.LogWarning("SpringBoneMaxAngle can not limit vrm 0.x avatars with this UniVRM version");
            return false;
        }

        // TypeByName only looks in assemblies already loaded, and VRM.dll is not when the patches go in
        private static MethodBase TargetMethod()
        {
            var system = typeof(VRM.VRMSpringBone).Assembly.GetType("VRM.SpringBone.SpringBoneSystem");
            return system == null ? null : AccessTools.Method(system, "UpdateProcess");
        }

        // the tail local is passed to SpringBoneJointState.Make and then to init.WorldRotationFromTailPosition,
        // which also gets the joint transform. the clamp goes in right before the tail is loaded for Make
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var make = code.FindIndex(instruction => Calls(instruction, "Make"));
            var rotate = code.FindIndex(instruction => Calls(instruction, "WorldRotationFromTailPosition"));
            var init = rotate < 3 ? null : ((MethodInfo)code[rotate].operand).DeclaringType;
            var rest = init == null ? null : AccessTools.Field(init, "LocalRotation");
            var axis = init == null ? null : AccessTools.Field(init, "BoneAxis");
            var length = init == null ? null : AccessTools.Field(init, "Length");
            if (make < 1 || rest == null || axis == null || length == null || !code[make - 1].IsLdloc() ||
                !code[rotate - 2].IsLdloc() || !SameLocal(code[make - 1], code[rotate - 1]) ||
                (code[rotate - 3].opcode != OpCodes.Ldloca_S && code[rotate - 3].opcode != OpCodes.Ldloca))
            {
                Logger.LogWarning("SpringBoneMaxAngle can not limit vrm 0.x avatars with this UniVRM version");
                return code;
            }

            var tail = code[make - 1];
            var head = code[rotate - 2];
            var joint = code[rotate - 3];
            code.InsertRange(make - 1,
                new[]
                {
                    new CodeInstruction(head.opcode, head.operand), new CodeInstruction(tail.opcode, tail.operand),
                    new CodeInstruction(joint.opcode, joint.operand), new CodeInstruction(OpCodes.Ldfld, rest),
                    new CodeInstruction(joint.opcode, joint.operand), new CodeInstruction(OpCodes.Ldfld, axis),
                    new CodeInstruction(joint.opcode, joint.operand), new CodeInstruction(OpCodes.Ldfld, length),
                    CodeInstruction.Call(typeof(PatchVrm0SpringBoneSystem), nameof(Clamp)), Store(tail)
                });
            return code;
        }

        private static bool SameLocal(CodeInstruction a, CodeInstruction b)
        {
            return a.opcode == b.opcode && Equals(LocalIndex(a.operand), LocalIndex(b.operand));
        }

        private static object LocalIndex(object operand)
        {
            return operand is LocalBuilder local ? local.LocalIndex : operand;
        }

        // the stloc that matches a ldloc, same local
        private static CodeInstruction Store(CodeInstruction load)
        {
            if (load.opcode == OpCodes.Ldloc_0) return new CodeInstruction(OpCodes.Stloc_0);
            if (load.opcode == OpCodes.Ldloc_1) return new CodeInstruction(OpCodes.Stloc_1);
            if (load.opcode == OpCodes.Ldloc_2) return new CodeInstruction(OpCodes.Stloc_2);
            if (load.opcode == OpCodes.Ldloc_3) return new CodeInstruction(OpCodes.Stloc_3);
            return new CodeInstruction(load.opcode == OpCodes.Ldloc_S ? OpCodes.Stloc_S : OpCodes.Stloc, load.operand);
        }

        private static bool Calls(CodeInstruction instruction, string method)
        {
            return instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo called &&
                called.Name == method;
        }

        private static Vector3 Clamp(Transform head, Vector3 tail, Quaternion rest, Vector3 axis, float length)
        {
            if (Limits.Count == 0 || !Limits.TryGetValue(head, out var radians)) return tail;
            var parent = head.parent;
            var position = head.position;
            var logic = new BlittableJointImmutable(length: length, localRotation: rest, boneAxis: axis);
            var joint = new BlittableJointMutable(angleLimitType: (float)AnglelimitTypes.Cone,
                angleLimit1: radians,
                angleLimitOffset: quaternion.identity);
            return Anglelimit.Apply(logic,
                joint,
                parent != null ? parent.rotation : Quaternion.identity,
                position,
                tail);
        }
    }
}

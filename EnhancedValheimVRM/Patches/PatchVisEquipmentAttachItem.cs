using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    internal static class PatchVisEquipmentAttachItem
    {
        // this is basically the entire method from the game. we only want to do one thing in here, and thats stop bones from being transferred.
        private static bool Prefix(
            VisEquipment __instance,
            int itemHash,
            int variant,
            Transform joint,
            ref GameObject __result,
            bool enableEquipEffects = true,
            bool backAttach = false
        )
        {
            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemHash);
            if (itemPrefab == null)
            {
                ZLog.Log(("Missing attach item: " + itemHash + "  ob:" + __instance.gameObject.name + "  joint:" +
                          ((bool)(Object)joint ? joint.name : "none")));
                __result = null;
                return false;
            }

            GameObject original = null;
            int childCount = itemPrefab.transform.childCount;
            for (int index = 0; index < childCount; ++index)
            {
                Transform child = itemPrefab.transform.GetChild(index);
                if (backAttach && child.gameObject.name == "attach_back")
                {
                    original = child.gameObject;
                    break;
                }

                if (child.gameObject.name == "attach" || !backAttach && child.gameObject.name == "attach_skin")
                {
                    original = child.gameObject;
                    break;
                }
            }

            if (original == null)
            {
                __result = null;
                return false;
            }

            GameObject instance = UnityEngine.Object.Instantiate(original);
            instance.SetActive(true);

            __instance.InvokePrivateMethod("CleanupInstance", instance);

            if (enableEquipEffects)
            {
                __instance.InvokePrivateMethod("EnableEquippedEffects", instance);
            }

            if (original.name == "attach_skin")
            {
                instance.transform.SetParent(__instance.m_bodyModel.transform.parent);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                foreach (SkinnedMeshRenderer componentsInChild in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // stop the sharing of bones from the player model to the item instance.
                    if (VrmAnimator.RiggedItemNamesOther.Contains(componentsInChild.name))
                    {
                        continue;
                    }

                    componentsInChild.rootBone = __instance.m_bodyModel.rootBone;
                    componentsInChild.bones = __instance.m_bodyModel.bones;
                }
            }
            else
            {
                instance.transform.SetParent(joint);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }

            Type iEquipmentVisualType = Type.GetType("Namespace.IEquipmentVisual, AssemblyName");
            if (iEquipmentVisualType != null)
            {
                MethodInfo setupMethod = iEquipmentVisualType.GetMethod("Setup");
                if (setupMethod != null)
                {
                    var visualComponent = instance.GetComponentInChildren(iEquipmentVisualType);
                    if (visualComponent != null)
                    {
                        setupMethod.Invoke(visualComponent, new object[] { variant });
                    }
                }
            }

            __result = instance;
            return false;
        }

        //
        //     private static void Postfix(
        //         VisEquipment __instance,
        //         int itemHash,
        //         int variant,
        //         Transform joint,
        //         bool enableEquipEffects,
        //         bool backAttach,
        //         ref GameObject __result)
        //     {
        //         return;
        //
        //         if (!__instance.m_isPlayer || !__instance.TryGetComponent(out Player player))
        //         {
        //             Logger.Log("VisEquipment instance is not a player, exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         Logger.Log("Player component found", Logger.LogLevel.Info);
        //         
        //         Logger.Log("AttachItem Postfix called", Logger.LogLevel.Debug);
        //
        //         if (__result == null)
        //         {
        //             Logger.Log("Result is null, exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemHash);
        //         if (itemPrefab == null)
        //         {
        //             Logger.Log($"Missing attach item: {itemHash}  ob: {__instance.gameObject.name}  joint: {(joint != null ? joint.name : "none")}", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         GameObject original = null;
        //         int childCount = itemPrefab.transform.childCount;
        //         for (int index = 0; index < childCount; ++index)
        //         {
        //             Transform child = itemPrefab.transform.GetChild(index);
        //             if (backAttach && child.gameObject.name == "attach_back")
        //             {
        //                 original = child.gameObject;
        //                 break;
        //             }
        //
        //             if (child.gameObject.name == "attach" || !backAttach && child.gameObject.name == "attach_skin")
        //             {
        //                 original = child.gameObject;
        //                 break;
        //             }
        //         }
        //
        //         if (original == null)
        //         {
        //             Logger.Log("Original object is null, exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         Logger.Log($"Original object name: {original.name}", Logger.LogLevel.Info);
        //         if (original.name != "attach_skin")
        //         {
        //             Logger.Log("Original name is not 'attach_skin', exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //
        //
        //         var vrmInstance = player.GetVrmInstance();
        //         var vrmGo = vrmInstance.GetGameObject();
        //         var boneTransformer = vrmInstance.GetBoneTransformer();
        //         if (vrmGo == null)
        //         {
        //             Logger.Log("VRM GameObject is null, exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         Logger.Log("VRM GameObject obtained", Logger.LogLevel.Info);
        //
        //
        //
        //         var instanceSmrs = __result.GetComponentsInChildren<SkinnedMeshRenderer>();
        //         var vrmSmrs = vrmGo.GetComponentsInChildren<SkinnedMeshRenderer>();
        //
        //         Logger.Log($"instanceSmr SkinnedMeshRenderers found: {instanceSmrs.Length}", Logger.LogLevel.Debug);
        //         Logger.Log($"VRM SkinnedMeshRenderers found: {vrmSmrs.Length}", Logger.LogLevel.Debug);
        //
        //         SkinnedMeshRenderer vrmSmrBody = vrmSmrs.FirstOrDefault(smr => smr.name == "Body");
        //         if (vrmSmrBody == null)
        //         {
        //             Logger.Log("VRM Body SkinnedMeshRenderer not found, exiting", Logger.LogLevel.Debug);
        //             return;
        //         }
        //
        //         Logger.Log("VRM Body SkinnedMeshRenderer found", Logger.LogLevel.Info);
        //
        //         // Clone and map the bones
        //         var vrmAnimator  = vrmInstance.GetAnimator();
        //         __result.transform.SetParent(vrmGo.transform);
        //
        //         var instanceAnimator = __result.GetComponentInChildren<Animator>();
        //         
        //         foreach (SkinnedMeshRenderer smr in instanceSmrs)
        //         {
        //             Logger.Log($"Mapping bones for SkinnedMeshRenderer: {smr.name}", Logger.LogLevel.Debug);
        //             
        //             var rootBoot = boneTransformer.CloneVrmBoneWithPlayerBoneInfo(vrmSmrBody.rootBone);
        //   
        //             
        //             if (rootBoot !=  null)
        //             {
        //                 smr.rootBone = rootBoot;
        //                 Logger.Log("Root bone set", Logger.LogLevel.Debug);
        //             }
        //             //smr.rootBone = vrmSmrBody.rootBone;
        //             Logger.Log("Root bone set", Logger.LogLevel.Debug);
        //
        //
        //             // Clone and map the bones using the provided function
        //             smr.bones = boneTransformer.CloneVrmBonesWithPlayerBoneInfo(vrmSmrBody.bones);
        //             //smr.bones = vrmSmrBody.bones;
        //             Logger.Log("Bones cloned and mapped", Logger.LogLevel.Debug);
        //         }
        //         instanceAnimator.Rebind();
        //
        //         Logger.Log("AttachItem Postfix completed", Logger.LogLevel.Debug);
        //     }
    }
}
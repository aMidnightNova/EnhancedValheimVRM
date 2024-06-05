using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class PatchVisEquipmentUpdateLodgroup
    {
        private static void Postfix(VisEquipment __instance)
        {
            if (!__instance.m_isPlayer || !__instance.TryGetComponent(out Player player)) return;

            Logger.LogWarning("Update LOG GROUP");

            var vrmInstance = player.GetVrmInstance();
            if (vrmInstance == null) return;

            var settings = vrmInstance.GetSettings();
            var vrmAnimator = player.GetVrmGoAnimator();

            TryHandleItemInstance(__instance, "m_hairItemInstance", vrmAnimator, settings);
            TryHandleItemInstance(__instance, "m_beardItemInstance", vrmAnimator, settings);
            
            TryHandleItemList(__instance, "m_chestItemInstances", settings.ChestVisible);
            TryHandleItemList(__instance, "m_legItemInstances", settings.LegsVisible);
            TryHandleItemList(__instance, "m_shoulderItemInstances", settings.ShouldersVisible);
            TryHandleItemList(__instance, "m_utilityItemInstances", settings.UtilityVisible);
            
            TryHandleHelmet(__instance, "m_helmetItemInstance", vrmAnimator, settings);
            TryHandleLeftItem(__instance, "m_leftItemInstance", vrmAnimator, settings);
            TryHandleRightItem(__instance, "m_rightItemInstance", vrmAnimator, settings);
            TryHandleRightBackItem(__instance, "m_rightBackItemInstance", vrmAnimator, settings);
            TryHandleLeftBackItem(__instance, "m_leftBackItemInstance", vrmAnimator, settings);
        }

        private static void TryHandleItemInstance(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                // gets the field like so m_hairItem~~Instance~~ -> m_hairItem
                var itemName = instance.GetFieldValue<FieldInfo>(fieldName.Replace("Instance", ""))?.GetValue(instance) as string;
                Logger.Log($"Handling item: {fieldName}, Item Name: {itemName}");
                HandleItemInstance(go, vrmAnimator, false, false);
            }
        }

        private static void TryHandleItemList(VisEquipment instance, string fieldName, bool visible)
        {
            if (instance.TryGetField<VisEquipment, List<GameObject>>(fieldName, out var goList))
            {
                var itemName = instance.GetFieldValue<FieldInfo>(fieldName.Replace("Instances", ""))?.GetValue(instance) as string;
                Logger.Log($"Handling item: {fieldName}, Item Name: {itemName}");
                HandleItemInstanceList(goList, visible);
            }
        }

        private static void TryHandleHelmet(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_helmetItem")?.GetValue(instance) as string;
                Logger.Log($"Handling helmet: {fieldName}, Item Name: {itemName}");
                HandleHelmet(go, vrmAnimator, settings);
            }
        }

        private static void TryHandleLeftItem(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_leftItem")?.GetValue(instance) as string;
                Logger.Log($"Handling left item: {fieldName}, Item Name: {itemName}");
                HandleLeftItem(go, vrmAnimator, settings);
            }
        }

        private static void TryHandleRightItem(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_rightItem")?.GetValue(instance) as string;
                Logger.Log($"Handling right item: {fieldName}, Item Name: {itemName}");

                
                if (itemName == "KnifeSkollAndHati")
                {
                    // Animator animator = go.GetComponent<Animator>();
                    // if (animator != null)
                    // {
                    //     animator.enabled = false;
                    //     Logger.Log("KnifeSkollAndHati Animator Found");
                    // }
                    // else
                    // {
                    //     Logger.Log("KnifeSkollAndHati Animator Not Found");
                    // }
                    //
                    //
                    // SkinnedMeshRenderer smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    // if (smr != null)
                    // {
                    //     if (smr.rootBone != null)
                    //     {
                    //         smr.rootBone.localPosition = settings.RightHandItemPos;
                    //         smr.rootBone.localScale = (Vector3.one * settings.EquipmentScale);;
                    //     }
                    // }
                }
                else
                {
                    HandleRightItem(go, vrmAnimator, settings);
                }

            }
        }

        private static void TryHandleRightBackItem(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_rightBackItem")?.GetValue(instance) as string;
                Logger.Log($"Handling right back item: {fieldName}, Item Name: {itemName}");
                HandleRightBackItem(go, instance, vrmAnimator, settings);
            }
        }

        private static void TryHandleLeftBackItem(VisEquipment instance, string fieldName, Animator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_leftBackItem")?.GetValue(instance) as string;
                Logger.Log($"Handling left back item: {fieldName}, Item Name: {itemName}");
                HandleLeftBackItem(go, instance, vrmAnimator, settings);
            }
        }

        private static void HandleItemInstance(GameObject go, Animator vrmAnimator, bool visible, bool attachToBone)
        {
            if (!visible)
            {
                go.SetActive(false);
            }
            else if (attachToBone)
            {
                //go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Head), false);
            }
        }

        private static void HandleItemInstanceList(List<GameObject> itemList, bool visible)
        {
            if (!visible)
            {
                itemList.ForEach(item => item.SetActive(false));
            }
        }

        private static void HandleHelmet(GameObject go, Animator vrmAnimator, VrmSettings settings)
        {
            if (!settings.HelmetVisible)
            {
                go.SetActive(false);
            }
            else
            {
               // go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Head), false);
                go.transform.localScale = settings.HelmetScale;
                go.transform.localPosition = settings.HelmetOffset;
            }
        }

        private static void HandleLeftItem(GameObject go, Animator vrmAnimator, VrmSettings settings)
        {
           // go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.LeftHand), false);
            go.transform.localPosition = settings.LeftHandItemPos;
            go.transform.localScale = Vector3.one * settings.EquipmentScale;
        }

        private static void HandleRightItem(GameObject go, Animator vrmAnimator, VrmSettings settings)
        {
           // go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.RightHand), false);
            go.transform.localPosition = settings.RightHandItemPos;
            go.transform.localScale = Vector3.one * settings.EquipmentScale;
        }

        private static void HandleRightBackItem(GameObject go, VisEquipment instance, Animator vrmAnimator, VrmSettings settings)
        {
            var rightBackName = instance.GetFieldValue<FieldInfo>("m_rightBackItem")?.GetValue(instance) as string;
 
            Vector3 offset = Vector3.zero;

            if (rightBackName?.StartsWith("Knife") == true)
            {
                //go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Hips), false);
                offset = settings.KnifeSidePos;
                go.transform.Rotate(settings.KnifeSideRot);
            }
            else if (rightBackName?.StartsWith("Staff") == true)
            {
               // go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Chest), false);
                offset = settings.StaffPos;
                go.transform.Rotate(settings.StaffRot);
            }
            else
            {
                //go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Chest), false);
                offset = go.transform.parent == instance.m_backTool
                    ? settings.RightHandBackItemToolPos
                    : settings.RightHandBackItemPos;
            }

            go.transform.localPosition = offset;
            go.transform.localScale = Vector3.one * settings.EquipmentScale;
        }

        private static void HandleLeftBackItem(GameObject go, VisEquipment instance, Animator vrmAnimator, VrmSettings settings)
        {
            var leftBackName = instance.GetFieldValue<FieldInfo>("m_leftBackItem")?.GetValue(instance) as string;

            if (leftBackName?.StartsWith("Bow") == true)
            {
                go.transform.localPosition = settings.BowBackPos;
            }
            else if (leftBackName?.StartsWith("StaffSkeleton") == true)
            {
                go.transform.localPosition = settings.StaffSkeletonPos;
            }
            else
            {
                go.transform.localPosition = settings.LeftHandBackItemPos;
            }

            // go.transform.SetParent(vrmAnimator.GetBoneTransform(HumanBodyBones.Chest), false);
            go.transform.localScale = Vector3.one * settings.EquipmentScale;
        }
    }
}

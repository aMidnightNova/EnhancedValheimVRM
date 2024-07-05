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
            var vrmGoAnimator = player.GetVrmGoAnimator();
            var vrmAnimator = player.GetVrmAnimator();

            TryHandleItemInstance(__instance, "m_hairItemInstance", vrmGoAnimator, settings);
            TryHandleItemInstance(__instance, "m_beardItemInstance", vrmGoAnimator, settings);

            TryHandleItemList(__instance, "m_chestItemInstances", settings.ChestVisible);
            TryHandleItemList(__instance, "m_legItemInstances", settings.LegsVisible);
            TryHandleItemList(__instance, "m_shoulderItemInstances", settings.ShouldersVisible);
            TryHandleItemList(__instance, "m_utilityItemInstances", settings.UtilityVisible);

            TryHandleHelmet(__instance, "m_helmetItemInstance", vrmGoAnimator, settings);
            TryHandleLeftItem(__instance, "m_leftItemInstance", vrmGoAnimator, vrmAnimator, settings);
            TryHandleRightItem(__instance, "m_rightItemInstance", vrmGoAnimator, vrmAnimator, settings);
            TryHandleRightBackItem(__instance, "m_rightBackItemInstance", vrmGoAnimator, vrmAnimator, settings);
            TryHandleLeftBackItem(__instance, "m_leftBackItemInstance", vrmGoAnimator, vrmAnimator, settings);
        }

        private static void TryHandleItemInstance(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                // gets the field like so m_hairItem~~Instance~~ -> m_hairItem
                var itemName = instance.GetFieldValue<FieldInfo>(fieldName.Replace("Instance", ""))?.GetValue(instance) as string;
                Logger.Log($"Handling item: {fieldName}, Item Name: {itemName}");
                HandleItemInstance(go, vrmGoAnimator, false, false);
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

        private static void TryHandleHelmet(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_helmetItem")?.GetValue(instance) as string;
                Logger.Log($"Handling helmet: {fieldName}, Item Name: {itemName}");
                HandleHelmet(go, vrmGoAnimator, settings);
            }
        }

        private static void TryHandleLeftItem(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmAnimator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_leftItem")?.GetValue(instance) as string;


                if (vrmAnimator != null)
                {
                    vrmAnimator.LeftHandItemInstance = go;
                    vrmAnimator.LeftHandItemInstanceName = itemName;
                }


                Logger.Log($"Handling left item: {fieldName}, Item Name: {itemName}");

                // go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.LeftHand), false);
                go.transform.localPosition = settings.LeftHandItemPos;
                go.transform.localScale = settings.PlayerVrmScaleVector3;

                //go.transform.localScale = Vector3.one;
            }
        }

        private static void TryHandleRightItem(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmAnimator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_rightItem")?.GetValue(instance) as string;
                Logger.Log($"Handling right item: {fieldName}, Item Name: {itemName}");

                if (vrmAnimator != null)
                {
                    Logger.Log($"______________________________ potato");
                    vrmAnimator.RightHandItemInstance = go;
                    vrmAnimator.RightHandItemInstanceName = itemName;
                }
                else
                {
                    Logger.Log($"______________________________ vrmAnimator NULL");

                }


                if (GameItem.IsSpecialCase(itemName))
                {
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        if (smr.rootBone != null)
                        {
                            // var leftHandBoneName = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.LeftHand);
                            // var rightHandBoneName = BoneTransformer.MapHumanBodyBoneToPlayerBoneName(HumanBodyBones.RightHand);
                            //
                            // var left = BoneTransformer.FindBoneInHierarchy(go.transform, leftHandBoneName);
                            // var right = BoneTransformer.FindBoneInHierarchy(go.transform, rightHandBoneName);
                            //
                            // left.localScale = settings.PlayerVrmScaleVector3;
                            // right.localScale = settings.PlayerVrmScaleVector3;
                            // //smr.rootBone.localPosition = Vector3.zero;
                            // //smr.rootBone.localPosition = settings.RightHandItemPos;
                            smr.rootBone.localScale = settings.PlayerVrmScaleVector3;
                        }
                    }
                }
                else
                {
                    // go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.RightHand), false);
                    go.transform.localScale = settings.PlayerVrmScaleVector3;
                    //go.transform.localScale = Vector3.one;
                }
                go.transform.localPosition = settings.RightHandItemPos;
            }
        }

        private static void TryHandleRightBackItem(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmAnimator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_rightBackItem")?.GetValue(instance) as string;
                Logger.Log($"Handling right back item: {fieldName}, Item Name: {itemName}");

                if (vrmAnimator != null)
                {
                    vrmAnimator.RightHandBackItemInstance = go;
                    vrmAnimator.RightHandBackItemInstanceName = itemName;
                }

                HandleRightBackItem(go, instance, vrmGoAnimator, settings);
            }
        }

        private static void TryHandleLeftBackItem(VisEquipment instance, string fieldName, Animator vrmGoAnimator, VrmAnimator vrmAnimator, VrmSettings settings)
        {
            if (instance.TryGetField<VisEquipment, GameObject>(fieldName, out var go))
            {
                var itemName = instance.GetFieldValue<FieldInfo>("m_leftBackItem")?.GetValue(instance) as string;
                Logger.Log($"Handling left back item: {fieldName}, Item Name: {itemName}");

                if (vrmAnimator != null)
                {
                    vrmAnimator.LeftHandBackItemInstance = go;
                    vrmAnimator.LeftHandBackItemInstanceName = itemName;
                }


                HandleLeftBackItem(go, instance, vrmGoAnimator, settings);
            }
        }

        private static void HandleItemInstance(GameObject go, Animator vrmGoAnimator, bool visible, bool attachToBone)
        {
            if (!visible)
            {
                go.SetActive(false);
            }
            else if (attachToBone)
            {
                //go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head), false);
            }
        }

        private static void HandleItemInstanceList(List<GameObject> itemList, bool visible)
        {
            if (!visible)
            {
                itemList.ForEach(item => item.SetActive(false));
            }
        }

        private static void HandleHelmet(GameObject go, Animator vrmGoAnimator, VrmSettings settings)
        {
            if (!settings.HelmetVisible)
            {
                go.SetActive(false);
            }
            else
            {
                // go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Head), false);
                go.transform.localPosition = settings.HelmetOffset;
                //go.transform.localScale = settings.HelmetScale;
                go.transform.localScale = Vector3.Scale(Vector3.one, settings.HelmetScale);
            }
        }


        private static void HandleRightBackItem(GameObject go, VisEquipment instance, Animator vrmGoAnimator, VrmSettings settings)
        {
            var rightBackName = instance.GetFieldValue<FieldInfo>("m_rightBackItem")?.GetValue(instance) as string;

            Vector3 offset = Vector3.zero;

            if (rightBackName?.StartsWith("Knife") == true)
            {
                //go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Hips), false);
                offset = settings.KnifeSidePos;
                go.transform.Rotate(settings.KnifeSideRot);
            }
            else if (rightBackName?.StartsWith("Staff") == true)
            {
                // go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), false);
                offset = settings.StaffPos;
                go.transform.Rotate(settings.StaffRot);
            }
            else
            {
                //go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), false);
                offset = go.transform.parent == instance.m_backTool
                    ? settings.RightHandBackItemToolPos
                    : settings.RightHandBackItemPos;
            }

            go.transform.localPosition = offset;
            //go.transform.localScale = settings.PlayerVrmScaleVector3;
            go.transform.localScale = Vector3.one;
        }

        private static void HandleLeftBackItem(GameObject go, VisEquipment instance, Animator vrmGoAnimator, VrmSettings settings)
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

            // go.transform.SetParent(vrmGoAnimator.GetBoneTransform(HumanBodyBones.Chest), false);

            //go.transform.localScale = settings.PlayerVrmScaleVector3;
            go.transform.localScale = Vector3.one;
        }
    }
}
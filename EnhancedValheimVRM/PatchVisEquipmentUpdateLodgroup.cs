using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    internal static class PatchVisEquipmentUpdateLodgroup
    {
        private static void Postfix(VisEquipment __instance)
        {
            if (!__instance.m_isPlayer) return;


            Player player;
            if (!__instance.TryGetComponent(out player))
            {
                return;
            }


            var vrmInstance = player.GetVrmInstance();
            
            if(vrmInstance == null) return;
            
            var settings = vrmInstance.GetSettings();


            if (__instance.TryGetField<VisEquipment, GameObject>("m_hairItemInstance", out var hair))
            {
                hair.SetVisible(false);
            }


            if (__instance.TryGetField<VisEquipment, GameObject>("m_beardItemInstance", out var beard))
            {
                beard.SetVisible(false);
            }


            if (__instance.TryGetField<VisEquipment, List<GameObject>>("m_chestItemInstances", out var chestList))
            {
                //TODO: settings.ChestVisible can probably be out outside of the if
                if (!settings.ChestVisible)
                {
                    foreach (var chest in chestList) chest.SetVisible(false);
                }
            }


            if (__instance.TryGetField<VisEquipment, List<GameObject>>("m_legItemInstances", out var legList))
            {
                if (!settings.LegsVisible)
                {
                    foreach (var leg in legList) leg.SetVisible(false);
                }
            }


            if (__instance.TryGetField<VisEquipment, List<GameObject>>("m_shoulderItemInstances", out var shoulderList))
            {
                if (!settings.ShouldersVisible)
                {
                    foreach (var shoulder in shoulderList) shoulder.SetVisible(false);
                }
            }

            if (__instance.TryGetField<VisEquipment, List<GameObject>>("m_utilityItemInstances", out var utilityList))
            {
                if (!settings.UtilityVisible)
                {
                    foreach (var utility in utilityList) utility.SetVisible(false);
                }
            }


            if (__instance.TryGetField<VisEquipment, GameObject>("m_helmetItemInstance", out var helmet))
            {
                if (!settings.HelmetVisible)
                {
                    helmet.SetVisible(false);
                }
                else
                {
                    helmet.transform.localScale = settings.HelmetScale;
                    helmet.transform.localPosition = settings.HelmetOffset;
                }
            }

            float equipmentScale = settings.EquipmentScale;
            Vector3 equipmentScaleVector = new Vector3(equipmentScale, equipmentScale, equipmentScale);


            if (__instance.TryGetField<VisEquipment, GameObject>("m_leftItemInstance", out var leftItem))
            {
                leftItem.transform.localPosition = settings.LeftHandItemPos;
                leftItem.transform.localScale = equipmentScaleVector;
            }


            if (__instance.TryGetField<VisEquipment, GameObject>("m_rightItemInstance", out var rightItem))
            {
                rightItem.transform.localPosition = settings.RightHandItemPos;
                rightItem.transform.localScale = equipmentScaleVector;
            }

            // divided  by 100 to keep the settings file positions in the same number range. (position offset appears to be on the world, not local)

            if (__instance.TryGetField<VisEquipment, GameObject>("m_rightBackItemInstance", out var rightBackItem))
            {
                var rightBackName = __instance.GetFieldValue<VisEquipment>("m_rightBackItem");

                var isKnife = rightBackName.ToString().Substring(0, 5) == "Knife";
                var isStaff = rightBackName.ToString().Substring(0, 5) == "Staff";

                Vector3 offset = Vector3.zero;

                if (isKnife)
                {
                    offset = settings.KnifeSidePos;
                    rightBackItem.transform.Rotate(settings.KnifeSideRot);
                }
                else if (isStaff)
                {
                    offset = settings.StaffPos;
                    rightBackItem.transform.Rotate(settings.StaffRot);
                }
                else
                {
                    offset = rightBackItem.transform.parent == __instance.m_backTool
                        ? settings.RightHandBackItemToolPos
                        : settings.RightHandBackItemPos;
                }

                rightBackItem.transform.localPosition = offset / 100.0f;
                rightBackItem.transform.localScale = equipmentScaleVector / 100.0f;
            }


            if (__instance.TryGetField<VisEquipment, GameObject>("m_leftBackItemInstance", out var leftBackItem))
            {
                var leftBackName = __instance.GetFieldValue<VisEquipment>("m_leftBackItem");

                var isBow = leftBackName.ToString().Substring(0, 3) == "Bow";
                var isStaffSkeleton = leftBackName.ToString() == "StaffSkeleton";
                if (isBow)
                {
                    leftBackItem.transform.localPosition = settings.BowBackPos / 100.0f;
                }
                else if (isStaffSkeleton)
                {
                    leftBackItem.transform.localPosition = settings.StaffSkeletonPos / 100.0f;
                }
                else
                {
                    leftBackItem.transform.localPosition = settings.LeftHandBackItemPos / 100.0f;
                }

                leftBackItem.transform.localScale = equipmentScaleVector / 100.0f;
            }
        }
    }
}
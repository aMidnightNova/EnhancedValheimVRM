using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
    static class PatchVisEquipmentUpdateLodgroup
    {
        static void Postfix(VisEquipment __instance)
        {
            if (!__instance.m_isPlayer) return;
            var player = __instance.GetComponent<Player>();


            if (player == null) return;

            var vrmInstance = VrmController.GetVrmInstance(player);

            var settings = vrmInstance.GetSettings();


            var hair = __instance.GetField<VisEquipment, GameObject>("m_hairItemInstance");

            if (hair != null) hair.SetVisible(false);

            var beard = __instance.GetField<VisEquipment, GameObject>("m_beardItemInstance");

            if (beard != null) beard.SetVisible(false);

            var chestList = __instance.GetField<VisEquipment, List<GameObject>>("m_chestItemInstances");

            if (chestList != null)
            {
                if (!settings.ChestVisible)
                {
                    foreach (var chest in chestList) chest.SetVisible(false);
                }
            }

            var legList = __instance.GetField<VisEquipment, List<GameObject>>("m_legItemInstances");
            if (legList != null)
            {
                if (!settings.LegsVisible)
                {
                    foreach (var leg in legList) leg.SetVisible(false);
                }
            }

            var shoulderList = __instance.GetField<VisEquipment, List<GameObject>>("m_shoulderItemInstances");

            if (shoulderList != null)
            {
                if (shoulderList != null)
                {
                    if (!settings.ShouldersVisible)
                    {
                        foreach (var shoulder in shoulderList) shoulder.SetVisible(false);
                    }
                }
            }

            var utilityList = __instance.GetField<VisEquipment, List<GameObject>>("m_utilityItemInstances");
            if (utilityList != null)
            {
                if (!settings.UtilityVisible)
                {
                    foreach (var utility in utilityList) utility.SetVisible(false);
                }
            }

            var helmet = __instance.GetField<VisEquipment, GameObject>("m_helmetItemInstance");
            if (helmet != null)
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

            // 武器位置合わせ
            float equipmentScale = settings.EquipmentScale;
            Vector3 equipmentScaleVector = new Vector3(equipmentScale, equipmentScale, equipmentScale);

            var leftItem = __instance.GetField<VisEquipment, GameObject>("m_leftItemInstance");
            if (leftItem != null)
            {
                leftItem.transform.localPosition = settings.LeftHandItemPos;
                leftItem.transform.localScale = equipmentScaleVector;
            }

            var rightItem = __instance.GetField<VisEquipment, GameObject>("m_rightItemInstance");
            if (rightItem != null)
            {
                rightItem.transform.localPosition = settings.RightHandItemPos;
                rightItem.transform.localScale = equipmentScaleVector;
            }

            // divided  by 100 to keep the settings file positions in the same number range. (position offset appears to be on the world, not local)
            var rightBackItem = __instance.GetField<VisEquipment, GameObject>("m_rightBackItemInstance");
            if (rightBackItem != null)
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
                    offset = rightBackItem.transform.parent == __instance.m_backTool ? settings.RightHandBackItemToolPos : settings.RightHandBackItemPos;
                }

                rightBackItem.transform.localPosition = offset / 100.0f;
                rightBackItem.transform.localScale = equipmentScaleVector / 100.0f;
            }

            var leftBackItem = __instance.GetField<VisEquipment, GameObject>("m_leftBackItemInstance");
            if (leftBackItem != null)
            {
                var leftBackName = __instance.GetFieldValue<VisEquipment>("m_leftBackItem");
                //Debug.Log(leftBackName.ToString());

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
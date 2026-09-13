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
            if (!__instance.m_isPlayer || !__instance.TryGetComponent<Player>(out var player)) return;
            Apply(__instance, player.GetVrmInstance());
        }

        internal static void Apply(VisEquipment equipment, VrmInstance vrm)
        {
            if (vrm == null || vrm.GetGameObject() == null) return;
            // Staging is already loaded here; rollback restores this flag if setup fails.
            if (vrm.IsReady && equipment.m_bodyModel != null)
            {
                equipment.m_bodyModel.forceRenderingOff = true;
                equipment.m_bodyModel.updateWhenOffscreen = true;
            }

            var settings = vrm.GetSettings();
            foreach (string field in new[] { "m_hairItemInstance", "m_beardItemInstance" })
                if (equipment.TryGetField<VisEquipment, GameObject>(field, out var hair))
                    hair.SetActive(false);
            SetListVisible(equipment, "m_chestItemInstances", settings.ChestVisible);
            SetListVisible(equipment, "m_legItemInstances", settings.LegsVisible);
            SetListVisible(equipment, "m_shoulderItemInstances", settings.ShouldersVisible);
            SetListVisible(equipment, "m_utilityItemInstances", settings.UtilityVisible);
            SetListVisible(equipment, "m_trinketItemInstances", settings.TrinketVisible);
            if (equipment.TryGetField<VisEquipment, GameObject>("m_helmetItemInstance", out var helmet))
            {
                helmet.SetActive(settings.HelmetVisible);
                if (settings.HelmetVisible)
                {
                    EquipmentTransformReference.Get(helmet.transform)
                        .SetPositionOffset(settings.HelmetOffset, settings.PlayerVrmScale);
                    EquipmentTransformReference.Get(helmet.transform)
                        .SetScale(settings.PlayerVrmScale, settings.HelmetScale);
                }
            }

            SetHand(equipment, "m_leftItem", settings.LeftHandItemPos, settings.LeftHandItemRot, settings);
            SetHand(equipment, "m_rightItem", settings.RightHandItemPos, settings.RightHandItemRot, settings);
            SetBack(equipment, "m_rightBackItem", true, settings);
            SetBack(equipment, "m_leftBackItem", false, settings);
            // Refresh both slots together, including nulls after unequipping.
            vrm.GetGameObject().GetComponent<VrmAnimator>()?.StartupGetItems();
        }

        private static void SetListVisible(VisEquipment equipment, string field, bool visible)
        {
            if (!equipment.TryGetField<VisEquipment, List<GameObject>>(field, out var items)) return;
            foreach (var item in items)
                if (item != null)
                    item.SetActive(visible);
        }

        private static void SetHand(VisEquipment equipment,
            string field,
            Vector3 offset,
            Vector3 rotation,
            VrmSettings settings)
        {
            if (!equipment.TryGetField<VisEquipment, GameObject>(field + "Instance", out var item)) return;
            string name = equipment.GetEquippedItemName(field);
            if (settings.TryGetItemAdjustment(name, GameItem.ClassOf(name), true, out var itemPos, out var itemRot))
            {
                offset += itemPos;
                rotation += itemRot;
            }

            var reference = EquipmentTransformReference.Get(item.transform);
            reference.SetRotationOffset(rotation);
            reference.SetPositionOffset(offset, settings.PlayerVrmScale);
            reference.SetScale(settings.PlayerVrmScale * settings.GetWeaponScale(name));
        }

        private static void SetBack(VisEquipment equipment, string field, bool right, VrmSettings settings)
        {
            if (!equipment.TryGetField<VisEquipment, GameObject>(field + "Instance", out var item)) return;
            var reference = EquipmentTransformReference.Get(item.transform);
            Vector3 offset = right ? settings.RightHandBackItemPos : settings.LeftHandBackItemPos;
            Vector3 rotation = right ? settings.RightHandBackItemRot : settings.LeftHandBackItemRot;
            string name = equipment.GetEquippedItemName(field);
            if (settings.TryGetItemAdjustment(name, GameItem.ClassOf(name), false, out var itemPos, out var itemRot))
            {
                offset += itemPos;
                rotation += itemRot;
            }

            reference.SetRotationOffset(rotation);
            reference.SetPositionOffset(offset, settings.PlayerVrmScale);
            reference.SetScale(settings.PlayerVrmScale * settings.GetWeaponScale(name));
        }

        internal static void RecreateEquipment(VisEquipment equipment)
        {
            // Recreate any rig that was attached before the VRM became ready, and
            // rebuild skinned clothing whose renderer visibility was previously hidden.
            foreach (string slot in new[]
                     {
                         "LeftItem", "RightItem", "HelmetItem", "ChestItem", "LegItem", "ShoulderItem",
                         "UtilityItem", "TrinketItem", "LeftBackItem", "RightBackItem"
                     })
                AccessTools.Field(typeof(VisEquipment), "m_current" + slot + "Hash")?.SetValue(equipment, int.MinValue);
            equipment.InvokePrivateMethod("UpdateEquipmentVisuals");
        }
    }
}

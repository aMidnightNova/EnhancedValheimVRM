using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            // the game hides the players lod group within 2 m of the camera, with fade off items stay drawn
            if (!settings.EnablePlayerFade &&
                equipment.TryGetField<VisEquipment, LODGroup>("m_lodGroup", out var group) && group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0 && lods[0].renderers.Length > 0)
                {
                    lods[0].renderers = new Renderer[0];
                    group.SetLODs(lods);
                }
            }

            foreach (var field in new[] { "m_hairItemInstance", "m_beardItemInstance" })
            {
                if (equipment.TryGetField<VisEquipment, GameObject>(field, out var hair)) hair.SetActive(false);
            }

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
            {
                if (item != null) item.SetActive(visible);
            }
        }

        private static void SetHand(VisEquipment equipment,
            string field,
            Vector3 offset,
            Vector3 rotation,
            VrmSettings settings)
        {
            if (!equipment.TryGetField<VisEquipment, GameObject>(field + "Instance", out var item)) return;
            var name = equipment.GetEquippedItemName(field);
            if (settings.TryGetItemAdjustment(name, GameItem.ClassOf(name), true, out var itemPos, out var itemRot))
            {
                offset += itemPos;
                rotation += itemRot;
            }

            var reference = EquipmentTransformReference.Get(item.transform);
            reference.SetRotationOffset(rotation);
            reference.SetPositionOffset(offset, settings.PlayerVrmScale);
            reference.SetScale(settings.PlayerVrmScale * settings.GetWeaponScale(name));
            RebuildJoints(item.transform);
        }

        private static void SetBack(VisEquipment equipment, string field, bool right, VrmSettings settings)
        {
            if (!equipment.TryGetField<VisEquipment, GameObject>(field + "Instance", out var item)) return;
            var reference = EquipmentTransformReference.Get(item.transform);
            var offset = right ? settings.RightHandBackItemPos : settings.LeftHandBackItemPos;
            var rotation = right ? settings.RightHandBackItemRot : settings.LeftHandBackItemRot;
            var name = equipment.GetEquippedItemName(field);
            if (settings.TryGetItemAdjustment(name, GameItem.ClassOf(name), false, out var itemPos, out var itemRot))
            {
                offset += itemPos;
                rotation += itemRot;
            }

            reference.SetRotationOffset(rotation);
            reference.SetPositionOffset(offset, settings.PlayerVrmScale);
            reference.SetScale(settings.PlayerVrmScale * settings.GetWeaponScale(name));
            RebuildJoints(item.transform);
        }

        // where a jointed part sat when the item was made, and the size its joint was last built for
        private sealed class JointRest
        {
            internal Vector3 LocalPosition, BuiltFor;
            internal Quaternion LocalRotation;
        }

        private static readonly ConditionalWeakTable<Joint, JointRest> JointRests =
            new ConditionalWeakTable<Joint, JointRest>();

        private static readonly List<Rigidbody> SwingingParts = new List<Rigidbody>();
        private static bool _swingingPartsInstalled;

        // unity draws a swinging part like the lantern between its last two physics spots. posing the avatar
        // after that drags it, and unity then puts its body back on that older spot every frame, so it trails
        // the hand. put the part back on its body first
        internal static void InstallSwingingParts()
        {
            if (_swingingPartsInstalled) return;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            if (!FrameClock.AddBefore(ref loop,
                    typeof(UnityEngine.PlayerLoop.EarlyUpdate),
                    typeof(UnityEngine.PlayerLoop.EarlyUpdate.PhysicsResetInterpolatedTransformPosition),
                    PlaceSwingingParts,
                    typeof(PatchVisEquipmentUpdateLodgroup)))
            {
                Logger.LogWarning(
                    "Could not hook the frame start, items that swing from the avatar may hang behind it.");
                return;
            }

            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
            _swingingPartsInstalled = true;
        }

        private static void PlaceSwingingParts()
        {
            for (var i = SwingingParts.Count - 1; i >= 0; i--)
            {
                var part = SwingingParts[i];
                if (part == null)
                {
                    SwingingParts.RemoveAt(i);
                    continue;
                }

                if (part.gameObject.activeInHierarchy)
                    part.transform.SetPositionAndRotation(part.position, part.rotation);
            }
        }

        // joints keep the anchors they were built with, so a scaled lantern hangs as low as a full size one.
        // reset the part and rebuild the joint at the current size
        private static void RebuildJoints(Transform item)
        {
            foreach (var joint in item.GetComponentsInChildren<Joint>(true))
            {
                var part = joint.transform;
                if (!JointRests.TryGetValue(joint, out var rest))
                {
                    rest = new JointRest { LocalPosition = part.localPosition, LocalRotation = part.localRotation };
                    JointRests.Add(joint, rest);
                    var swinging = joint.GetComponent<Rigidbody>();
                    if (swinging != null && !swinging.isKinematic &&
                        swinging.interpolation != RigidbodyInterpolation.None)
                        SwingingParts.Add(swinging);
                }

                if ((part.lossyScale - rest.BuiltFor).sqrMagnitude < 1e-10f) continue;
                part.SetLocalPositionAndRotation(rest.LocalPosition, rest.LocalRotation);
                var body = joint.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = part.position;
                    body.rotation = part.rotation;
                    if (!body.isKinematic)
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                }

                // the body it hangs from can still be at the vanilla hand, sync it or that spot gets baked in
                var connected = joint.connectedBody;
                if (connected != null)
                {
                    connected.position = connected.transform.position;
                    connected.rotation = connected.transform.rotation;
                }

                joint.connectedBody = null;
                joint.connectedBody = connected;
                rest.BuiltFor = part.lossyScale;
            }
        }

        internal static void RecreateEquipment(VisEquipment equipment)
        {
            // Recreate any rig that was attached before the VRM became ready, and
            // rebuild skinned clothing whose renderer visibility was previously hidden.
            foreach (var slot in new[]
                     {
                         "LeftItem", "RightItem", "HelmetItem", "ChestItem", "LegItem", "ShoulderItem",
                         "UtilityItem", "TrinketItem", "LeftBackItem", "RightBackItem"
                     })
                AccessTools.Field(typeof(VisEquipment), "m_current" + slot + "Hash")?.SetValue(equipment, int.MinValue);
            equipment.InvokePrivateMethod("UpdateEquipmentVisuals");
        }
    }
}

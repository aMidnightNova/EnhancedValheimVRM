using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    internal static class PatchVisEquipmentAttachItem
    {
        private static readonly System.Type EquipmentVisual = typeof(VisEquipment).Assembly.GetType("IEquipmentVisual", true);

        // Keep the game's attachment behavior; the one exception is preserving a
        // dual-hand item's own skinning rig for Enhanced's hand corrections.
        private static bool Prefix(VisEquipment __instance, int itemHash, int variant, Transform joint,
            ref GameObject __result, bool enableEquipEffects = true, bool backAttach = false, int quality = 0)
        {
            if (!__instance.m_isPlayer || !__instance.TryGetComponent<Player>(out var player) || player.GetVrmInstance()?.GetGameObject() == null) return true;
            var prefab = ObjectDB.instance.GetItemPrefab(itemHash);
            if (prefab == null || !GameItem.IsSpecialCase(prefab.name)) return true;
            Transform attachment = null;
            foreach (Transform child in prefab.transform)
            {
                if (backAttach && child.name == "attach_back") { attachment = child; break; }
                if (child.name == "attach" || (!backAttach && child.name == "attach_skin")) { attachment = child; break; }
            }
            if (attachment == null) { __result = null; return false; }
            var instance = Object.Instantiate(attachment.gameObject);
            instance.SetActive(true);
            __instance.InvokePrivateMethod("CleanupInstance", instance);
            if (enableEquipEffects) __instance.InvokePrivateMethod("EnableEquippedEffects", instance);
            foreach (var scaler in instance.GetComponentsInChildren<ParticleIntensityScaler>()) scaler.SetQuality(quality);
            instance.transform.SetParent(attachment.name == "attach_skin" ? __instance.m_bodyModel.transform.parent : joint);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (attachment.name == "attach_skin")
            {
                // Its hands will move outside the prefab's authored rest bounds.
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
                __instance.InvokePrivateMethod("SetupCloth", instance);
            }
            var offset = prefab.transform.Find("equipoffset");
            if (offset != null)
            {
                instance.transform.localPosition += offset.position;
                instance.transform.localRotation *= offset.rotation;
            }
            var visual = instance.GetComponentInChildren(EquipmentVisual);
            if (visual != null) EquipmentVisual.GetMethod("Setup").Invoke(visual, new object[] { variant });
            __instance.InvokePrivateMethod("RefreshSnowLevel");
            __result = instance;
            return false;
        }
    }
}

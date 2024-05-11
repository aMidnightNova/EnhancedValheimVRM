using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    static class ExtensionMethods
    {
        public static FieldInfo GetFieldValue<T>(this object instance, string fieldName)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            return instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
        
        public static Tout GetField<Tin, Tout>(this Tin self, string fieldName)
        {
            return AccessTools.FieldRefAccess<Tin, Tout>(fieldName).Invoke(self);
        }
        public static bool TryGetField<Tin, Tout>(this Tin self, string fieldName, out Tout result)
        {
            try
            {
                result = AccessTools.FieldRefAccess<Tin, Tout>(fieldName).Invoke(self);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to access field '{fieldName}': {ex.Message}");
            }
            result = default(Tout);
            return false;
        }
        public static void SetVisible(this GameObject obj, bool flag)
        {
            foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = flag;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = flag;
        }

    }
}
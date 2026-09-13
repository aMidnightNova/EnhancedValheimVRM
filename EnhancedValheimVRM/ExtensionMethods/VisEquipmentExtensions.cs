using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    internal static class VisEquipmentExtensions
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();

        private static object ReadField(VisEquipment equipment, string name)
        {
            if (!Fields.TryGetValue(name, out var field))
            {
                field = AccessTools.Field(typeof(VisEquipment), name);
                Fields[name] = field;
            }

            return field?.GetValue(equipment);
        }

        public static string GetEquippedItemName(this VisEquipment equipment, string fieldName)
        {
            if (equipment == null || string.IsNullOrEmpty(fieldName) ||
                !fieldName.StartsWith("m_", StringComparison.Ordinal) || fieldName.Length < 3)
                return null;
            var currentField = "m_current" + char.ToUpperInvariant(fieldName[2]) + fieldName.Substring(3) + "Hash";
            if (!(ReadField(equipment, currentField) is int hash) || hash == 0) return null;
            return ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash)?.name : null;
        }
    }
}

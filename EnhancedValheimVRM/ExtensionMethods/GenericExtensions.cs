using System;
using System.Reflection;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    internal static class GenericExtensions
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
                result = GetField<Tin, Tout>(self, fieldName);
                if (result == null)
                {
                    // Logger.Log($"Field '{fieldName}' exists but is null");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to access field '{fieldName}': {ex.Message}");
            }

            result = default(Tout);
            return false;
        }

        public static void SetField<Tin, Tvalue>(this Tin self, string fieldName, Tvalue value)
        {
            var field = self.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new ArgumentException($"Field '{fieldName}' not found in type '{typeof(Tin).FullName}'");

            field.SetValue(self, value);
        }

        public static void SetProperty<Tin, Tvalue>(this Tin self, string propertyName, Tvalue value)
        {
            var property = self.GetType().GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new ArgumentException($"Property '{propertyName}' not found in type '{typeof(Tin).FullName}'");

            property.SetValue(self, value);
        }

        public static Tvalue GetProperty<Tin, Tvalue>(this Tin self, string propertyName)
        {
            var property = self.GetType().GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new ArgumentException($"Property '{propertyName}' not found in type '{typeof(Tin).FullName}'");

            return (Tvalue)property.GetValue(self);
        }

        public static object InvokePrivateMethod(this object instance, string methodName, params object[] parameters)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method == null)
                throw new ArgumentException($"Method '{methodName}' not found in type '{instance.GetType().FullName}'");

            return method.Invoke(instance, parameters);
        }
    }
}

using System;
using System.Reflection;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    internal static class GenericExtensions
    {
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
                Logger.LogOnce("field-access:" + typeof(Tin).FullName + ":" + fieldName,
                    $"Failed to access field '{fieldName}': {ex.Message}");
            }

            result = default(Tout);
            return false;
        }

        public static object InvokePrivateMethod(this object instance, string methodName, params object[] parameters)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var method = instance.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method == null)
                throw new ArgumentException($"Method '{methodName}' not found in type '{instance.GetType().FullName}'");

            return method.Invoke(instance, parameters);
        }
    }
}
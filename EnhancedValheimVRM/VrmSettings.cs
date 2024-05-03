using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmSettings
    {
        public float ModelScale = 1.1f;
        public float ModelOffsetY = 0.0f;
        public float PlayerHeight = 1.85f;
        public float PlayerRadius = 0.5f;

        public float HeightAspect => PlayerHeight / 1.85f;
        public float RadiusAspect => PlayerRadius / 0.5f;

        public Vector3 SittingOnChairOffset = Vector3.zero;
        public Vector3 SittingOnThroneOffset = Vector3.zero;
        public Vector3 SittingOnShipOffset = Vector3.zero;
        public Vector3 HoldingMastOffset = Vector3.zero;
        public Vector3 HoldingDragonOffset = Vector3.zero;
        public Vector3 SittingIdleOffset = Vector3.zero;
        public Vector3 SleepingOffset = Vector3.zero;

        public Vector3 RightHandItemPos = Vector3.zero;
        public Vector3 LeftHandItemPos = Vector3.zero;
        public Vector3 RightHandBackItemPos = Vector3.zero;
        public Vector3 RightHandBackItemToolPos = Vector3.zero;
        public Vector3 LeftHandBackItemPos = Vector3.zero;
        public Vector3 BowBackPos = Vector3.zero;
        public Vector3 KnifeSidePos = Vector3.zero;
        public Vector3 KnifeSideRot = Vector3.zero;
        public Vector3 StaffPos = Vector3.zero;
        public Vector3 StaffSkeletonPos = Vector3.zero;
        public Vector3 StaffRot = Vector3.zero;

        public bool HelmetVisible = false;
        public Vector3 HelmetScale = Vector3.one;
        public Vector3 HelmetOffset = Vector3.zero;

        public bool ChestVisible = false;
        public bool ShouldersVisible = false;
        public bool UtilityVisible = false;
        public bool LegsVisible = false;

        public float ModelBrightness = 0.8f;
        public bool FixCameraHeight = true;
        public bool UseMToonShader = false;
        public bool EnablePlayerFade = true;
        public bool AllowShare = true;

        public float SpringBoneStiffness = 1.0f;
        public float SpringBoneGravityPower = 1.0f;

        public float EquipmentScale = 1.0f;
        public float AttackDistanceScale = 1.0f;
        public float InteractionDistanceScale = 1.0f;
        public float SwimDepthScale = 1.0f;

        public bool AttemptTextureFix = false;


        // everything above is settings file properties.


        [NonSerializedAttribute] public string Name;


        private bool canReload = true;
        private string _path;

        private Dictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();
        private Dictionary<string, bool> _updatedProperties = new Dictionary<string, bool>();

        public VrmSettings(string playerName)
        {
            Name = playerName;

            _path = Path.Combine(Settings.Constants.VrmDir, $"{playerName}.vrm");

            if (File.Exists(_path))
            {
                Load();
            }
            else
            {
                canReload = false;
                Debug.LogWarning($"No Settings file found for '{playerName}', using defaults.");
            }
        }

        private void InitializePropertyTracking()
        {
            foreach (var prop in typeof(VrmSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanWrite)
                {
                    _properties[prop.Name] = prop;
                    _updatedProperties[prop.Name] = false;
                }
            }
        }

        private void CheckIfSettingsFileHasMissingProperties()
        {
            foreach (var entry in _updatedProperties)
            {
                if (!entry.Value)
                {
                    Debug.LogWarning($"No setting found in the settings file for: {entry.Key}. Using default value.");
                }
            }
        }

        private void Load()
        {
            Debug.Log($"Reading settings.");

            InitializePropertyTracking();

            var lines = File.ReadAllLines(_path);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }


                var parts = line.Split('=');
                if (parts.Length != 2)
                {
                    continue;
                }


                string key = parts[0].Trim();
                string value = parts[1].Trim();

                if (_properties.TryGetValue(key, out PropertyInfo property) && property.CanWrite)
                {
                    object valueOut = ParseValue(property.PropertyType, value);

                    if (valueOut != null)
                    {
                        property.SetValue(this, valueOut, null);
                        Debug.Log($"Setting '{key}' To -> {valueOut}");
                    }
                    else
                    {
                        Debug.LogWarning($"Unable to Parse : {key}. Bad value {value}. Using Default Value {property.GetValue(this)}");
                    }
                }
            }

            CheckIfSettingsFileHasMissingProperties();
        }

        private static object ParseValue(Type type, string value)
        {
            object valueOut = null;

            try
            {
                if (type == typeof(float))
                {
                    valueOut = float.Parse(value, CultureInfo.InvariantCulture);
                }
                else if (type == typeof(bool))
                {
                    valueOut = bool.Parse(value);
                }
                else if (type == typeof(Vector3))
                {
                    valueOut = ParseVector3(value);
                }
            }
            catch (Exception ex)
            {
                return null;
            }

            return valueOut;
        }

        private static Vector3? ParseVector3(string vectorString)
        {
            //vectorString = vectorString.Trim('(', ')');
            vectorString = vectorString.Trim('<', '>').Trim();
            string[] components = vectorString.Split(',');

            if (components.Length == 3)
            {
                try
                {
                    float x = float.Parse(components[0].Trim(), CultureInfo.InvariantCulture);
                    float y = float.Parse(components[1].Trim(), CultureInfo.InvariantCulture);
                    float z = float.Parse(components[2].Trim(), CultureInfo.InvariantCulture);
                    return new Vector3(x, y, z);
                }
                catch (FormatException ex)
                {
                    return null;
                }
            }

            return null;
        }

        public void Reload()
        {
            if (canReload)
            {
                Load();
            }
        }
    }
}
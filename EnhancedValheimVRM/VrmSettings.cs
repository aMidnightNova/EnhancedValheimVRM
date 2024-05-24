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
        // Writable Settings Fields
        public float ModelScale = 1.1f;
        public float ModelOffsetY = 0.0f;
        public float PlayerHeight = 1.85f;
        public float PlayerRadius = 0.5f;


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


        // END - Writable Settings Properties

        // COMPUTED Settings Properties
        // should be private or a property {get set}
        private float HeightAspect => PlayerHeight / 1.85f;
        private float RadiusAspect => PlayerRadius / 0.5f;
        private string _name;

        private bool _canReload = true;
        private string _path;

        private Dictionary<string, FieldInfo> _fields = new Dictionary<string, FieldInfo>();
        private Dictionary<string, bool> _updatedFields = new Dictionary<string, bool>();

        public VrmSettings(string playerName)
        {
            _name = playerName;

            _path = Path.Combine(Constants.Vrm.Dir, $"settings_{playerName}.txt");

            if (File.Exists(_path))
            {
                Load();
            }
            else
            {
                _canReload = false;
                Logger.LogWarning($"No Settings file found for '{playerName}', using defaults.");
            }
        }

        private void InitializePropertyTracking()
        {
            foreach (var field in typeof(VrmSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                _fields[field.Name] = field;
                _updatedFields[field.Name] = false;
            }
        }

        private void CheckIfSettingsFileHasMissingProperties()
        {
            foreach (var entry in _updatedFields)
            {
                if (!entry.Value)
                {
                    Logger.LogWarning($"No setting found in the settings file for: {entry.Key}. Using default value.");
                }
            }
        }

        private void Load()
        {
            Logger.Log($"Reading settings. -> {_name}");
            InitializePropertyTracking();

            var lines = File.ReadAllLines(_path);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
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


                if (_fields.TryGetValue(key, out FieldInfo field))
                {
                    object valueOut = ParseValue(field.FieldType, value);

                    if (valueOut != null)
                    {
                        field.SetValue(this, valueOut);
                        Logger.Log($"Setting '{key}' To -> {valueOut}");
                        _updatedFields[key] = true;
                    }
                    else
                    {
                        Logger.LogWarning($"Unable to Parse : {key}. Bad value {value}. Using Default Value {field.GetValue(this)}");
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
            vectorString = vectorString.Trim('<', '>').Trim();
            vectorString = vectorString.Trim('(', ')');
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
            if (_canReload)
            {
                Load();
            }
        }
    }
}
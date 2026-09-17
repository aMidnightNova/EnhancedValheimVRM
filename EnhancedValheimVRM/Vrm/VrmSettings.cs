using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public class VrmSettings
    {
        // Writable Settings Fields
        public float ModelScale = 1.0f;

        // seat nudge, x right y up z forward, scales with avatar height
        public Vector3 SittingOnChairOffset = Vector3.zero;

        public Vector3 RightHandItemPos = Vector3.zero;
        public Vector3 LeftHandItemPos = Vector3.zero;
        public Vector3 RightHandItemRot = Vector3.zero;
        public Vector3 LeftHandItemRot = Vector3.zero;
        public Vector3 RightHandBackItemRot = Vector3.zero;
        public Vector3 LeftHandBackItemRot = Vector3.zero;
        public Vector3 RightHandBackItemPos = Vector3.zero;
        public Vector3 LeftHandBackItemPos = Vector3.zero;
        public float WeaponScale = 1.0f;

        public bool HelmetVisible = false;
        public Vector3 HelmetScale = Vector3.one;
        public Vector3 HelmetOffset = Vector3.zero;

        public bool ChestVisible = false;
        public bool ShouldersVisible = false;
        public bool UtilityVisible = false;
        public bool TrinketVisible = false;
        public bool LegsVisible = false;

        public float ModelBrightness = 0.8f;
        public bool FixCameraHeight = true;
        public bool UseMToonShader = false;
        public bool EnablePlayerFade = true;
        public bool AllowShare = true;

        public float SpringBoneStiffness = 1.0f;
        public float SpringBoneGravityPower = 1.0f;

        public float InteractionDistanceScale = 1.0f;

        // Which game shader the texture fix converts to: "player" (the vanilla character shader)
        // or "creature" (the animal/monster shader, the only one with an emission input).
        public string ShaderForTextureFix = "player";

        public bool AttemptTextureFix = false;

        // only blendshapes the vrm expressions or your outfit file use get loaded, the rest stay empty so models
        // with hundreds of them load faster and cause less of a frame spike. true loads all of them.
        public bool KeepAllBlendShapes = false;

        // With AttemptTextureFix, how much of the avatar's own colour glows on its own (0 = none,
        // 1 = fully self-lit). Keeps a toon avatar from going black in shadow.
        public float TextureFixEmission = 0.15f;

        public bool UsesCreatureShader =>
            string.Equals(ShaderForTextureFix, "creature", StringComparison.OrdinalIgnoreCase);
        // END - Writable Settings Properties


        // COMPUTED Settings Properties
        // should be private or a property {get set}

        //private float RadiusAspect => PlayerRadius / 0.5f;

        // END - COMPUTED Settings Properties


        //Internal computed properties
        // a property {get set}
        // this is the scale of the VRM to the Player Model, typically its a smaller number but can be larger. E.G. 0.68f
        public float PlayerVrmScale { get; set; } = 1f;

        // avatar to player, measured the same way on both so where the bones sit inside the body cancels out
        public float PlayerVrmWidthScale { get; set; } = 1f;

        public float VrmHeight { get; set; } = 1f;

        public float VrmRadius { get; set; } = 0.5f;


        //Internal computed properties


        private string _name;
        private string _path;

        private Dictionary<string, FieldInfo> _fields = new Dictionary<string, FieldInfo>();

        // Per-class and per-weapon adjustments, on top of the slot offsets above. Lines look like
        //   BowPos=<0, 0.02, 0>                stowed bows
        //   BowPos=Draugrfang,<0, 0.03, 0>     one bow only (prefab name)
        //   KnifeHandRot=<0, 10, 0>            knives while held
        // Classes: Bow Crossbow Sword Knife Club Axe Spear Polearm Staff Shield Tool Pickaxe Torch Fist
        // (Mace = Club, Atgeir = Polearm, Hammer = Tool). A named line replaces its class line.
        public static readonly string[] ItemClasses =
        {
            "Bow", "Crossbow", "Sword", "Knife", "Club", "Axe", "Spear", "Polearm", "Staff", "Shield", "Tool",
            "Pickaxe", "Torch", "Fist"
        };

        private static readonly Dictionary<string, string> ClassAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mace", "Club" },
                { "Atgeir", "Polearm" },
                { "Hammer", "Tool" },
                { "Dagger", "Knife" },
                { "Greatsword", "Sword" }
            };

        private sealed class ItemAdjustment
        {
            public string Class;
            public Vector3 Pos, Rot;
            public bool HasPos, HasRot;
        }

        private readonly Dictionary<string, ItemAdjustment> _items =
            new Dictionary<string, ItemAdjustment>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, float> _weaponScales =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // per piece offsets for rigged weapons:
        //   KnifeSkollAndHatiLeftHandPos=<0, 0.01, 0>
        //   FistGoldRightForeArmRot=<0, 10, 0>
        public static readonly string[] RigParts = { "LeftHand", "RightHand", "LeftForeArm", "RightForeArm" };

        private readonly Dictionary<string, ItemAdjustment> _rigParts =
            new Dictionary<string, ItemAdjustment>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetRigOffset(string prefabName, string part, out Vector3 pos, out Vector3 rot)
        {
            pos = Vector3.zero;
            rot = Vector3.zero;
            if (string.IsNullOrEmpty(prefabName) || !_rigParts.TryGetValue(prefabName + part, out var offset))
                return false;
            pos = offset.Pos;
            rot = offset.Rot;
            return true;
        }

        private bool TryParseRigLine(string key, string value)
        {
            string suffix;
            if (key.EndsWith("Pos", StringComparison.OrdinalIgnoreCase))
                suffix = "Pos";
            else if (key.EndsWith("Rot", StringComparison.OrdinalIgnoreCase))
                suffix = "Rot";
            else
                return false;
            var stem = key.Substring(0, key.Length - 3);
            var part = Array.Find(RigParts, p => stem.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            if (part == null || stem.Length == part.Length) return false;
            var vector = ParseVector3(value);
            if (vector == null) throw new InvalidDataException("Cannot parse setting " + key + ": " + value);
            var name = stem.Substring(0, stem.Length - part.Length) + part;
            if (!_rigParts.TryGetValue(name, out var offset)) _rigParts[name] = offset = new ItemAdjustment();
            if (suffix == "Pos")
            {
                offset.Pos = vector.Value;
                offset.HasPos = true;
            }
            else
            {
                offset.Rot = vector.Value;
                offset.HasRot = true;
            }

            return true;
        }

        private static string ItemKey(string scope, bool hand)
        {
            return scope + (hand ? "|hand" : "|back");
        }

        public float GetWeaponScale(string prefabName)
        {
            return !string.IsNullOrEmpty(prefabName) && _weaponScales.TryGetValue(prefabName, out var scale)
                ? scale
                : WeaponScale;
        }

        private bool TryParseWeaponScaleLine(string key, string value)
        {
            if (!key.Equals(nameof(WeaponScale), StringComparison.OrdinalIgnoreCase)) return false;
            var comma = value.IndexOf(',');
            if (comma < 0) return false;
            var prefabName = value.Substring(0, comma).Trim();
            var scaleText = value.Substring(comma + 1).Trim();
            if (prefabName.Length == 0 || !(ParseValue(typeof(float), scaleText) is float scale))
                throw new InvalidDataException("Cannot parse named WeaponScale: " + value);
            _weaponScales[prefabName] = scale;
            return true;
        }

        // Returns true and the adjustment for a weapon: its own name first, then its class.
        public bool TryGetItemAdjustment(string prefabName,
            string itemClass,
            bool hand,
            out Vector3 pos,
            out Vector3 rot)
        {
            pos = Vector3.zero;
            rot = Vector3.zero;
            ItemAdjustment named = null, classed = null;
            if (!string.IsNullOrEmpty(prefabName)) _items.TryGetValue(ItemKey(prefabName, hand), out named);
            if (!string.IsNullOrEmpty(itemClass)) _items.TryGetValue(ItemKey(itemClass, hand), out classed);
            if (named == null && classed == null) return false;
            pos = named != null && named.HasPos ? named.Pos :
                classed != null && classed.HasPos ? classed.Pos : Vector3.zero;
            rot = named != null && named.HasRot ? named.Rot :
                classed != null && classed.HasRot ? classed.Rot : Vector3.zero;
            return true;
        }

        // Accepts "<Class>[Hand](Pos|Rot)=[name,]<x,y,z>"; returns false when the key is not one.
        private bool TryParseItemLine(string key, string value)
        {
            var hand = false;
            string suffix;
            if (key.EndsWith("Pos", StringComparison.OrdinalIgnoreCase))
                suffix = "Pos";
            else if (key.EndsWith("Rot", StringComparison.OrdinalIgnoreCase))
                suffix = "Rot";
            else
                return false;
            var cls = key.Substring(0, key.Length - 3);
            if (cls.EndsWith("Hand", StringComparison.OrdinalIgnoreCase))
            {
                hand = true;
                cls = cls.Substring(0, cls.Length - 4);
            }

            if (ClassAliases.TryGetValue(cls, out var alias)) cls = alias;
            var canonical = Array.Find(ItemClasses, c => c.Equals(cls, StringComparison.OrdinalIgnoreCase));
            if (canonical == null) return false;
            string scope = canonical, vectorText = value;
            var comma = value.IndexOf(',');
            var open = value.IndexOfAny(new[] { '<', '(' });
            if (comma >= 0 && (open < 0 || comma < open))
            {
                scope = value.Substring(0, comma).Trim();
                vectorText = value.Substring(comma + 1).Trim();
                if (scope.Length == 0) throw new InvalidDataException("Empty weapon name in " + key);
            }

            var vector = ParseVector3(vectorText);
            if (vector == null) throw new InvalidDataException("Cannot parse setting " + key + ": " + value);
            var itemKey = ItemKey(scope, hand);
            if (!_items.TryGetValue(itemKey, out var adjustment))
                _items[itemKey] = adjustment = new ItemAdjustment { Class = canonical };
            if (suffix == "Pos")
            {
                adjustment.Pos = vector.Value;
                adjustment.HasPos = true;
            }
            else
            {
                adjustment.Rot = vector.Value;
                adjustment.HasRot = true;
            }

            return true;
        }

        public VrmSettings(string playerName)
        {
            _name = playerName;

            _path = Constants.Vrm.Find($"settings_{playerName}.txt");

            if (File.Exists(_path)) Load();
        }

        public string GetSettingsFilePath()
        {
            return _path;
        }

        private void InitializePropertyTracking()
        {
            foreach (var field in typeof(VrmSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                _fields[field.Name] = field;
        }

        public VrmSettings(string playerName, string sharedSettings)
        {
            _name = playerName;
            LoadLines(sharedSettings.Split('\n'));
        }

        public string Serialize()
        {
            var lines = new List<string>();
            foreach (var field in typeof(VrmSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = field.GetValue(this);
                string text;
                if (value is Vector3 vector)
                {
                    text = string.Format(CultureInfo.InvariantCulture,
                        "({0:R},{1:R},{2:R})",
                        vector.x,
                        vector.y,
                        vector.z);
                }
                else if (value is float number)
                    text = number.ToString("R", CultureInfo.InvariantCulture);
                else
                    text = Convert.ToString(value, CultureInfo.InvariantCulture);

                lines.Add(field.Name + "=" + text);
            }

            // Per-weapon lines travel with the shared settings too.
            foreach (var pair in _items)
            {
                var bar = pair.Key.LastIndexOf('|');
                var scope = pair.Key.Substring(0, bar);
                var hand = pair.Key.EndsWith("|hand", StringComparison.Ordinal);
                var isClass = scope.Equals(pair.Value.Class, StringComparison.OrdinalIgnoreCase);
                var prefix = pair.Value.Class + (hand ? "Hand" : "");
                var name = isClass ? "" : scope + ",";
                if (pair.Value.HasPos)
                {
                    lines.Add(prefix + "Pos=" + name + string.Format(CultureInfo.InvariantCulture,
                        "({0:R},{1:R},{2:R})",
                        pair.Value.Pos.x,
                        pair.Value.Pos.y,
                        pair.Value.Pos.z));
                }

                if (pair.Value.HasRot)
                {
                    lines.Add(prefix + "Rot=" + name + string.Format(CultureInfo.InvariantCulture,
                        "({0:R},{1:R},{2:R})",
                        pair.Value.Rot.x,
                        pair.Value.Rot.y,
                        pair.Value.Rot.z));
                }
            }

            foreach (var pair in _weaponScales)
                lines.Add("WeaponScale=" + pair.Key + "," + pair.Value.ToString("R", CultureInfo.InvariantCulture));

            foreach (var pair in _rigParts)
            {
                if (pair.Value.HasPos)
                    lines.Add(pair.Key + "Pos=" + string.Format(CultureInfo.InvariantCulture,
                        "({0:R},{1:R},{2:R})",
                        pair.Value.Pos.x,
                        pair.Value.Pos.y,
                        pair.Value.Pos.z));
                if (pair.Value.HasRot)
                    lines.Add(pair.Key + "Rot=" + string.Format(CultureInfo.InvariantCulture,
                        "({0:R},{1:R},{2:R})",
                        pair.Value.Rot.x,
                        pair.Value.Rot.y,
                        pair.Value.Rot.z));
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private void Load()
        {
            LoadLines(File.ReadAllLines(_path));
        }

        private void LoadLines(IEnumerable<string> lines)
        {
            InitializePropertyTracking();


            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//")) continue;


                var parts = line.Split('=');
                if (parts.Length != 2) continue;


                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (TryParseWeaponScaleLine(key, value)) continue;
                if (!_fields.ContainsKey(key) && TryParseItemLine(key, value)) continue;
                if (!_fields.ContainsKey(key) && TryParseRigLine(key, value)) continue;

                if (_fields.TryGetValue(key, out var field))
                {
                    var valueOut = ParseValue(field.FieldType, value);

                    if (valueOut != null)
                        field.SetValue(this, valueOut);
                    else
                        throw new InvalidDataException($"Cannot parse setting {key}: {value}");
                }
            }

            Validate();
        }

        private void Validate()
        {
            foreach (var pair in _items.Concat(_rigParts))
            foreach (var v in new[] { pair.Value.Pos, pair.Value.Rot })
            {
                if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) ||
                    float.IsNaN(v.z) || float.IsInfinity(v.z))
                    throw new InvalidDataException("Non-finite weapon adjustment: " + pair.Key);
            }

            foreach (var pair in _weaponScales)
            {
                if (pair.Value <= 0 || float.IsNaN(pair.Value) || float.IsInfinity(pair.Value))
                    throw new InvalidDataException("WeaponScale override must be positive and finite: " + pair.Key);
            }

            foreach (var field in _fields.Values)
            {
                var value = field.GetValue(this);
                if (value is float number && (float.IsNaN(number) || float.IsInfinity(number)))
                    throw new InvalidDataException("Non-finite setting: " + field.Name);
                if (value is Vector3 vector && (float.IsNaN(vector.x) || float.IsInfinity(vector.x) ||
                        float.IsNaN(vector.y) || float.IsInfinity(vector.y) ||
                        float.IsNaN(vector.z) || float.IsInfinity(vector.z)))
                    throw new InvalidDataException("Non-finite offset: " + field.Name);
            }

            if (!UsesCreatureShader &&
                !string.Equals(ShaderForTextureFix, "player", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ShaderForTextureFix must be player or creature.");
            if (TextureFixEmission < 0 || TextureFixEmission > 1)
                throw new InvalidDataException("TextureFixEmission must be between 0 and 1.");
            if (ModelScale <= 0 || WeaponScale <= 0 || InteractionDistanceScale <= 0 || SpringBoneStiffness < 0 ||
                SpringBoneGravityPower < 0 || ModelBrightness < 0)
            {
                throw new InvalidDataException(
                    "Model, weapon, and interaction scales must be positive; brightness and spring multipliers must be nonnegative.");
            }
        }

        private static object ParseValue(Type type, string value)
        {
            object valueOut = null;

            try
            {
                if (type == typeof(string))
                    valueOut = value;
                else if (type == typeof(float))
                    valueOut = float.Parse(value, CultureInfo.InvariantCulture);
                else if (type == typeof(bool))
                    valueOut = bool.Parse(value);
                else if (type == typeof(Vector3)) valueOut = ParseVector3(value);
            }
            catch (Exception)
            {
                return null;
            }

            return valueOut;
        }

        private static Vector3? ParseVector3(string vectorString)
        {
            vectorString = vectorString.Trim('<', '>').Trim();
            vectorString = vectorString.Trim('(', ')');
            var components = vectorString.Split(',');

            if (components.Length == 3)
            {
                try
                {
                    var x = float.Parse(components[0].Trim(), CultureInfo.InvariantCulture);
                    var y = float.Parse(components[1].Trim(), CultureInfo.InvariantCulture);
                    var z = float.Parse(components[2].Trim(), CultureInfo.InvariantCulture);
                    return new Vector3(x, y, z);
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}

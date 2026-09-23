using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using UniGLTF;
using UniGLTF.Utils;
using UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    // unity only clones [Serializable] classes and structs from assemblies on the game's own list.
    // anything loaded from the plugin folder is not on it, so a clone comes out with those fields
    // reset. every such field is put back from the cached import, before the clone is switched on
    internal static class VrmRepair
    {
        private static string _gameAssemblies;
        private static readonly Dictionary<Assembly, bool> Unlisted = new Dictionary<Assembly, bool>();
        private static readonly Dictionary<Type, FieldInfo[]> Serialized = new Dictionary<Type, FieldInfo[]>();

        // how many fields the clone had lost, 0 when it came out whole
        internal static int Restore(GameObject template, GameObject clone)
        {
            // a fresh clone has the same hierarchy, so both sides list their components in the same order
            var from = template.GetComponentsInChildren<Component>(true);
            var to = clone.GetComponentsInChildren<Component>(true);
            if (from.Length != to.Length) return Mismatch(clone);
            var map = new Dictionary<Object, Object>(from.Length * 2);
            for (var i = 0; i < from.Length; i++)
            {
                if (from[i] == null && to[i] == null) continue;
                if (from[i] == null || to[i] == null || from[i].GetType() != to[i].GetType()) return Mismatch(clone);
                map[from[i]] = to[i];
                map[from[i].gameObject] = to[i].gameObject;
            }

            var lost = 0;
            for (var i = 0; i < from.Length; i++)
            {
                // our own components keep per copy state, they are never taken from the import
                if (!(from[i] is MonoBehaviour)) continue;
                var type = from[i].GetType();
                if (type.Assembly == typeof(VrmRepair).Assembly || !IsUnlisted(type.Assembly)) continue;
                foreach (var field in Fields(type))
                {
                    if (!HoldsCustom(field.FieldType)) continue;
                    var authored = field.GetValue(from[i]);
                    if (Same(authored, field.GetValue(to[i]), map)) continue;
                    field.SetValue(to[i], Copy(authored, map));
                    lost++;
                }
            }

            return lost + RestoreRestPose(template, clone, map);
        }

        // the rest pose is a plain dictionary, unity never clones those. vrm 1.0 springs start from it
        private static int RestoreRestPose(GameObject template, GameObject clone, Dictionary<Object, Object> map)
        {
            var rest = template.GetComponent<RuntimeGltfInstance>();
            var target = clone.GetComponent<RuntimeGltfInstance>();
            if (rest == null || target == null || clone.GetComponent<Vrm10Instance>() == null) return 0;
            if (target.InitialTransformStates.Count == rest.InitialTransformStates.Count) return 0;
            if (!target.TryGetField<RuntimeGltfInstance, Dictionary<Transform, TransformState>>(
                    "_initialTransformStates",
                    out var states))
                return 0;
            foreach (var state in rest.InitialTransformStates)
            {
                if (map.TryGetValue(state.Key, out var bone)) states[(Transform)bone] = state.Value;
            }

            return 1;
        }

        private static bool IsUnlisted(Assembly assembly)
        {
            if (Unlisted.TryGetValue(assembly, out var unlisted)) return unlisted;
            if (_gameAssemblies == null)
            {
                try
                {
                    _gameAssemblies = File.ReadAllText(Path.Combine(Application.dataPath, "ScriptingAssemblies.json"));
                }
                catch (Exception)
                {
                    _gameAssemblies = "";
                }
            }

            var name = assembly.GetName().Name;
            unlisted = name != "mscorlib" && name != "netstandard" &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !_gameAssemblies.Contains("\"" + name + ".dll\"");
            Unlisted[assembly] = unlisted;
            return unlisted;
        }

        // a class or struct of the kind unity drops, as opposed to numbers, unity objects and unity's own types
        private static bool IsCustom(Type type)
        {
            return !type.IsEnum && !typeof(Object).IsAssignableFrom(type) && type.IsSerializable &&
                IsUnlisted(type.Assembly);
        }

        private static bool HoldsCustom(Type type)
        {
            if (type.IsArray) return IsCustom(type.GetElementType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return IsCustom(type.GetGenericArguments()[0]);
            return IsCustom(type);
        }

        // the fields unity would save: public or [SerializeField], up to the first base class it knows
        private static FieldInfo[] Fields(Type type)
        {
            if (Serialized.TryGetValue(type, out var fields)) return fields;
            var found = new List<FieldInfo>();
            for (var level = type; level != null && IsUnlisted(level.Assembly); level = level.BaseType)
            {
                foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public |
                             BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsNotSerialized) continue;
                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), false)) found.Add(field);
                }
            }

            return Serialized[type] = found.ToArray();
        }

        private static Object Mapped(Object original, Dictionary<Object, Object> map)
        {
            return original != null && map.TryGetValue(original, out var cloned) ? cloned : original;
        }

        private static bool Same(object authored, object cloned, Dictionary<Object, Object> map)
        {
            // unity's own compare, so a destroyed object counts as null
            if (authored is Object || cloned is Object) return Mapped(authored as Object, map) == cloned as Object;
            if (authored == null || cloned == null) return authored == cloned;
            if (authored.GetType() != cloned.GetType()) return false;
            if (authored is IList list)
            {
                var other = (IList)cloned;
                if (other.Count != list.Count) return false;
                for (var i = 0; i < list.Count; i++)
                {
                    if (!Same(list[i], other[i], map)) return false;
                }

                return true;
            }

            if (!IsCustom(authored.GetType())) return authored.Equals(cloned);
            foreach (var field in Fields(authored.GetType()))
            {
                if (!Same(field.GetValue(authored), field.GetValue(cloned), map)) return false;
            }

            return true;
        }

        // parts of the avatar are swapped for the clones own, assets like meshes and curves stay shared
        private static object Copy(object value, Dictionary<Object, Object> map)
        {
            if (value == null) return null;
            if (value is Object original) return Mapped(original, map);
            var type = value.GetType();
            if (value is IList list)
            {
                var copy = type.IsArray
                    ? Array.CreateInstance(type.GetElementType(), list.Count)
                    : (IList)Activator.CreateInstance(type);
                for (var i = 0; i < list.Count; i++)
                {
                    if (type.IsArray)
                        copy[i] = Copy(list[i], map);
                    else
                        copy.Add(Copy(list[i], map));
                }

                return copy;
            }

            if (!IsCustom(type)) return value;
            // some of these have no empty constructor, and the fields are all that gets saved anyway
            var result = FormatterServices.GetUninitializedObject(type);
            foreach (var field in Fields(type)) field.SetValue(result, Copy(field.GetValue(value), map));
            return result;
        }

        private static int Mismatch(GameObject clone)
        {
            Logger.LogWarning(clone.name + ": the avatar copy does not line up with its import, " +
                "hair and tail physics may be off");
            return 0;
        }
    }
}

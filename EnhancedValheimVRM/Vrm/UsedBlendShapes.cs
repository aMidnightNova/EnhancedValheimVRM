using System;
using System.Collections.Generic;
using UniGLTF;
using UniGLTF.Extensions.VRMC_vrm;
using UniVRM10;
using VRM;

namespace EnhancedValheimVRM
{
    // names of the blendshapes something can actually drive. vrm expressions and blendshape
    // groups, blendshape lines in the outfit file and its [Blendshapes] section.
    internal static class UsedBlendShapes
    {
        internal static HashSet<string> Names(object parsed, OutfitConfig outfits)
        {
            // case insensitive, the face stream and outfit files spell shapes their own way
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            switch (parsed)
            {
                case VRMData vrm0:
                    foreach (var group in vrm0.VrmExtension.blendShapeMaster.blendShapeGroups)
                    {
                        foreach (var bind in group.binds) AddTarget(names, vrm0.Data.GLTF, bind.mesh, bind.index);
                    }

                    break;
                case Vrm10Data vrm1:
                    var gltf = vrm1.Data.GLTF;
                    foreach (var expression in Expressions(vrm1.VrmExtension.Expressions))
                    {
                        if (expression.MorphTargetBinds == null) continue;
                        foreach (var bind in expression.MorphTargetBinds)
                        {
                            if (bind.Node is int node && bind.Index is int index && node >= 0 &&
                                node < gltf.nodes.Count)
                                AddTarget(names, gltf, gltf.nodes[node].mesh, index);
                        }
                    }

                    break;
            }

            if (outfits == null) return names;
            foreach (var outfit in outfits.Outfits)
            {
                foreach (var key in outfit.Blendshapes.Keys)
                {
                    var colon = key.IndexOf(':');
                    names.Add(colon >= 0 ? key.Substring(colon + 1) : key);
                }
            }

            names.UnionWith(outfits.KeepBlendshapes);
            return names;
        }

        private static IEnumerable<Expression> Expressions(Expressions expressions)
        {
            if (expressions == null) yield break;
            if (expressions.Preset != null)
            {
                foreach (var field in typeof(Preset).GetFields())
                {
                    if (field.GetValue(expressions.Preset) is Expression expression) yield return expression;
                }
            }

            if (expressions.Custom == null) yield break;
            foreach (var expression in expressions.Custom.Values)
            {
                if (expression != null) yield return expression;
            }
        }

        private static void AddTarget(HashSet<string> names, glTF gltf, int mesh, int index)
        {
            if (mesh < 0 || mesh >= gltf.meshes.Count) return;
            if (gltf_mesh_extras_targetNames.TryGet(gltf.meshes[mesh], out var targetNames) && index >= 0 &&
                index < targetNames.Count)
                names.Add(targetNames[index]);
        }
    }
}

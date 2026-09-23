using System.Collections.Generic;
using HarmonyLib;
using UniGLTF;

namespace EnhancedValheimVRM
{
    // adding a blendshape to a unity mesh gets slower with every shape already on it. blendshapes nothing can
    // drive are emptied before univrm builds the mesh, it then adds them as empty frames, which keeps names and
    // indices and costs next to nothing. vrm 0.x and 1.0 meshes are both built here and yield between shapes
    internal static class PatchBlendShapes
    {
        internal sealed class Import
        {
            public HashSet<string> Keep; // null keeps everything
        }

        private static readonly List<Import> Active = new List<Import>();

        internal static Import Begin(HashSet<string> keep)
        {
            var import = new Import { Keep = keep };
            Active.Add(import);
            return import;
        }

        internal static void End(Import import)
        {
            Active.Remove(import);
        }

        private static bool Keep(string name)
        {
            if (Active.Count == 0) return true;
            foreach (var import in Active)
            {
                if (import.Keep == null || import.Keep.Contains(name)) return true;
            }

            return false;
        }

        // MeshUploader and MeshData are internal to univrm, hence the names
        [HarmonyPatch]
        private static class Upload
        {
            private static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("UniGLTF.MeshUploader"), "BuildMeshAndUploadAsync");
            }

            private static void Prefix(object data)
            {
                var shapes = AccessTools.Property(data.GetType(), "BlendShapes").GetValue(data) as
                    IReadOnlyList<BlendShape>;
                if (shapes == null) return;
                var kept = 0;
                foreach (var shape in shapes)
                {
                    if (Keep(shape.Name))
                    {
                        kept++;
                        continue;
                    }

                    shape.Positions.Clear();
                    shape.Normals.Clear();
                    shape.Tangents.Clear();
                }

                var mesh = AccessTools.Property(data.GetType(), "Name").GetValue(data) as string;
                if (Settings.LogLoadTiming && shapes.Count > 0)
                    Logger.Log("Blendshapes on " + mesh + ": " + kept + " of " + shapes.Count +
                        " kept, the rest left empty");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VrmLib;
using Mesh = UnityEngine.Mesh;

namespace EnhancedValheimVRM
{
    // adding a blendshape to a unity mesh gets slower with every shape already on it, and the vrm
    // 1.0 mesh builders add them all in one synchronous call. two things happen here. blendshapes
    // nothing can drive are added as empty frames, which keeps names and indices and costs nothing.
    // and on the frame sliced import the real ones are lifted out of the builder and added back a
    // few per frame from the import callers NextFrame, same data, same order.
    internal static class PatchBlendShapes
    {
        internal sealed class Import
        {
            public HashSet<string> Keep; // null keeps everything
            public bool Deferred;
        }

        private static readonly List<Import> Active = new List<Import>();
        private static readonly Queue<Action> Pending = new Queue<Action>();
        private static Vector3[] _zeros;

        internal static int PendingCount => Pending.Count;

        internal static Import Begin(HashSet<string> keep, bool deferred)
        {
            var import = new Import { Keep = keep, Deferred = deferred };
            Active.Add(import);
            return import;
        }

        internal static void End(Import import)
        {
            Active.Remove(import);
        }

        // adds blendshapes until the deadline, true when nothing is left
        internal static bool Drain(float untilRealtime)
        {
            while (Pending.Count > 0)
            {
                if (FrameClock.Reporting) FrameClock.Label = "blendshape, " + Pending.Count + " left";
                Pending.Dequeue()();
                if (Time.realtimeSinceStartup >= untilRealtime) break;
            }

            return Pending.Count == 0;
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

        // Queue work only when every active import can drain it across frames.
        // Menu imports are sync, so they add blendshapes immediately.
        private static bool Deferred()
        {
            if (Active.Count == 0) return false;
            foreach (var import in Active)
            {
                if (!import.Deferred) return false;
            }

            return true;
        }

        private static void Add(Action add)
        {
            if (Deferred())
                Pending.Enqueue(add);
            else
                add();
        }

        private static Vector3[] Zeros(int count)
        {
            if (_zeros == null || _zeros.Length != count) _zeros = new Vector3[count];
            return _zeros;
        }

        private static Vector3[] Read(BufferAccessor accessor)
        {
            return accessor != null ? accessor.Bytes.Reinterpret<Vector3>(1).ToArray() : null;
        }

        private static void Report(string mesh, int kept, int total)
        {
            if (Settings.LogLoadTiming && total > 0)
                Logger.Log("Blendshapes on " + mesh + ": " + kept + " of " + total + " kept, the rest left empty");
        }

        [HarmonyPatch(typeof(MeshImporterDivided), nameof(MeshImporterDivided.LoadDivided))]
        private static class Divided
        {
            private static void Prefix(MeshGroup meshGroup, out List<List<MorphTarget>> __state)
            {
                __state = new List<List<MorphTarget>>();
                foreach (var mesh in meshGroup.Meshes)
                {
                    __state.Add(new List<MorphTarget>(mesh.MorphTargets));
                    mesh.MorphTargets.Clear();
                }
            }

            private static void Postfix(MeshGroup meshGroup, Mesh __result, List<List<MorphTarget>> __state)
            {
                for (var m = 0; m < meshGroup.Meshes.Count; m++) meshGroup.Meshes[m].MorphTargets.AddRange(__state[m]);
                var count = __state.Count > 0 ? __state[0].Count : 0;
                var kept = 0;
                for (var i = 0; i < count; i++)
                {
                    var index = i;
                    var keep = Keep(__state[0][i].Name);
                    if (keep) kept++;
                    Add(() => AddFrame(meshGroup, __result, index, keep));
                }

                Report(meshGroup.Name, kept, count);
            }

            // concatenates the per mesh deltas the way MeshImporterDivided does, zeros where a mesh has no normals
            private static void AddFrame(MeshGroup meshGroup, Mesh target, int index, bool keep)
            {
                if (target == null) return;
                var name = meshGroup.Meshes[0].MorphTargets[index].Name;
                if (!keep)
                {
                    target.AddBlendShapeFrame(name, 100f, Zeros(target.vertexCount), null, null);
                    return;
                }

                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                foreach (var mesh in meshGroup.Meshes)
                {
                    var morph = mesh.MorphTargets[index];
                    positions.AddRange(Read(morph.VertexBuffer.Positions));
                    var deltaNormals = Read(morph.VertexBuffer.Normals);
                    normals.AddRange(deltaNormals ?? new Vector3[morph.VertexBuffer.Count]);
                }

                target.AddBlendShapeFrame(name, 100f, positions.ToArray(), normals.ToArray(), null);
            }
        }

        [HarmonyPatch(typeof(MeshImporterShared), nameof(MeshImporterShared.LoadSharedMesh))]
        private static class Shared
        {
            private static void Prefix(VrmLib.Mesh src, out List<MorphTarget> __state)
            {
                __state = new List<MorphTarget>(src.MorphTargets);
                src.MorphTargets.Clear();
            }

            private static void Postfix(VrmLib.Mesh src, Mesh __result, List<MorphTarget> __state)
            {
                src.MorphTargets.AddRange(__state);
                var kept = 0;
                foreach (var morph in __state)
                {
                    var target = morph;
                    var keep = Keep(morph.Name);
                    if (keep) kept++;
                    Add(() =>
                    {
                        if (__result == null) return;
                        var positions = keep ? Read(target.VertexBuffer.Positions) : null;
                        __result.AddBlendShapeFrame(target.Name,
                            100f,
                            positions ?? Zeros(__result.vertexCount),
                            null,
                            null);
                    });
                }

                Report(__result != null ? __result.name : "mesh", kept, __state.Count);
            }
        }

        // the vrm 0.x builder already yields between shapes, it only needs the unused ones emptied.
        // MeshUploader and MeshData are internal to univrm, hence the names
        [HarmonyPatch]
        private static class Vrm0
        {
            private static System.Reflection.MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("UniGLTF.MeshUploader"), "BuildMeshAndUploadAsync");
            }

            private static void Prefix(object data)
            {
                var shapes =
                    AccessTools.Property(data.GetType(), "BlendShapes").GetValue(data) as
                        IReadOnlyList<UniGLTF.BlendShape>;
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

                Report(AccessTools.Property(data.GetType(), "Name").GetValue(data) as string, kept, shapes.Count);
            }
        }
    }
}

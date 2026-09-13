using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Utils
    {
        private static Vector3 RestPosition(GameObject model, Transform bone)
        {
            // Bind poses keep reload measurements stable while a character is running
            // or crouching. Bone names select anatomy; mesh names are irrelevant.
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || !renderer.sharedMesh.isReadable) continue;
                var index = Array.IndexOf(renderer.bones, bone);
                var bindposes = renderer.sharedMesh.bindposes;
                if (index >= 0 && index < bindposes.Length)
                    return renderer.transform.TransformPoint(bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero));
            }

            return bone.position;
        }

        public static float GetModelWidth(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>(true);
            var left = BoneLookup.Get(animator, HumanBodyBones.LeftUpperArm);
            var right = BoneLookup.Get(animator, HumanBodyBones.RightUpperArm);
            if (left == null || right == null)
            {
                throw new InvalidOperationException(
                    "Automatic sizing requires left/right upper-arm bones or a valid humanoid mapping.");
            }

            return Vector3.Distance(RestPosition(model, left), RestPosition(model, right));
        }

        // How far the avatar's torso extends behind its chest bone, in metres, measured on the
        // bind-pose mesh so the running pose does not matter. Only vertices that belong mostly
        // to the spine/chest bones count, so arms, tails and clothing props are ignored.
        // Returns 0 when nothing measurable is found.
        public static float GetTorsoBackDepth(GameObject model, Animator animator, Transform chest)
        {
            if (model == null || animator == null || chest == null) return 0f;
            var torso = new HashSet<Transform>();
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            {
                var t = BoneLookup.Get(animator, bone);
                if (t != null) torso.Add(t);
            }

            if (torso.Count == 0) return 0f;
            var chestRest = RestPosition(model, chest);
            var backward = -model.transform.forward;
            backward.y = 0;
            if (backward.sqrMagnitude < 1e-6f) return 0f;
            backward.Normalize();
            var depths = new List<float>();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var bones = renderer.bones;
                var torsoIndices = new HashSet<int>();
                for (var i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null && torso.Contains(bones[i])) torsoIndices.Add(i);
                }

                if (torsoIndices.Count == 0) continue;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                if (weights.Length != vertices.Length) continue;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    var dominant = w.boneIndex0;
                    var best = w.weight0;
                    if (w.weight1 > best)
                    {
                        best = w.weight1;
                        dominant = w.boneIndex1;
                    }

                    if (w.weight2 > best)
                    {
                        best = w.weight2;
                        dominant = w.boneIndex2;
                    }

                    if (w.weight3 > best)
                    {
                        best = w.weight3;
                        dominant = w.boneIndex3;
                    }

                    if (best < 0.5f || !torsoIndices.Contains(dominant)) continue;
                    var world = renderer.transform.TransformPoint(vertices[i]);
                    depths.Add(Vector3.Dot(world - chestRest, backward));
                }
            }

            if (depths.Count < 8) return 0f;
            depths.Sort();
            // Ignore the last couple of percent: stray vertices, hair, cloth tips.
            return Mathf.Max(0f, depths[(int)(depths.Count * 0.98f)]);
        }

        internal static bool TryGetSeatSupport(GameObject model, out Vector3 support)
        {
            support = Vector3.zero;
            if (model == null) return false;
            var animator = model.GetComponentInChildren<Animator>(true);
            var anatomy = new[]
            {
                BoneLookup.Get(animator, HumanBodyBones.Hips),
                BoneLookup.Get(animator, HumanBodyBones.LeftUpperLeg),
                BoneLookup.Get(animator, HumanBodyBones.RightUpperLeg),
                BoneLookup.Get(animator, HumanBodyBones.LeftLowerLeg),
                BoneLookup.Get(animator, HumanBodyBones.RightLowerLeg),
                BoneLookup.Get(animator, HumanBodyBones.LeftFoot),
                BoneLookup.Get(animator, HumanBodyBones.RightFoot)
            };
            if (Array.Exists(anatomy, bone => bone == null)) return false;
            var feet = new HashSet<Transform>
            {
                BoneLookup.Get(animator, HumanBodyBones.LeftFoot),
                BoneLookup.Get(animator, HumanBodyBones.RightFoot),
                BoneLookup.Get(animator, HumanBodyBones.LeftToes),
                BoneLookup.Get(animator, HumanBodyBones.RightToes)
            };
            feet.Remove(null);
            if (feet.Count == 0) return false;

            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // keep scale, drop position and facing
            var worldToMetres = Matrix4x4.TRS(model.transform.position, model.transform.rotation, Vector3.one).inverse;
            var rest = new Vector3[anatomy.Length];
            var found = new bool[anatomy.Length];
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var bones = skin.bones;
                var bindposes = mesh.bindposes;
                var toMetres = worldToMetres * skin.transform.localToWorldMatrix;
                for (var i = 0; i < anatomy.Length; i++)
                {
                    if (found[i]) continue;
                    var index = Array.IndexOf(bones, anatomy[i]);
                    if (index < 0 || index >= bindposes.Length) continue;
                    rest[i] = toMetres.MultiplyPoint3x4(bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero));
                    found[i] = math.all(math.isfinite(AttachmentTransforms.Vector(rest[i])));
                }
            }

            // bind pose only, a reload while sitting has to measure the same as standing
            if (Array.Exists(found, value => !value) || (rest[3] - rest[1]).sqrMagnitude < 1e-8f ||
                (rest[4] - rest[2]).sqrMagnitude < 1e-8f || (rest[5] - rest[3]).sqrMagnitude < 1e-8f ||
                (rest[6] - rest[4]).sqrMagnitude < 1e-8f)
                return false;
            var sample = new SeatSupportMeasurement(AttachmentTransforms.Vector(rest[0]),
                AttachmentTransforms.Vector(rest[1]),
                AttachmentTransforms.Vector(rest[2]),
                AttachmentTransforms.Rotation(Quaternion.FromToRotation(rest[3] - rest[1], Vector3.forward)),
                AttachmentTransforms.Rotation(Quaternion.FromToRotation(rest[4] - rest[2], Vector3.forward)),
                AttachmentTransforms.Vector(rest[3]),
                AttachmentTransforms.Vector(rest[4]),
                AttachmentTransforms.Rotation(Quaternion.FromToRotation(rest[5] - rest[3], Vector3.forward)),
                AttachmentTransforms.Rotation(Quaternion.FromToRotation(rest[6] - rest[4], Vector3.forward)),
                Vector3.Distance(rest[3], rest[5]),
                Vector3.Distance(rest[4], rest[6]));
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var bones = skin.bones;
                var groups = new int[bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == anatomy[0])
                        groups[i] = SeatSupportMeasurement.Hips;
                    else if (bones[i] == anatomy[1])
                        groups[i] = SeatSupportMeasurement.LeftThigh;
                    else if (bones[i] == anatomy[2])
                        groups[i] = SeatSupportMeasurement.RightThigh;
                    else if (bones[i] == anatomy[3])
                        groups[i] = SeatSupportMeasurement.LeftShin;
                    else if (bones[i] == anatomy[4])
                        groups[i] = SeatSupportMeasurement.RightShin;
                    else if (feet.Contains(bones[i])) groups[i] = SeatSupportMeasurement.Foot;
                }

                if (!Array.Exists(groups, group => group != 0)) continue;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                if (weights.Length != vertices.Length) continue;
                var toMetres = worldToMetres * skin.transform.localToWorldMatrix;

                int Group(int index)
                {
                    return index >= 0 && index < groups.Length ? groups[index] : 0;
                }

                for (var i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    sample.Add(AttachmentTransforms.Vector(toMetres.MultiplyPoint3x4(vertices[i])),
                        new int4(Group(w.boneIndex0), Group(w.boneIndex1), Group(w.boneIndex2), Group(w.boneIndex3)),
                        new float4(w.weight0, w.weight1, w.weight2, w.weight3));
                }
            }

            if (!sample.TryMeasure(out var measured)) return false;
            support = AttachmentTransforms.Vector(measured);
            return true;
        }

        public static float GetModelHeight(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>(true);
            var head = BoneLookup.Get(animator, HumanBodyBones.Head);
            var left = BoneLookup.Get(animator, HumanBodyBones.LeftFoot);
            var right = BoneLookup.Get(animator, HumanBodyBones.RightFoot);
            if (head == null || left == null || right == null)
            {
                throw new InvalidOperationException(
                    "Automatic sizing requires head and both foot bones or a valid humanoid mapping.");
            }

            var up = model.transform.up;
            var foot = Mathf.Min(Vector3.Dot(RestPosition(model, left), up),
                Vector3.Dot(RestPosition(model, right), up));
            var height = Vector3.Dot(RestPosition(model, head), up) - foot;
            if (height <= 0 || float.IsNaN(height) || float.IsInfinity(height))
                throw new InvalidOperationException("Cannot measure the avatar's head-to-foot height.");
            return height;
        }
    }
}

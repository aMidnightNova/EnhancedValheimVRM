using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
                int index = Array.IndexOf(renderer.bones, bone);
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
                throw new System.InvalidOperationException(
                    "Automatic sizing requires left/right upper-arm bones or a valid humanoid mapping.");
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
                for (int i = 0; i < bones.Length; i++) if (bones[i] != null && torso.Contains(bones[i])) torsoIndices.Add(i);
                if (torsoIndices.Count == 0) continue;
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                if (weights.Length != vertices.Length) continue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    int dominant = w.boneIndex0;
                    float best = w.weight0;
                    if (w.weight1 > best) { best = w.weight1; dominant = w.boneIndex1; }
                    if (w.weight2 > best) { best = w.weight2; dominant = w.boneIndex2; }
                    if (w.weight3 > best) { best = w.weight3; dominant = w.boneIndex3; }
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

        public static float GetModelHeight(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>(true);
            var head = BoneLookup.Get(animator, HumanBodyBones.Head);
            var left = BoneLookup.Get(animator, HumanBodyBones.LeftFoot);
            var right = BoneLookup.Get(animator, HumanBodyBones.RightFoot);
            if (head == null || left == null || right == null)
                throw new System.InvalidOperationException(
                    "Automatic sizing requires head and both foot bones or a valid humanoid mapping.");
            var up = model.transform.up;
            float foot = Mathf.Min(Vector3.Dot(RestPosition(model, left), up),
                Vector3.Dot(RestPosition(model, right), up));
            float height = Vector3.Dot(RestPosition(model, head), up) - foot;
            if (height <= 0 || float.IsNaN(height) || float.IsInfinity(height))
                throw new System.InvalidOperationException("Cannot measure the avatar's head-to-foot height.");
            return height;
        }
    }
}
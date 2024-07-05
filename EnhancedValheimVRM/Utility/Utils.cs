using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Utils
    {
        private static Dictionary<string, Coroutine> _debounceCoroutines = new Dictionary<string, Coroutine>();

        public static void Debounce(Action action, string key, float delay)
        {
            if (_debounceCoroutines.ContainsKey(key))
            {
                CoroutineHelper.Instance.StopCoroutine(_debounceCoroutines[key]);
            }

            _debounceCoroutines[key] = CoroutineHelper.Instance.StartCoroutine(DebounceCoroutine(action, key, delay));
        }

        private static IEnumerator DebounceCoroutine(Action action, string key, float delay)
        {
            yield return new WaitForSeconds(delay);

            action();

            _debounceCoroutines.Remove(key);
        }


        public static float GetModelWidth(GameObject model)
        {
            if (model == null)
            {
                Debug.LogError("Model is null");
                return 0f;
            }

            SkinnedMeshRenderer smrBody = null;
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in smrs)
            {
                if (smr.name == "Body" || smr.name == "body")
                {
                    smrBody = smr;
                    break;
                }
            }

            if (smrBody == null)
            {
                Debug.LogError("No SkinnedMeshRenderer named 'Body' or 'body' found on the model");
                return 0f;
            }

            var bones = smrBody.bones;
            if (bones == null || bones.Length == 0)
            {
                Debug.LogError("No bones found on the model");
                return 0f;
            }

            var leftUpperArm = BoneTransformer.FindPlayerBoneWithBoneEnum(bones, BoneTransformer.Bones.LeftUpperArm);
            var rightUpperArm = BoneTransformer.FindPlayerBoneWithBoneEnum(bones, BoneTransformer.Bones.RightUpperArm);

            if (leftUpperArm == null || rightUpperArm == null)
            {
                Debug.LogError("Shoulder bones not found on the model");
                return 0f;
            }

            var shoulderWidth = Vector3.Distance(leftUpperArm.position, rightUpperArm.position);
            return shoulderWidth;
        }




        public static float GetModelHeight(GameObject model)
        {
            if (model == null)
            {
                Debug.LogError("Model is null");
                return 0f;
            }

            SkinnedMeshRenderer smrBody = null;
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in smrs)
            {
                if (smr.name == "Body" || smr.name == "body")
                {
                    smrBody = smr;
                    break;
                }
            }

            if (smrBody == null)
            {
                Debug.LogError("No SkinnedMeshRenderer named 'Body' or 'body' found on the model");
                return 0f;
            }

            var bones = smrBody.bones;
            if (bones == null || bones.Length == 0)
            {
                Debug.LogError("No bones found on the model");
                return 0f;
            }

            var minY = float.MaxValue;
            var maxY = float.MinValue;

            foreach (var bone in bones)
            {
                float boneY = bone.position.y;
                if (boneY < minY)
                {
                    minY = boneY;
                }

                if (boneY > maxY)
                {
                    maxY = boneY;
                }
            }

            var height = maxY - minY;
            return height;
        }
    }
}
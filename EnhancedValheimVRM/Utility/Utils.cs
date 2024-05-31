using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class Utils
    {
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

            float minY = float.MaxValue;
            float maxY = float.MinValue;

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

            float height = maxY - minY;
            return height;
        }
    }
}
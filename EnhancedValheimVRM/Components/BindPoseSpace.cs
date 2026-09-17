using UnityEngine;

namespace EnhancedValheimVRM
{
    // a skinned mesh renders from its bones and bind poses, its own node transform is ignored. some
    // exporters write bind poses relative to that node, others relative to the model root, and both
    // render fine. we can only tell by comparing with the skeleton while it is still in rest pose,
    // which is right after import. the answer is kept on the renderer so clones carry it.
    public sealed class BindPoseSpace : MonoBehaviour
    {
        public bool UseRoot;

        public static void Detect(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null || !mesh.isReadable || renderer.GetComponent<BindPoseSpace>() != null) continue;
                var bones = renderer.bones;
                var bindposes = mesh.bindposes;
                int viaRoot = 0, viaRenderer = 0;
                for (var i = 0; i < bones.Length && i < bindposes.Length; i++)
                {
                    if (bones[i] == null) continue;
                    var rest = bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
                    var live = bones[i].position;
                    var rootError = (root.transform.TransformPoint(rest) - live).sqrMagnitude;
                    var rendererError = (renderer.transform.TransformPoint(rest) - live).sqrMagnitude;
                    if (rootError < rendererError)
                        viaRoot++;
                    else
                        viaRenderer++;
                }

                renderer.gameObject.AddComponent<BindPoseSpace>().UseRoot = viaRoot > viaRenderer;
            }
        }
    }
}

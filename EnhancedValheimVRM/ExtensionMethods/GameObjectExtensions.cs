using UnityEngine;

namespace EnhancedValheimVRM
{
    internal static class GameObjectExtensions
    {
        public static void SetVisible(this GameObject obj, bool flag)
        {
            foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = flag;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = flag;
        }
    }
}
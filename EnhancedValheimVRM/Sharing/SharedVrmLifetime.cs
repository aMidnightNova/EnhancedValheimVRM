using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnhancedValheimVRM
{
    // Cache templates own imported assets; player/corpse clones own only their material copies.
    public sealed class SharedVrmLifetime : MonoBehaviour
    {
        public readonly List<Object> ExtraResources = new List<Object>();
        internal readonly HashSet<Material> ConvertedMaterials = new HashSet<Material>();
        internal Action CacheReleased;
        internal event Action<Material> MaterialReady;

        internal void NotifyMaterial(Material material)
        {
            if (material != null) ConvertedMaterials.Add(material);
            MaterialReady?.Invoke(material);
        }

        public void Release()
        {
            foreach (var resource in ExtraResources)
                if (resource != null)
                    Destroy(resource);
            ExtraResources.Clear();
            ConvertedMaterials.Clear();
            MaterialReady = null;
            var release = CacheReleased;
            CacheReleased = null;
            release?.Invoke();
        }

        private void OnDestroy() => Release();
    }
}

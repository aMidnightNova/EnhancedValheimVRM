using System.Collections.Generic;
using UniGLTF;
using UniVRM10;
using VRM;
using VRMShaders;

namespace EnhancedValheimVRM
{
    // with the texture fix on the mod swaps materials to the games shader and only keeps the albedo,
    // normal and emission maps. univrm merges metallic and occlusion into one map with a gpu readback
    // that stalls the main thread, so that slot is dropped before it gets there.
    internal sealed class TextureFixMaterialGenerator : IMaterialDescriptorGenerator
    {
        private readonly IMaterialDescriptorGenerator _inner;

        private TextureFixMaterialGenerator(IMaterialDescriptorGenerator inner)
        {
            _inner = inner;
        }

        internal static IMaterialDescriptorGenerator For(VRMData data)
        {
            return new TextureFixMaterialGenerator(new BuiltInVrmMaterialDescriptorGenerator(data.VrmExtension));
        }

        internal static IMaterialDescriptorGenerator For(Vrm10Data data)
        {
            return new TextureFixMaterialGenerator(new BuiltInVrm10MaterialDescriptorGenerator());
        }

        public MaterialDescriptor Get(GltfData data, int i)
        {
            return WithoutMergedMap(_inner.Get(data, i));
        }

        public MaterialDescriptor GetGltfDefault()
        {
            return WithoutMergedMap(_inner.GetGltfDefault());
        }

        private static MaterialDescriptor WithoutMergedMap(MaterialDescriptor source)
        {
            var slots = new Dictionary<string, TextureDescriptor>();
            var dropped = false;
            foreach (var pair in source.TextureSlots)
            {
                if (pair.Value.TextureType == TextureImportTypes.StandardMap)
                    dropped = true;
                else
                    slots[pair.Key] = pair.Value;
            }

            if (!dropped) return source;
            return new MaterialDescriptor(source.Name,
                source.Shader,
                source.RenderQueue,
                slots,
                source.FloatValues,
                source.Colors,
                source.Vectors,
                source.Actions);
        }
    }
}

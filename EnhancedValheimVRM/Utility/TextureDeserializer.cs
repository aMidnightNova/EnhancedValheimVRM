using System.Collections.Generic;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;


namespace EnhancedValheimVRM
{
    public sealed class TextureDeserializerAsync : ITextureDeserializer
    {
        // univrm hands over an images bytes with no name, a failed one is matched back to the files
        // image list by its size
        private readonly List<KeyValuePair<int, string>> _images = new List<KeyValuePair<int, string>>();

        public static TextureDeserializerAsync For(object parsed)
        {
            var deserializer = new TextureDeserializerAsync();
            var gltf = parsed is VRM.VRMData vrm0 ? vrm0.Data.GLTF : (parsed as UniVRM10.Vrm10Data)?.Data.GLTF;
            if (gltf?.images == null || gltf.bufferViews == null) return deserializer;
            for (var i = 0; i < gltf.images.Count; i++)
            {
                var image = gltf.images[i];
                if (image == null || image.bufferView < 0 || image.bufferView >= gltf.bufferViews.Count) continue;
                var name = string.IsNullOrEmpty(image.name) ? "image " + i : image.name + " (image " + i + ")";
                deserializer._images.Add(new KeyValuePair<int, string>(gltf.bufferViews[image.bufferView].byteLength,
                    name));
            }

            return deserializer;
        }

        private string NameOf(int bytes)
        {
            var names = new List<string>();
            foreach (var image in _images)
            {
                if (image.Key == bytes) names.Add(image.Value);
            }

            return names.Count == 0 ? "with no name" : string.Join(" or ", names);
        }

        public async Task<Texture2D> LoadTextureAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            // an image with no bytes in the file
            if (textureInfo.ImageData == null || textureInfo.ImageData.Length == 0) return Blank(textureInfo);
            var settings = new AsyncImageLoader.LoaderSettings();
            settings.linear = textureInfo.ColorSpace == UniGLTF.ColorSpace.Linear;

            switch (textureInfo.DataMimeType)
            {
                case "image/png":
                    settings.format = AsyncImageLoader.FreeImage.Format.FIF_PNG;
                    break;
                case "image/jpg":
                case "image/jpeg":
                    settings.format = AsyncImageLoader.FreeImage.Format.FIF_JPEG;
                    break;
                default:
                    if (string.IsNullOrEmpty(textureInfo.DataMimeType))
                        Logger.Log($"Texture image MIME type is empty.");
                    else
                        Logger.Log($"Texture image MIME type `{textureInfo.DataMimeType}` is not supported.");

                    break;
            }

            // names the texture in the timing log if the longest main thread stretch lands here
            if (Settings.LogLoadTiming && awaitCaller is PersistentImportAwaitCaller caller)
                caller.Phase = "texture " + textureInfo.DataMimeType + " " + textureInfo.ImageData.Length + " bytes";

            var label = FrameClock.Reporting
                ? textureInfo.DataMimeType + " " + textureInfo.ImageData.Length + " bytes"
                : null;
            if (label != null) FrameClock.Label = "decoding " + label;
            Texture2D texture = null;

            texture = await AsyncImageLoader.CreateFromImageAsync(textureInfo.ImageData, settings);
            if (label != null) FrameClock.Label = "uploaded " + label;
            if (texture == null)
            {
                // the async loader refused it. one missing texture beats losing the avatar
                Logger.LogWarning("Texture " + NameOf(textureInfo.ImageData.Length) + " could not be decoded (" +
                    textureInfo.DataMimeType + ", " + textureInfo.ImageData.Length +
                    " bytes), the avatar loads without it.");
                return Blank(textureInfo);
            }

            texture.wrapModeU = textureInfo.WrapModeU;
            texture.wrapModeV = textureInfo.WrapModeV;
            texture.filterMode = textureInfo.FilterMode;


            return texture;
        }

        // a blank texture instead of null, a null fails the whole import. a new texture holds whatever was
        // in memory and that drew a missing matcap as solid white, so fill it with what adds nothing:
        // flat for a normal map, white for a pbr map, black for the rest
        private static Texture2D Blank(DeserializingTextureInfo textureInfo)
        {
            var texture = new Texture2D(2,
                2,
                TextureFormat.ARGB32,
                textureInfo.UseMipmap,
                textureInfo.ColorSpace == UniGLTF.ColorSpace.Linear);
            var fill = new Color32(0, 0, 0, 255);
            if (textureInfo.ImportTypes == TextureImportTypes.NormalMap)
                fill = new Color32(128, 128, 255, 255);
            else if (textureInfo.ImportTypes == TextureImportTypes.StandardMap) fill = new Color32(255, 255, 255, 255);
            texture.SetPixels32(new[] { fill, fill, fill, fill });
            texture.Apply(textureInfo.UseMipmap, false);
            return texture;
        }
    }
}

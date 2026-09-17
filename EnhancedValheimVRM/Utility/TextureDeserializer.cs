using System.Threading.Tasks;
using UnityEngine;
using VRMShaders;


namespace EnhancedValheimVRM
{
    public sealed class TextureDeserializerAsync : ITextureDeserializer
    {
        public async Task<Texture2D> LoadTextureAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            // univrm asks for both halves of a merged pbr map even when the material only has one texture
            if (textureInfo.ImageData == null || textureInfo.ImageData.Length == 0) return null;
            var settings = new AsyncImageLoader.LoaderSettings();
            settings.linear = textureInfo.ColorSpace == VRMShaders.ColorSpace.Linear;

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
                Logger.LogWarning("Texture could not be decoded (" + textureInfo.DataMimeType + ", " +
                    textureInfo.ImageData.Length + " bytes), the avatar loads without it.");
                return null;
            }

            texture.wrapModeU = textureInfo.WrapModeU;
            texture.wrapModeV = textureInfo.WrapModeV;
            texture.filterMode = textureInfo.FilterMode;


            return texture;
        }
    }
}

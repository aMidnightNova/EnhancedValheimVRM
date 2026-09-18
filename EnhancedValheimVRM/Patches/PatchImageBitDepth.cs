using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace EnhancedValheimVRM
{
    // the async image loader only takes 24 and 32 bit images. a palette or greyscale png and a greyscale
    // jpg decode fine but get refused after that, and came out blank. widen them right after the decode.
    // 16 bit pngs go to 8 bit as well, same as unitys loader, a 16 bit greyscale drew as red only
    internal static class PatchImageBitDepth
    {
        private const int Bitmap = 1, Uint16 = 2, Rgb16 = 9, Rgba16 = 10;

        private static FieldInfo _bitmap, _bitsPerPixel, _imageType;

        [DllImport("FreeImage", EntryPoint = "FreeImage_ConvertTo24Bits")]
        private static extern IntPtr ConvertTo24Bits(IntPtr bitmap);

        [DllImport("FreeImage", EntryPoint = "FreeImage_ConvertTo32Bits")]
        private static extern IntPtr ConvertTo32Bits(IntPtr bitmap);

        [DllImport("FreeImage", EntryPoint = "FreeImage_ConvertToStandardType")]
        private static extern IntPtr ConvertToStandardType(IntPtr bitmap, bool scaleLinear);

        [DllImport("FreeImage", EntryPoint = "FreeImage_IsTransparent")]
        private static extern bool IsTransparent(IntPtr bitmap);

        [DllImport("FreeImage", EntryPoint = "FreeImage_GetBPP")]
        private static extern int GetBitsPerPixel(IntPtr bitmap);

        [DllImport("FreeImage", EntryPoint = "FreeImage_Unload")]
        private static extern void Unload(IntPtr bitmap);

        public static void Apply(Harmony harmony)
        {
            var importer = AccessTools.Inner(typeof(AsyncImageLoader), "ImageImporter");
            var target = importer == null ? null : AccessTools.Method(importer, "DetermineTextureFormat");
            if (target != null)
            {
                _bitmap = AccessTools.Field(importer, "_bitmap");
                _bitsPerPixel = AccessTools.Field(importer, "_imageBitsPerPixel");
                _imageType = AccessTools.Field(importer, "_imageType");
            }

            if (target == null || _bitmap == null || _bitsPerPixel == null || _imageType == null)
            {
                Logger.LogWarning(
                    "Could not hook the image loader, palette and greyscale textures on avatars will be missing.");
                return;
            }

            harmony.Patch(target, new HarmonyMethod(typeof(PatchImageBitDepth), nameof(Widen)));
        }

        // runs on the loaders worker thread, no unity calls in here
        private static void Widen(object __instance)
        {
            try
            {
                WidenBitmap(__instance);
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException)
            {
                // an image library without these calls, the loader carries on as it always did
            }
        }

        private static void WidenBitmap(object __instance)
        {
            var bitmap = (IntPtr)_bitmap.GetValue(__instance);
            if (bitmap == IntPtr.Zero) return;
            var type = Convert.ToInt32(_imageType.GetValue(__instance));
            var bits = (int)_bitsPerPixel.GetValue(__instance);
            var widened = IntPtr.Zero;
            if (type == Bitmap && bits != 24 && bits != 32)
                widened = IsTransparent(bitmap) ? ConvertTo32Bits(bitmap) : ConvertTo24Bits(bitmap);
            else if (type == Rgb16)
                widened = ConvertTo24Bits(bitmap);
            else if (type == Rgba16)
                widened = ConvertTo32Bits(bitmap);
            else if (type == Uint16)
            {
                var grey = ConvertToStandardType(bitmap, true);
                if (grey == IntPtr.Zero) return;
                widened = ConvertTo24Bits(grey);
                Unload(grey);
            }

            // zero is freeimage saying it cannot, the loader then refuses the image like before
            if (widened == IntPtr.Zero) return;
            Unload(bitmap);
            _bitmap.SetValue(__instance, widened);
            _bitsPerPixel.SetValue(__instance, GetBitsPerPixel(widened));
            _imageType.SetValue(__instance, Enum.ToObject(_imageType.FieldType, Bitmap));
        }
    }
}

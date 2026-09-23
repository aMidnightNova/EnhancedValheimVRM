using System;
using System.Collections;
using System.Threading.Tasks;
using HarmonyLib;
using UniGLTF;
using UnityEngine;

namespace EnhancedValheimVRM
{
    // univrm merges a materials metallic roughness and occlusion maps into one unity texture with a
    // shader and a gpu readback, a few hundred ms of main thread per material. same maths here on a
    // worker thread, the main thread only copies pixels in and out.
    [HarmonyPatch(typeof(OcclusionMetallicRoughnessConverter), nameof(OcclusionMetallicRoughnessConverter.Import))]
    internal static class PatchMetallicMerge
    {
        // merges still being packed. the import waits for zero before the avatar can show
        internal static int InFlight { get; private set; }

        private static bool Prefix(Texture2D metallicRoughnessTexture,
            float metallicFactor,
            float roughnessFactor,
            Texture2D occlusionTexture,
            bool isLegacySquaredRoughness,
            ref Texture2D __result)
        {
            var source = metallicRoughnessTexture != null ? metallicRoughnessTexture : occlusionTexture;
            if (source == null) return true;
            var clock = Settings.LogLoadTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (FrameClock.Reporting) FrameClock.Label = "metallic merge " + source.width + "x" + source.height;
            Color32[] metallic, occlusion;
            try
            {
                metallic = metallicRoughnessTexture != null ? metallicRoughnessTexture.GetPixels32() : null;
                occlusion = occlusionTexture != null ? occlusionTexture.GetPixels32() : null;
            }
            catch (UnityException error)
            {
                Logger.LogWarning("Metallic map merge left to univrm, " + error.Message);
                return true;
            }

            int width = source.width, height = source.height;
            var metallicSize = metallicRoughnessTexture != null
                ? new Vector2Int(metallicRoughnessTexture.width, metallicRoughnessTexture.height)
                : default;
            var occlusionSize = occlusionTexture != null
                ? new Vector2Int(occlusionTexture.width, occlusionTexture.height)
                : default;
            var packed = new Color32[width * height];
            var merged = new Texture2D(width, height, TextureFormat.ARGB32, source.mipmapCount > 1, true);
            InFlight++;
            var job = Task.Run(() => Pack(packed,
                width,
                height,
                metallic,
                metallicSize,
                occlusion,
                occlusionSize,
                metallicFactor,
                roughnessFactor,
                isLegacySquaredRoughness));
            CoroutineHelper.Instance.StartCoroutine(TransferToGpu(job, merged, packed, clock));
            if (clock != null)
            {
                Logger.Log("Metallic map merge " + width + "x" + height + ": main thread " +
                    clock.Elapsed.TotalMilliseconds.ToString("F1") + "ms before the worker took it");
            }

            __result = merged;
            return false;
        }

        private static IEnumerator TransferToGpu(Task job,
            Texture2D merged,
            Color32[] packed,
            System.Diagnostics.Stopwatch clock)
        {
            while (!job.IsCompleted) yield return null;
            try
            {
                if (job.IsFaulted)
                    Logger.LogWarning("Metallic map merge failed: " + job.Exception?.GetBaseException().Message);
                else if (merged != null)
                {
                    var uploadStart = clock?.Elapsed.TotalMilliseconds ?? 0;
                    if (FrameClock.Reporting)
                        FrameClock.Label = "metallic merge upload " + merged.width + "x" + merged.height;
                    merged.SetPixels32(packed);
                    merged.Apply(merged.mipmapCount > 1, false);
                    if (clock != null)
                    {
                        Logger.Log("Metallic map merge " + merged.width + "x" + merged.height + ": done after " +
                            clock.Elapsed.TotalMilliseconds.ToString("F0") + "ms, upload " +
                            (clock.Elapsed.TotalMilliseconds - uploadStart).ToString("F1") + "ms");
                    }
                }
            }
            finally
            {
                InFlight--;
            }
        }

        // same as univrms StandardMapImporter shader. both maps are read at the same uv, so a map of
        // another size is sampled nearest
        private static void Pack(Color32[] packed,
            int width,
            int height,
            Color32[] metallic,
            Vector2Int metallicSize,
            Color32[] occlusion,
            Vector2Int occlusionSize,
            float metallicFactor,
            float roughnessFactor,
            bool legacySquaredRoughness)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float roughness = 0, metal = 0, occluded = 0;
                    if (metallic != null)
                    {
                        var m = metallic[Sample(x, y, width, height, metallicSize)];
                        roughness = m.g / 255f * roughnessFactor;
                        metal = m.b / 255f * metallicFactor;
                    }

                    if (occlusion != null) occluded = occlusion[Sample(x, y, width, height, occlusionSize)].r / 255f;
                    if (legacySquaredRoughness) roughness = (float)Math.Sqrt(roughness);
                    packed[y * width + x] = new Color32(Byte(metal), Byte(occluded), 0, Byte(1f - roughness));
                }
            }
        }

        private static int Sample(int x, int y, int width, int height, Vector2Int size)
        {
            if (size.x == width && size.y == height) return y * width + x;
            return y * size.y / height * size.x + x * size.x / width;
        }

        private static byte Byte(float value)
        {
            return (byte)Math.Round(Mathf.Clamp01(value) * 255f);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Shader), "Find")]
    [HarmonyPriority(Priority.VeryHigh)]
    internal static class PatchShaderFind
    {
        // Previous commit's resolution order: game shaders, VRM bundle, native fallback.
        private static readonly Dictionary<string, Shader> GameShaders = new Dictionary<string, Shader>();
        private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        private static bool _started, _ready;
        private static AssetBundle _bundle;
        private static AssetBundleCreateRequest _bundleRequest;

        private static string BundlePath => Path.Combine(Constants.Shaders.Dir, BundleFile(Settings.ShaderBundle));

        // current is the univrm the mod ships with, previous and old stay around for shaders that break on it
        private static string BundleFile(string option)
        {
            if (option == Settings.ShaderOptions.Old) return "OldUniVrm.shaders";
            if (option == Settings.ShaderOptions.Previous) return "PreviousUniVrm.shaders";
            return "UniVrm.shaders";
        }

        private static void CaptureGameShaders()
        {
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (!GameShaders.ContainsKey(shader.name)) GameShaders.Add(shader.name, shader);
            }
        }

        private static void AddShaders(Object[] assets)
        {
            foreach (var asset in assets)
            {
                if (asset is Shader shader) Shaders[shader.name] = shader;
            }
        }

        internal static void EnsureLoadedForMenu()
        {
            if (_ready) return;
            if (!_started)
            {
                CaptureGameShaders();
                _started = true;
            }

            try
            {
                // Like the old local loader, menu construction is synchronous.
                // Reuse an existing request if returning from a world during its load.
                if (_bundle == null)
                {
                    _bundle = _bundleRequest != null
                        ? _bundleRequest.assetBundle
                        : AssetBundle.LoadFromFile(BundlePath);
                }

                if (_bundle == null)
                {
                    Logger.LogError("VRM shader bundle could not be loaded.");
                    return;
                }

                AddShaders(_bundle.LoadAllAssets<Shader>());
            }
            finally
            {
                _ready = true;
            }
        }

        internal static IEnumerator EnsureLoaded()
        {
            if (_started)
            {
                while (!_ready) yield return null;
                yield break;
            }

            _started = true;
            try
            {
                CaptureGameShaders();
                _bundleRequest = AssetBundle.LoadFromFileAsync(BundlePath);
                yield return _bundleRequest;
                if (_ready) yield break;
                _bundle = _bundleRequest.assetBundle;
                if (_bundle == null)
                {
                    Logger.LogError("VRM shader bundle could not be loaded.");
                    yield break;
                }

                var assets = _bundle.LoadAllAssetsAsync<Shader>();
                yield return assets;
                if (!_ready) AddShaders(assets.allAssets);
            }
            finally
            {
                _ready = true;
            }
        }

        private static bool Prefix(ref Shader __result, string name)
        {
            if (!GameShaders.TryGetValue(name, out var shader) && !Shaders.TryGetValue(name, out shader)) return true;
            __result = shader;
            return false;
        }
    }
}

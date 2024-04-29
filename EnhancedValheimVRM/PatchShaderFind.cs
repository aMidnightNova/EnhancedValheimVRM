﻿using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace EnhancedValheimVRM
{
    [HarmonyPatch(typeof(Shader))]
    [HarmonyPatch("Find")]
    internal static class PatchShaderFind
    {
        private static readonly Dictionary<string, Shader> ShaderDictionary = new Dictionary<string, Shader>();

        private static readonly Dictionary<string, Shader> VRMShaderDictionary = new Dictionary<string, Shader>();


        static PatchShaderFind()
        {
            Shader[] allShaders = Resources.FindObjectsOfTypeAll<Shader>();

            foreach (Shader shader in allShaders)
            {
                if (!ShaderDictionary.ContainsKey(shader.name))
                {
                    ShaderDictionary.Add(shader.name, shader);
                }
            }

            Debug.Log("[EnhancedValheimVRM ShaderPatch] All shaders loaded into ShaderDictionary.");


            var shaderFile = "";

            if (Settings.ShaderBundle == Settings.ShaderOptions.Current)
            {
                shaderFile = "UniVrm.shaders";
            }
            else if (Settings.ShaderBundle == Settings.ShaderOptions.Old)
            {
                shaderFile = "OldUniVrm.shaders";
            }
            else
            {
                Debug.LogError("[EnhancedValheimVRM ShaderPatch] Invalid ShaderBundle; old, current");
            }

            var shaderPath = Path.Combine(Constants.VrmDir, shaderFile);


            if (File.Exists(shaderPath))
            {
                var assetBundle = AssetBundle.LoadFromFile(shaderPath);
                var shaders = assetBundle.LoadAllAssets<Shader>();
                foreach (var shader in shaders)
                {
                    Debug.Log("[EnhancedValheimVRM ShaderPatch] Add Shader: " + shader.name);
                    VRMShaderDictionary.Add(shader.name, shader);
                }
            }
        }

        static bool Prefix(ref Shader _shader, string name)
        {
            Shader shader;

            if (VRMShaderDictionary.TryGetValue(name, out shader))
            {
                Debug.Log("[EnhancedValheimVRM ShaderPatch] Shader '" + name + "' found in VRMShaders.Shaders");
                _shader = shader;
                return false;
            }

            if (ShaderDictionary.TryGetValue(name, out shader))
            {
                Debug.Log("[EnhancedValheimVRM ShaderPatch] Shader '" + name + "' found in preloaded ShaderDictionary.");
                _shader = shader;
                return false;
            }

            Debug.Log("[EnhancedValheimVRM ShaderPatch] Shader '" + name + "' NOT FOUND in ShaderDictionary. passing method to original Shader.Find.");
            return true;
        }
    }
}
using System.IO;
using UnityEditor;
using UnityEngine;

// builds the shader bundle EnhancedValheimVRM loads as UniVRM.shaders, from the UniVRM packages in this project.
// shaders are compiled for the graphics apis set for Windows in the player settings (Direct3D11 + Vulkan)
public static class BuildUniVrmShaders
{
    private const string Output = "ShaderBundle";

    [MenuItem("Tools/Build UniVRM shader bundle")]
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        var bundle = new AssetBundleBuild
        {
            assetBundleName = "univrm_v131",
            assetNames = new[]
            {
                "Packages/com.vrmc.univrm/MToon/Shaders/MToon.shader",
                "Packages/com.vrmc.vrm/MToon10/Shaders/vrmc_materials_mtoon.shader",
                "Packages/com.vrmc.gltf/UniUnlit/Shaders/UniUnlit.shader",
                "Packages/com.vrmc.gltf/Resources/UniGLTF/NormalMapExporter.shader",
                "Packages/com.vrmc.gltf/Resources/UniGLTF/StandardMapExporter.shader",
                "Packages/com.vrmc.gltf/Resources/UniGLTF/StandardMapImporter.shader"
            }
        };
        var manifest = BuildPipeline.BuildAssetBundles(Output,
            new[] { bundle },
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            Debug.LogError("Shader bundle build failed, see the errors above");
        else
            Debug.Log("Shader bundle built: " + Path.GetFullPath(Path.Combine(Output, bundle.assetBundleName)));
    }
}

using System;
using System.IO;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Run in Unity 2022.3 with WindowsStandaloneSupport: -executeMethod D3DCompileSmoke.Run.
public static class D3DCompileSmoke
{
    const string Root = "Assets/SmokeResults/D3DCompileSmoke";
    const string ScenePath = Root + "/D3DCompileSmoke.unity";

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("D3D compile smoke needs a graphics-enabled editor.");
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "SmokeResults/D3DCompileSmoke"));
            AssetDatabase.Refresh();

            var sampleNames = new[] { "Lacquered Surface", "Velvet Fabric", "Shiny Surface", "Neon Wireframe", "Tessellated Bumps", "Fur Cards", "Surface Sparkles", "Particle Lifetime", "Audio Hologram", "Volume Nebula", "Volume Carved Orb", "Volume Smoke Ring", "Touch Dent" };
            var sampleRoot = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderEmitter).Assembly).resolvedPath, "Samples~");
            var shaders = sampleNames.Select((name, index) => Emit(File.ReadAllText(Path.Combine(sampleRoot, name + ".nxsg")), "sample_" + index)).ToList();
            // Camera-depth LOD sampling must compile in both the regular vertex
            // path and the tessellation domain, alongside a generated-particle pass.
            foreach (var wrapper in new[] { "core.tessellation", "core.surfaceParticles" })
            {
                var touch = GraphJson.Parse(File.ReadAllText(Path.Combine(sampleRoot, "Touch Dent.nxsg")));
                var wrapperNode = NodeCatalog.Create(wrapper); wrapperNode.Id = "wrapper";
                touch.Nodes.Add(wrapperNode);
                touch.Connections.Single(e => e.To.NodeId == "output").To = new GraphPortRef { NodeId = "wrapper", PortId = "base" };
                touch.Connections.Add(new GraphConnection { Id = "wrapper-output", From = new GraphPortRef { NodeId = "wrapper", PortId = "surface" }, To = new GraphPortRef { NodeId = "output", PortId = "surface" } });
                shaders.Add(Emit(GraphJson.Serialize(touch), "depth_" + wrapper.Substring(5)));
            }
            shaders.Add(Emit(GraphJson.Serialize(Minimal("core.toonSurface")), "minimal_toon"));
            shaders.Add(Emit(GraphJson.Serialize(Minimal("core.pbrSurface")), "minimal_pbr"));

            shaders.Add(Emit(GraphJson.Serialize(Minimal("core.pbrSurface")), "unoptimized_pbr"));
            var graphDirectory = Environment.GetEnvironmentVariable("NXSG_D3D_GRAPHS");
            if (!string.IsNullOrEmpty(graphDirectory))
                foreach (var path in Directory.GetFiles(graphDirectory, "*.nxsg"))
                    shaders.Add(Emit(File.ReadAllText(path), "reported_" + shaders.Count));
            var rawDirectory = Environment.GetEnvironmentVariable("NXSG_D3D_RAW_SHADERS");
            if (!string.IsNullOrEmpty(rawDirectory))
                foreach (var path in Directory.GetFiles(rawDirectory, "*.shader"))
                    shaders.Add(ImportSource(File.ReadAllText(path), "raw_" + shaders.Count));
            var dynamicParticles = GraphJson.Parse(File.ReadAllText(Path.Combine(sampleRoot, "Surface Sparkles.nxsg")));
            var emitter = dynamicParticles.Nodes.First(n => n.Operation == "core.surfaceParticles");
            foreach (var port in new[] { "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread" }) {
                var value = NodeCatalog.Create("core.value"); value.Id = "numeric-" + port; value.Properties["value"] = .25;
                dynamicParticles.Nodes.Add(value);
                dynamicParticles.Connections.RemoveAll(e => e.To.NodeId == emitter.Id && e.To.PortId == port);
                dynamicParticles.Connections.Add(new GraphConnection { Id = value.Id, From = new GraphPortRef { NodeId = value.Id, PortId = "value" }, To = new GraphPortRef { NodeId = emitter.Id, PortId = port } });
            }
            shaders.Add(Emit(GraphJson.Serialize(dynamicParticles), "dynamic_particles"));
            var materials = shaders.Select(CreateMaterial).ToArray();
            BuildScene(materials);
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included = graphics.FindProperty("m_AlwaysIncludedShaders");
            var previousCount = included.arraySize;
            foreach (var shader in shaders) { included.InsertArrayElementAtIndex(included.arraySize); included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader; }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            var previousAuto = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
            var previousApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var compileErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Application.LogCallback onCompileLog = (message, stack, type) => {
                if (message.StartsWith("Shader error in 'NXSG/D3DCompile/", StringComparison.Ordinal)) compileErrors.Enqueue(message);
            };
            Application.logMessageReceivedThreaded += onCompileLog;
            try
            {
                var output = "Temp/NXSGD3DBuild";
                Directory.CreateDirectory(output);
                var manifest = BuildPipeline.BuildAssetBundles(output, new[] {
                    new AssetBundleBuild { assetBundleName = "nxsg-d3d-smoke", assetNames = shaders.Select(AssetDatabase.GetAssetPath).ToArray() }
                }, BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new InvalidOperationException("Windows/D3D shader bundle build failed.");
                if (!compileErrors.IsEmpty) throw new InvalidOperationException("D3D shader variants failed even though Unity produced a bundle: " + string.Join("; ", compileErrors.Distinct().Take(4)));
                foreach (var shader in shaders)
                    if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("D3D shader errors remain: " + shader.name);
            }
            finally {
                Application.logMessageReceivedThreaded -= onCompileLog;
                included.arraySize = previousCount; graphics.ApplyModifiedPropertiesWithoutUndo();
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, previousApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, previousAuto);
            }
            Debug.Log("NXSG D3D COMPILE SMOKE PASSED: " + shaders.Count + " shaders");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static Shader Emit(string json, string id)
    {
        var result = ShaderEmitter.Emit(GraphJson.Parse(json), new EmitterOptions { ShaderName = "NXSG/D3DCompile/" + id, LightVolumesAvailable = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc") != null });
        if (!result.Succeeded) throw new InvalidOperationException(id + ": " + string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        return ImportSource(result.ShaderSource, id);
    }

    static Shader ImportSource(string source, string id)
    {
        source = System.Text.RegularExpressions.Regex.Replace(source, @"Shader ""[^""]+""", $"Shader \"NXSG/D3DCompile/{id}\"");
        // Force the actual D3D stereo code path even when the fixture has no XR
        // provider. Merely building for Windows can strip these variants.
        if (Environment.GetEnvironmentVariable("NXSG_D3D_STEREO") == "1")
        {
            // CGPROGRAM implicitly includes Unity headers before authored code.
            // HLSLPROGRAM lets us set stereo before those same headers, so texture
            // arrays and matrices agree with the stereo vertex output contract.
            source = source.Replace("CGPROGRAM\n", "HLSLPROGRAM\n").Replace("ENDCG", "ENDHLSL")
                .Replace("CGINCLUDE\n", "HLSLINCLUDE\n#if defined(SHADER_API_D3D11)\n#define STEREO_INSTANCING_ON 1\n#endif\n#include \"HLSLSupport.cginc\"\n#include \"UnityShaderVariables.cginc\"\n");
        }
        if (Environment.GetEnvironmentVariable("NXSG_D3D_LEGACY") == "1") source = source.Replace("#pragma require interpolators32\n", string.Empty);
        // Keep the complete declared vertex payload live, as in unoptimized build variants.
        if (id == "unoptimized_pbr") source = source.Replace("CGPROGRAM\n", "CGPROGRAM\n#pragma skip_optimizations d3d11\n").Replace("HLSLPROGRAM\n", "HLSLPROGRAM\n#pragma skip_optimizations d3d11\n");
        var path = Root + "/" + id + ".shader";
        File.WriteAllText(path, source);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException(id + " shader import failed.");
        return shader;
    }

    static Material CreateMaterial(Shader shader)
    {
        var material = new Material(shader) { name = shader.name.Replace('/', '_') };
        AssetDatabase.CreateAsset(material, Root + "/" + material.name + ".mat");
        return material;
    }

    static void BuildScene(Material[] materials)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        for (var i = 0; i < materials.Length; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "D3DCompile_" + i;
            go.transform.position = new Vector3(i % 8, i / 8, 0);
            go.GetComponent<Renderer>().sharedMaterial = materials[i];
        }
        if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath))
            throw new IOException("Could not save D3D compile scene.");
        AssetDatabase.SaveAssets();
    }

    static ShaderGraph Minimal(string operation)
    {
        var graph = new ShaderGraph { GraphId = "d3d-" + operation };
        var surface = NodeCatalog.Create(operation); surface.Id = "surface"; surface.Properties["opacity"] = 1;
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        graph.Nodes.Add(surface); graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection { Id = "surface-output", From = new GraphPortRef { NodeId = "surface", PortId = "surface" }, To = new GraphPortRef { NodeId = "output", PortId = "surface" } });
        return graph;
    }
}

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class NodePackRenderSmoke
{
    const string ShaderPath = "Assets/NodePackCheck.shader";

    public static void Run()
    {
        GameObject subject = null, cameraObject = null;
        Material material = null;
        RenderTexture target = null;
        Texture2D first = null, second = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            ShaderUtil.allowAsyncCompilation = false;
            var firstShader = Compile(CreateGraph(0f), "NXSG/NodePackCheck0");
            material = new Material(firstShader);
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            subject.GetComponent<Renderer>().sharedMaterial = material;
            cameraObject = new GameObject("NXSG Node Pack Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            first = Render(camera, target);
            var secondShader = Compile(CreateGraph(1f), "NXSG/NodePackCheck1");
            material.shader = secondShader;
            second = Render(camera, target);
            var red = first.GetPixel(128, 128);
            var spatialMin = 1f; var spatialMax = 0f; var changed = false;
            for (var y = 32; y < 224; y += 16) for (var x = 32; x < 224; x += 16)
            {
                var a = first.GetPixel(x, y); var b = second.GetPixel(x, y);
                spatialMin = Mathf.Min(spatialMin, a.r); spatialMax = Mathf.Max(spatialMax, a.r);
                if (Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b)) > .01f) changed = true;
            }
            if (red.r <= .1f || red.g >= red.r * .5f || red.b >= red.r * .5f) throw new InvalidOperationException("Emission is not visible: " + red);
            if (spatialMax - spatialMin <= .01f || !changed) throw new InvalidOperationException("Noise has no spatial or time variation.");
            File.WriteAllBytes(Path.Combine(Application.dataPath, "NodePack0.png"), first.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(Application.dataPath, "NodePack1.png"), second.EncodeToPNG());
            Debug.Log("NXSG NODE PACK RENDER CHECK PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first); if (second != null) UnityEngine.Object.DestroyImmediate(second);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.DeleteAsset(ShaderPath);
        }
    }

    static Shader Compile(ShaderGraph graph, string name)
    {
        var result = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = name });
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        File.WriteAllText(Path.Combine(Application.dataPath, "NodePackCheck.shader"), result.ShaderSource);
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Node pack shader failed import.");
        return shader;
    }

    static Texture2D Render(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture.active = target;
        var capture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        capture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); capture.Apply(); return capture;
    }

    static ShaderGraph CreateGraph(float time)
    {
        var graph = new ShaderGraph { GraphId = "node-pack-render" };
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0", Properties = new JObject() });
        graph.Nodes.Add(new GraphNode { Id = "time", Operation = "core.value", Properties = new JObject { ["value"] = time } });
        graph.Nodes.Add(new GraphNode { Id = "noise", Operation = "core.noise", Properties = new JObject { ["scale"] = 5, ["speed"] = 1 } });
        graph.Nodes.Add(new GraphNode { Id = "white", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "red", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 0, 0, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "black", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 0, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "mix", Operation = "core.mix", Properties = new JObject { ["factor"] = .5 } });
        graph.Nodes.Add(new GraphNode { Id = "emission", Operation = "core.emission", Properties = new JObject { ["strength"] = 1 } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        var transform = NodeCatalog.Create("core.uvTransform"); transform.Id = "transform"; graph.Nodes.Add(transform);
        var scroll = NodeCatalog.Create("core.uvScroll"); scroll.Id = "scroll"; graph.Nodes.Add(scroll);
        var clock = NodeCatalog.Create("core.time"); clock.Id = "clock"; clock.Properties["speed"] = 0; clock.Properties["offset"] = time; graph.Nodes.Add(clock);
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "white-texture" } });
        graph.Resources.Add(new GraphResource { Id = "white-texture", Kind = "texture2D", Uri = "builtin://white" });
        foreach (var pair in new[] { new[] { "add", "core.add" }, new[] { "clamp", "core.clamp" }, new[] { "invert1", "core.oneMinus" }, new[] { "invert2", "core.oneMinus" }, new[] { "multiply", "core.multiply" } })
        { var node = NodeCatalog.Create(pair[1]); node.Id = pair[0]; graph.Nodes.Add(node); }
        Connect(graph, "uv", "uv", "transform", "uv", "uv-transform");
        Connect(graph, "transform", "uv", "scroll", "uv", "transform-scroll");
        Connect(graph, "time", "value", "scroll", "time", "time-scroll");
        Connect(graph, "scroll", "uv", "noise", "uv", "scroll-noise");
        Connect(graph, "scroll", "uv", "texture", "uv", "scroll-texture");
        Connect(graph, "clock", "value", "noise", "time", "time-noise");
        Connect(graph, "red", "value", "mix", "a", "red-mix"); Connect(graph, "black", "value", "mix", "b", "black-mix"); Connect(graph, "noise", "value", "mix", "factor", "noise-factor");
        Connect(graph, "mix", "value", "add", "a", "mix-add");
        Connect(graph, "black", "value", "add", "b", "black-add");
        Connect(graph, "add", "value", "clamp", "color", "add-clamp");
        Connect(graph, "clamp", "color", "invert1", "color", "clamp-invert1");
        Connect(graph, "invert1", "color", "invert2", "color", "invert1-invert2");
        Connect(graph, "invert2", "color", "multiply", "a", "invert2-multiply");
        Connect(graph, "texture", "color", "multiply", "b", "texture-multiply");
        Connect(graph, "multiply", "value", "emission", "color", "multiply-emission"); Connect(graph, "black", "value", "toon", "albedo", "black-toon");
        Connect(graph, "emission", "color", "toon", "emission", "emission-toon"); Connect(graph, "toon", "surface", "output", "surface", "toon-output"); return graph;
    }
    static void Connect(ShaderGraph g, string f, string fp, string t, string tp, string id) { g.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = f, PortId = fp }, To = new GraphPortRef { NodeId = t, PortId = tp } }); }
}

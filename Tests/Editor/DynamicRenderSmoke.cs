using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod DynamicRenderSmoke.Run.
public static class DynamicRenderSmoke
{
    const int Size = 96;

    public static void Run()
    {
        GameObject sphere = null, cameraObject = null;
        RenderTexture target = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cameraObject = new GameObject("NXSG Dynamic Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();

            CheckRamp(camera, sphere, target);
            CheckMix(camera, sphere, target);
            CheckMixedMath(camera, sphere, target);
            CheckDivide(camera, sphere, target);
            CheckUnary(camera, sphere, target);
            Debug.Log("NXSG DYNAMIC RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
        }
    }

    static void CheckRamp(Camera camera, GameObject sphere, RenderTexture target)
    {
        foreach (var config in new[] { new[] { 0f, 1f, .25f }, new[] { .5f, .5f, .25f }, new[] { 1f, 0f, .25f } })
        {
            var graph = EmissionGraph();
            graph.Nodes.Add(Float("input", config[2]));
            graph.Nodes.Add(new GraphNode { Id = "ramp", Operation = "core.ramp", Properties = new JObject { ["blackPoint"] = config[0], ["whitePoint"] = config[1], ["smoothness"] = 0 } });
            Connect(graph, "input", "value", "ramp", "value", "input-ramp"); Connect(graph, "ramp", "value", "toon", "emission", "ramp-emission");
            var expected = config[0] == config[1] ? (config[2] >= config[0] ? 1 : 0) : (config[0] < config[1] ? config[2] : 1 - config[2]);
            using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), expected, "ramp"); }
        }
        var custom = EmissionGraph(); custom.Nodes.Add(Float("input", .25)); custom.Nodes.Add(new GraphNode { Id = "ramp", Operation = "core.ramp", Properties = new JObject { ["points"] = new JArray(new JArray(0, 0), new JArray(.5, 1), new JArray(1, 0)), ["smoothness"] = 0 } });
        Connect(custom, "input", "value", "ramp", "value", "input-ramp"); Connect(custom, "ramp", "value", "toon", "emission", "ramp-emission");
        using (var preview = GraphPreview.Create(custom, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), .5f, "custom ramp"); }
    }

    static void CheckMix(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = EmissionGraph(); graph.Nodes.Add(Float("black", 0)); graph.Nodes.Add(Float("white", 1)); graph.Nodes.Add(Float("factor", .25)); graph.Nodes.Add(new GraphNode { Id = "mix", Operation = "core.mix" });
        Connect(graph, "black", "value", "mix", "a", "black-a"); Connect(graph, "white", "value", "mix", "b", "white-b"); Connect(graph, "factor", "value", "mix", "factor", "factor"); Connect(graph, "mix", "value", "toon", "emission", "mix-emission");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), .25f, "float Mix factor"); }
    }

    static void CheckMixedMath(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = EmissionGraph(); graph.Nodes.Add(ColorNode("color", new Color(.1f, .2f, .3f, 1))); graph.Nodes.Add(Float("scalar", .25)); graph.Nodes.Add(new GraphNode { Id = "add", Operation = "core.add" });
        Connect(graph, "color", "value", "add", "a", "color-a"); Connect(graph, "scalar", "value", "add", "b", "scalar-b"); Connect(graph, "add", "value", "toon", "emission", "add-emission");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), new Color(.35f, .45f, .55f, 1), "mixed color math"); }
    }

    static void CheckDivide(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = EmissionGraph(); graph.Nodes.Add(Float("one", 0.000001)); graph.Nodes.Add(Float("zero", 0)); graph.Nodes.Add(new GraphNode { Id = "divide", Operation = "core.divide" });
        Connect(graph, "one", "value", "divide", "a", "one-a"); Connect(graph, "zero", "value", "divide", "b", "zero-b"); Connect(graph, "divide", "value", "toon", "emission", "divide-emission");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), .1f, "scalar divide zero"); }
        graph.Nodes.Single(n => n.Id == "zero").Properties["value"] = -0.00001;
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), -.1f, "scalar divide negative"); }
    }

    static void CheckUnary(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = EmissionGraph(); graph.Nodes.Add(Float("input", .25)); graph.Nodes.Add(new GraphNode { Id = "invert", Operation = "core.oneMinus" }); graph.Nodes.Add(new GraphNode { Id = "clamp", Operation = "core.clamp" });
        Connect(graph, "input", "value", "invert", "color", "input-invert"); Connect(graph, "invert", "color", "clamp", "color", "invert-clamp"); Connect(graph, "clamp", "color", "toon", "emission", "clamp-emission");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; AssertNear(Render(camera, target), .75f, "scalar invert clamp"); }
    }

    static ShaderGraph EmissionGraph()
    {
        var graph = new ShaderGraph { GraphId = "dynamic-render" }; graph.Nodes.Add(ColorNode("albedo", Color.black)); graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "albedo", "value", "toon", "albedo", "black-albedo"); Connect(graph, "toon", "surface", "output", "surface", "toon-output"); return graph;
    }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static GraphNode ColorNode(string id, Color value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(value.r, value.g, value.b, value.a) } }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color Render(Camera camera, RenderTexture target) { camera.Render(); RenderTexture.active = target; var capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply(); var result = capture.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(capture); return result; }
    static void AssertNear(Color actual, float expected, string name) { AssertNear(actual, new Color(expected, expected, expected, 1), name); }
    static void AssertNear(Color actual, Color expected, string name) { if (float.IsNaN(actual.r) || float.IsNaN(actual.g) || float.IsNaN(actual.b) || float.IsInfinity(actual.r) || float.IsInfinity(actual.g) || float.IsInfinity(actual.b) || Mathf.Abs(actual.r - expected.r) > .05f || Mathf.Abs(actual.g - expected.g) > .05f || Mathf.Abs(actual.b - expected.b) > .05f) throw new InvalidOperationException(name + " mismatch: " + actual + " expected " + expected); }
}

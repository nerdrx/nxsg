using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod LivePreviewSmoke.Run.
public static class LivePreviewSmoke
{
    public static void Run()
    {
        GraphPreview red = null, green = null;
        Material context = null;
        GameObject subject = null, cameraObject = null;
        RenderTexture target = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required for live preview smoke.");

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
            context = new Material(Shader.Find("Unlit/Color")) { name = "NXSG Preview Context" };
            context.color = new Color(.2f, .7f, .3f, 1f);
            var contextShader = context.shader;
            var contextColor = context.color;

            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cameraObject = new GameObject("NXSG Live Preview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0f, 0f, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;

            red = GraphPreview.Create(CreateGraph(Color.red), context);
            AssertAssetless(red.Material);
            subject.GetComponent<Renderer>().sharedMaterial = red.Material;
            AssertRed(Render(camera, target));

            green = GraphPreview.Create(CreateGraph(Color.green), context);
            AssertAssetless(green.Material);
            subject.GetComponent<Renderer>().sharedMaterial = green.Material;
            AssertGreen(Render(camera, target));

            var threw = false;
            try { GraphPreview.Create(new ShaderGraph { GraphId = "invalid-preview" }, context); }
            catch (Exception) { threw = true; }
            if (!threw) throw new InvalidOperationException("Invalid graph did not throw.");

            subject.GetComponent<Renderer>().sharedMaterial = red.Material;
            AssertRed(Render(camera, target));
            if (context.shader != contextShader || context.color != contextColor)
                throw new InvalidOperationException("Preview mutated context material shader or properties.");

            var redMaterial = red.Material;
            var greenMaterial = green.Material;
            red.Dispose(); green.Dispose();
            if (redMaterial != null || greenMaterial != null)
                throw new InvalidOperationException("Preview cleanup failed.");
            red = null; green = null;
            CheckMath(camera, target, subject);
            Debug.Log("NXSG LIVE PREVIEW SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (red != null) red.Dispose();
            if (green != null) green.Dispose();
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (context != null) UnityEngine.Object.DestroyImmediate(context);
        }
    }

    static void CheckMath(Camera camera, RenderTexture target, GameObject subject)
    {
        // Signed/zero denominators stay finite, then Subtract/Min/Max produce a known color.
        var graph = CreateGraph(Color.black);
        graph.Connections.RemoveAll(e => e.To.NodeId == "toon" && e.To.PortId == "emission");
        foreach (var op in new[] { "divide", "subtract", "minimum", "maximum" })
        { var node = NodeCatalog.Create("core." + op); node.Id = op; graph.Nodes.Add(node); }
        var colors = new[] { new Color(.000001f, .1f, .05f, 1), new Color(0, -.5f, .25f, 1),
            new Color(-.1f, -.4f, -.1f, 0), new Color(.15f, .3f, .25f, 1), new Color(.1f, .25f, .1f, 1) };
        for (var i = 0; i < colors.Length; i++)
            graph.Nodes.Add(new GraphNode { Id = "c" + i, Operation = "core.constant", Properties = ColorProperties(colors[i]) });
        Connect(graph, "c0", "value", "divide", "a", "c0-div");
        Connect(graph, "c1", "value", "divide", "b", "c1-div");
        Connect(graph, "divide", "value", "subtract", "a", "div-sub");
        Connect(graph, "c2", "value", "subtract", "b", "c2-sub");
        Connect(graph, "subtract", "value", "minimum", "a", "sub-min");
        Connect(graph, "c3", "value", "minimum", "b", "c3-min");
        Connect(graph, "minimum", "value", "maximum", "a", "min-max");
        Connect(graph, "c4", "value", "maximum", "b", "c4-max");
        Connect(graph, "maximum", "value", "toon", "emission", "math-emission");
        using (var math = GraphPreview.Create(graph, null))
        {
            subject.GetComponent<Renderer>().sharedMaterial = math.Material;
            var actual = Render(camera, target);
            var expected = new Color(.15f, .25f, .25f, 1);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) expected = expected.gamma;
            if (Mathf.Abs(actual.r - expected.r) > .03f || Mathf.Abs(actual.g - expected.g) > .03f || Mathf.Abs(actual.b - expected.b) > .03f)
                throw new InvalidOperationException("Math render mismatch: " + actual + " expected " + expected);
        }
        Debug.Log("NXSG MATH RENDER CHECK PASSED");
    }

    static Color Render(Camera camera, RenderTexture target)
    {
        camera.Render();
        RenderTexture.active = target;
        var capture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        capture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        capture.Apply();
        var center = capture.GetPixel(128, 128);
        UnityEngine.Object.DestroyImmediate(capture);
        return center;
    }

    static void AssertRed(Color color) { AssertColor(color, true); }
    static void AssertGreen(Color color) { AssertColor(color, false); }
    static void AssertColor(Color color, bool red)
    {
        if (red ? color.r <= .1f || color.g >= color.r * .5f || color.b >= color.r * .5f
                : color.g <= .1f || color.r >= color.g * .5f || color.b >= color.g * .5f)
            throw new InvalidOperationException("Preview center is wrong color: " + color);
    }

    static void AssertAssetless(Material material)
    {
        if (material == null || material.shader == null || AssetDatabase.GetAssetPath(material) != "" ||
            AssetDatabase.GetAssetPath(material.shader) != "")
            throw new InvalidOperationException("Preview created an asset-backed material or shader.");
    }

    static ShaderGraph CreateGraph(Color color)
    {
        var graph = new ShaderGraph { GraphId = color == Color.red ? "red-preview" : "green-preview" };
        graph.Nodes.Add(new GraphNode { Id = "albedo", Operation = "core.constant", Properties = ColorProperties(Color.black) });
        graph.Nodes.Add(new GraphNode { Id = "emission", Operation = "core.constant", Properties = ColorProperties(color) });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "albedo", "value", "toon", "albedo", "albedo-toon");
        Connect(graph, "emission", "value", "toon", "emission", "emission-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    static JObject ColorProperties(Color color)
    {
        return new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) };
    }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = id,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort }
        });
    }
}

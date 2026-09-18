using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod ParticleSourceUvSmoke.Run.
public static class ParticleSourceUvSmoke
{
    const int Size = 96;
    static GameObject subject;
    static Camera camera;
    static RenderTexture target;
    static Texture2D runtimeTexture;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            subject = CreateSourceTriangle();
            var cameraObject = new GameObject("NXSG Particle Source UV Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; camera.orthographicSize = 2; camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            runtimeTexture = TwoColorTexture();

            CheckSurfaceGraph(false);
            CheckSurfaceGraph(true);
            Debug.Log("NXSG PARTICLE SOURCE UV SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (runtimeTexture != null) UnityEngine.Object.DestroyImmediate(runtimeTexture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        }
    }

    static void CheckSurfaceGraph(bool testEmission)
    {
        var graph = SurfaceGraph(testEmission);
        Color[] spriteUv, sourceUv;
        using (var preview = GraphPreview.Create(graph, null))
        {
            preview.Material.SetTexture("_MainTex", runtimeTexture);
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            spriteUv = Capture();
        }
        graph.Nodes.Single(node => node.Id == "particles").Properties["sourceUV"] = 1;
        using (var preview = GraphPreview.Create(graph, null))
        {
            preview.Material.SetTexture("_MainTex", runtimeTexture);
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            sourceUv = Capture();
        }

        var spriteBlue = CountBlue(spriteUv);
        var sourceBlue = CountBlue(sourceUv);
        var sourceRed = CountRed(sourceUv);
        if (CountRed(spriteUv) < 8 || spriteBlue < 8) throw new InvalidOperationException((testEmission ? "emission" : "albedo") + " sprite UV did not span red and blue texture halves: " + CountRed(spriteUv) + " / " + spriteBlue);
        if (sourceRed < 8 || sourceBlue > 1) throw new InvalidOperationException((testEmission ? "emission" : "albedo") + " source UV did not stay at mesh UV0 (.25,.5): " + sourceRed + " / " + sourceBlue);
    }

    static ShaderGraph SurfaceGraph(bool testEmission)
    {
        var graph = new ShaderGraph { GraphId = "particle-source-uv-smoke" };
        graph.Resources.Add(new GraphResource { Id = "texture", Kind = "texture2D", Uri = "builtin://white" });
        graph.Nodes.Add(ColorConstant("black", new JArray(0, 0, 0, 1)));
        graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" });
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "texture" } });
        graph.Nodes.Add(new GraphNode { Id = "particles", Operation = "core.surfaceParticles", Properties = new JObject { ["density"] = 1, ["size"] = 1, ["speed"] = 0, ["spread"] = 0, ["lifetime"] = 2, ["time"] = 0, ["sourceUV"] = 0, ["blendMode"] = testEmission ? 1 : 0 } });
        graph.Nodes.Add(Float("time", .2));
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "black", "value", "base", "albedo");
        Edge(graph, "base", "surface", "particles", "base");
        Edge(graph, "uv", "uv", "texture", "uv");
        Edge(graph, "texture", "color", "particles", testEmission ? "emission" : "albedo");
        if (testEmission) Edge(graph, "black", "value", "particles", "albedo");
        Edge(graph, "time", "value", "particles", "time");
        Edge(graph, "particles", "surface", "output", "surface");
        return graph;
    }

    static GameObject CreateSourceTriangle()
    {
        var mesh = new Mesh { name = "NXSG Particle Source UV Triangle" };
        mesh.vertices = new[] { new Vector3(-.6f, -.5f, 0), new Vector3(.6f, -.5f, 0), new Vector3(0, .6f, 0) };
        mesh.triangles = new[] { 0, 2, 1 };
        mesh.uv = new[] { new Vector2(.25f, .5f), new Vector2(.25f, .5f), new Vector2(.25f, .5f) };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
        var go = new GameObject("NXSG Particle Source UV Subject"); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>(); return go;
    }

    static Texture2D TwoColorTexture()
    {
        var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels(new[] { Color.red, Color.blue }); texture.Apply(false, false); return texture;
    }

    static Color[] Capture()
    {
        camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply();
        var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
    }

    static int CountRed(Color[] pixels) { return pixels.Count(pixel => pixel.r > .08f && pixel.r > pixel.b * 1.5f); }
    static int CountBlue(Color[] pixels) { return pixels.Count(pixel => pixel.b > .08f && pixel.b > pixel.r * 1.5f); }
    static GraphNode ColorConstant(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}

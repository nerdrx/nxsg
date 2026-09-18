using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod ParallaxRenderSmoke.Run.
public static class ParallaxRenderSmoke
{
    const int Size = 160;
    static GameObject quad;
    static Camera camera;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.localScale = Vector3.one * 2.4f;
            camera = new GameObject("NXSG Parallax Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 1.6f; camera.transform.position = new Vector3(1, 0, -2); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckParallaxUV(); CheckParallaxOcclusion(); CheckNormalFromHeight();
            Debug.Log("NXSG PARALLAX RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (quad != null) UnityEngine.Object.DestroyImmediate(quad); }
    }

    static void CheckParallaxUV()
    {
        var flat = ParallaxGraph("core.parallaxUV", .5, .05, .5, 0); var offset = ParallaxGraph("core.parallaxUV", .8, .15, .5, 0);
        Color[] a, b;
        using (var preview = GraphPreview.Create(flat, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; a = Capture(); }
        using (var preview = GraphPreview.Create(offset, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; b = Capture(); }
        RequireFinite(a); RequireFinite(b); if (Changed(a, b) < 20) throw new InvalidOperationException("parallaxUV height/strength produced too little UV movement");
    }

    static void CheckParallaxOcclusion()
    {
        var gray = RuntimeTexture(.5f); var white = RuntimeTexture(1f);
        try
        {
            Color[] grayPixels, whitePixels;
            var grayGraph = ParallaxGraph("core.parallaxOcclusion", .5, .15, .5, 16); using (var preview = GraphPreview.Create(grayGraph, null)) { SetTexture(preview.Material, gray); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; grayPixels = Capture(); }
            var whiteGraph = ParallaxGraph("core.parallaxOcclusion", .5, .15, .5, 16); using (var preview = GraphPreview.Create(whiteGraph, null)) { SetTexture(preview.Material, white); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; whitePixels = Capture(); }
            RequireFinite(grayPixels); RequireFinite(whitePixels); if (Changed(grayPixels, whitePixels) < 20) throw new InvalidOperationException("POM gray height did not shift UVs versus white height");
            foreach (var steps in new[] { 4, 16, 64 })
            {
                var graph = ParallaxGraph("core.parallaxOcclusion", .5, .15, .5, steps);
                using (var preview = GraphPreview.Create(graph, null)) for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("POM pass failed at steps " + steps);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(gray); UnityEngine.Object.DestroyImmediate(white); }
    }

    static void CheckNormalFromHeight()
    {
        var flat = NormalGraph(false); var tilted = NormalGraph(true); Color flatColor, tiltedColor;
        using (var preview = GraphPreview.Create(flat, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; flatColor = Capture()[Size / 2 * Size + Size / 2]; }
        using (var preview = GraphPreview.Create(tilted, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; tiltedColor = Capture()[Size / 2 * Size + Size / 2]; }
        RequireFinite(new[] { flatColor, tiltedColor }); if (Mathf.Abs(tiltedColor.r - flatColor.r) < .03f) throw new InvalidOperationException("normalFromHeight did not tilt decoded normal");
    }

    static ShaderGraph ParallaxGraph(string operation, double height, double strength, double reference, int steps)
    {
        var graph = new ShaderGraph { GraphId = "parallax-render" }; graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" }); graph.Nodes.Add(Float("height", height));
        var node = NodeCatalog.Create(operation); node.Id = "parallax"; node.Properties["strength"] = strength; node.Properties["reference"] = reference; if (operation == "core.parallaxOcclusion") { node.Properties["steps"] = steps; node.Properties["resourceId"] = "heightTex"; graph.Resources.Add(new GraphResource { Id = "heightTex", Kind = "texture2D", Uri = "builtin://white" }); }
        graph.Nodes.Add(node); graph.Nodes.Add(new GraphNode { Id = "preview", Operation = "core.previewVector" }); graph.Nodes.Add(ColorNode("base", new JArray(.02, .02, .02, 1))); graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "uv", "uv", "parallax", "uv"); if (operation == "core.parallaxUV") Edge(graph, "height", "value", "parallax", "height"); Edge(graph, "parallax", "uv", "preview", "uv"); Edge(graph, "preview", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static ShaderGraph NormalGraph(bool tilted)
    {
        var graph = new ShaderGraph { GraphId = "normal-height-render" }; var normal = NodeCatalog.Create("core.normalFromHeight"); normal.Id = "normal"; graph.Nodes.Add(normal);
        if (tilted)
        {
            normal.Properties["strength"] = 2; graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" }); var gradient = NodeCatalog.Create("core.gradient"); gradient.Id = "gradient"; gradient.Properties["mode"] = 0; gradient.Properties["angle"] = 0; graph.Nodes.Add(gradient); Edge(graph, "uv", "uv", "gradient", "uv"); Edge(graph, "gradient", "value", "normal", "height");
        }
        else { graph.Nodes.Add(Float("height", .5)); Edge(graph, "height", "value", "normal", "height"); }
        graph.Nodes.Add(new GraphNode { Id = "preview", Operation = "core.previewVector" }); graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, "normal", "normal", "preview", "normal"); Edge(graph, "preview", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static Texture2D RuntimeTexture(float value) { var texture = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; texture.SetPixels(Enumerable.Repeat(new Color(value, value, value, 1), 4).ToArray()); texture.Apply(); return texture; }
    static void SetTexture(Material material, Texture2D texture) { for (var i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++) if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), texture); }
    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(delta => delta > .02f); }
    static void RequireFinite(Color[] pixels) { if (pixels.Any(c => float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))) throw new InvalidOperationException("Parallax produced non-finite pixels"); }
}

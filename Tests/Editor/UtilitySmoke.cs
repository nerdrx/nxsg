using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run only in the isolated graphics-enabled Unity test project.
public static class UtilitySmoke
{
    const int Size = 128;
    const string Root = "Assets/SmokeResults/Utility";
    static Camera camera;
    static GameObject quad;
    static RenderTexture target;
    static Texture2D capture;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A graphics device is required.");
            Directory.CreateDirectory(Root);
            var atlasPath = Root + "/numeric-sdf.png";
            TextAtlasUtility.CreateDigitAtlas(atlasPath);
            VerifyAtlas(atlasPath);
            CreateCircleAtlas(Root + "/synthetic-msdf.png");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera = new GameObject("NXSG utility smoke camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
            camera.orthographic = true; camera.orthographicSize = .5f; camera.transform.position = new Vector3(0, 0, -2);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);

            var numericA = NumericGraph(atlasPath, 12.34);
            var imageA = Render(numericA, "numeric-12-34");
            var numericB = NumericGraph(atlasPath, -56.78);
            var imageB = Render(numericB, "numeric-minus-56-78");
            Require(CountInk(imageA) > 100 && CountInk(imageB) > 100, "Numeric text did not render visible digits.");
            Require(Difference(imageA, imageB) > 80, "Changing 12.34 to -56.78 did not change the rendered glyphs.");
            Require(InkAt(imageB, 8, 64) > .2f && InkAt(imageA, 8, 64) < .08f, "Minus sign should render only for the negative value.");
            Require(InkAt(imageA, 88, 16) > .2f && InkAt(imageB, 88, 16) > .2f, "Decimal point should render in both numeric values.");
            Require(InkAt(imageA, 2, 64) < .08f && imageA[64 * Size + 2].a < .08f, "Blank sign slot should keep numeric-text background transparent.");

            var clockGraph = NumericGraph(atlasPath, 0);
            var clock = NodeCatalog.Create(UtilityNodes.Clock); clock.Id = "clock"; clock.Properties["source"] = 1; clock.Properties["period"] = 1.0;
            clockGraph.Connections.RemoveAll(e => e.To.NodeId == "text" && e.To.PortId == "value");
            clockGraph.Nodes.Remove(Node(clockGraph, "value", "core.value"));
            clockGraph.Nodes.Add(clock);
            Wire(clockGraph, "clock", "phase", "text", "value");
            var clockImageA = RenderAt(clockGraph, "clock-phase-025", .25f);
            var clockImageB = RenderAt(clockGraph, "clock-phase-075-network", .75f);
            var unitySource = Node(clockGraph, "clock", UtilityNodes.Clock); unitySource.Properties["source"] = 0;
            var unityImage = RenderAt(clockGraph, "clock-phase-075-unity", .75f);
            Require(Difference(clockImageA, clockImageB) > 20, "Preview time did not animate the network source.");
            Require(Difference(clockImageB, unityImage) < 4, "Editor preview override did not match across clock sources.");

            var stats = NumericGraph(atlasPath, 0);
            stats.Nodes.Remove(Node(stats, "value", "core.value"));
            stats.Nodes.Add(NodeCatalog.Create(UtilityNodes.ViewerStats)); stats.Nodes.Last().Id = "stats";
            stats.Connections.RemoveAll(e => e.To.NodeId == "text" && e.To.PortId == "value");
            Wire(stats, "stats", "renderFps", "text", "value");
            var statsImage = Render(stats, "viewer-render-fps-estimate");
            Require(CountInk(statsImage) > 20, "Viewer Stats render-frame estimate could not drive numeric text.");

            var circleAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/synthetic-msdf.png");
            var msdfGraph = MsdfGraph(circleAsset, 0);
            var fillOnly = Render(msdfGraph, "msdf-fill");
            Node(msdfGraph, "decal", UtilityNodes.MsdfDecal).Properties["outlineWidth"] = 4.0;
            var outlined = Render(msdfGraph, "msdf-outline");
            Require(CountInk(outlined) > CountInk(fillOnly) + 30, "MSDF outline width did not extend the rendered contour.");
            Require(outlined.All(c => IsFinite(c.r) && IsFinite(c.g) && IsFinite(c.b) && IsFinite(c.a)), "MSDF output contained a nonfinite pixel.");

            Debug.Log("NXSG UTILITY GRAPH RENDER PASSED: numeric SDF atlas and values, network-source preview override, Viewer Stats, MSDF fill/outline contour and finite pixels");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    static void VerifyAtlas(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Require(importer != null && !importer.sRGBTexture && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.filterMode == FilterMode.Bilinear && importer.wrapMode == TextureWrapMode.Clamp,
            "Numeric SDF atlas import settings are wrong.");
        var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            Require(ImageConversion.LoadImage(decoded, File.ReadAllBytes(path), false), "Generated SDF PNG could not be decoded.");
            Require(decoded.width == 256 && decoded.height == 192, "Unexpected numeric atlas dimensions.");
            Require(decoded.GetPixel(32, 57).r > .55f && decoded.GetPixel(32, 32).r < .45f, "Digit zero SDF has wrong signed-distance polarity.");
            Require(TextAtlasUtility.GlyphDistance(0, 32, 57) > 0 && TextAtlasUtility.GlyphDistance(0, 32, 32) < 0, "Numeric atlas CPU reference has wrong signed-distance polarity.");
        }
        finally { UnityEngine.Object.DestroyImmediate(decoded); }
    }

    static void CreateCircleAtlas(string path)
    {
        const int n = 64;
        var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[n * n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
        {
            var sd = 18f - Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32));
            var v = Mathf.Clamp01(.5f + sd / 16f); pixels[y * n + x] = new Color(v, v, v, 1);
        }
        texture.SetPixels(pixels); texture.Apply(false, false);
        File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
    }

    static ShaderGraph NumericGraph(string atlasPath, double value)
    {
        var graph = new ShaderGraph { GraphId = "utility-numeric-" + value.ToString("0.00").Replace('-', 'm').Replace('.', '-') };
        AddResource(graph, "atlas", atlasPath);
        var uv = NodeCatalog.Create("core.uv0"); uv.Id = "uv"; graph.Nodes.Add(uv);
        var number = NodeCatalog.Create("core.value"); number.Id = "value"; number.Properties["value"] = value; graph.Nodes.Add(number);
        var text = NodeCatalog.Create(UtilityNodes.NumericText); text.Id = "text"; text.Properties["resourceId"] = "atlas"; text.Properties["digits"] = 4; text.Properties["decimals"] = 2; text.Properties["spacing"] = .08; graph.Nodes.Add(text);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; surface.Properties["useAlbedoAlpha"] = 0; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; output.Properties["renderMode"] = 3; graph.Nodes.Add(output);
        Wire(graph, "uv", "uv", "text", "uv"); Wire(graph, "value", "value", "text", "value");
        Wire(graph, "text", "color", "surface", "albedo"); Wire(graph, "text", "alpha", "surface", "opacity"); Wire(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static ShaderGraph MsdfGraph(Texture2D texture, double outline)
    {
        var graph = new ShaderGraph { GraphId = "utility-msdf-circle" };
        AddResource(graph, "circle", AssetDatabase.GetAssetPath(texture));
        var uv = NodeCatalog.Create("core.uv0"); uv.Id = "uv"; graph.Nodes.Add(uv);
        var decal = NodeCatalog.Create(UtilityNodes.MsdfDecal); decal.Id = "decal"; decal.Properties["resourceId"] = "circle";
        decal.Properties["color"] = new JArray(1, .2, .1, 1); decal.Properties["outlineColor"] = new JArray(.1, .2, 1, 1);
        decal.Properties["outlineWidth"] = outline; decal.Properties["distanceRange"] = 8; graph.Nodes.Add(decal);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; surface.Properties["useAlbedoAlpha"] = 0; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; output.Properties["renderMode"] = 3; graph.Nodes.Add(output);
        Wire(graph, "uv", "uv", "decal", "uv"); Wire(graph, "decal", "color", "surface", "albedo"); Wire(graph, "decal", "alpha", "surface", "opacity"); Wire(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static void AddResource(ShaderGraph graph, string id, string path)
    {
        graph.Resources.Add(new GraphResource { Id = id, Name = id, Kind = "texture2D", Uri = path.Replace('\\', '/') });
        graph.Adapter = new JObject { ["textures"] = new JObject { [id] = AssetDatabase.AssetPathToGUID(path) } };
    }

    static Color[] Render(ShaderGraph graph, string name)
    {
        var emission = ShaderEmitter.Emit(graph);
        Require(emission.Succeeded, name + " failed emission: " + string.Join(";", emission.Diagnostics.Select(d => d.Message)));
        using (var preview = GraphPreview.Create(graph, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), name + " imported a shader with compile errors.");
            quad.GetComponent<Renderer>().sharedMaterial = preview.Material;
            var pixels = Capture(name); Save(graph, pixels, name); return pixels;
        }
    }

    static Color[] RenderAt(ShaderGraph graph, string name, float time)
    {
        var emission = ShaderEmitter.Emit(graph);
        Require(emission.Succeeded, name + " failed emission: " + string.Join(";", emission.Diagnostics.Select(d => d.Message)));
        using (var preview = GraphPreview.Create(graph, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), name + " imported a shader with compile errors.");
            preview.Material.SetFloat("_NXSG_PreviewClock", 1f);
            preview.Material.SetFloat("_NXSG_PreviewTime", time);
            quad.GetComponent<Renderer>().sharedMaterial = preview.Material;
            var pixels = Capture(name); Save(graph, pixels, name); return pixels;
        }
    }

    static Color[] Capture(string name)
    {
        camera.Render(); RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply(false, false);
        return capture.GetPixels();
    }

    static void Save(ShaderGraph graph, Color[] pixels, string name)
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); image.SetPixels(pixels); image.Apply(false, false);
        File.WriteAllBytes(Root + "/" + name + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
        File.WriteAllText(Root + "/" + name + ".nxsg", GraphJson.Serialize(graph, true));
    }

    static int CountInk(Color[] pixels) { return pixels.Count(c => c.r + c.g + c.b > .08f); }
    static float InkAt(Color[] pixels, int x, int y) { var c = pixels[y * Size + x]; return c.r + c.g + c.b; }
    static int Difference(Color[] a, Color[] b)
    { var count = 0; for (var i = 0; i < a.Length; i++) if (Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b) > .15f) count++; return count; }
    static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    static GraphNode Node(ShaderGraph graph, string id, string op) { return graph.Nodes.Single(n => n.Id == id && n.Operation == op); }
    static void Wire(ShaderGraph graph, string from, string port, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + port + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

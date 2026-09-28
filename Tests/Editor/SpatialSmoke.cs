using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Invoke from the editor test harness after SpatialNodes and emitter hooks are integrated.
public static class SpatialSmoke
{
    const int ImageSize = 96;
    const string ParallaxTexturePath = "Assets/SmokeResults/Spatial/parallax-pattern.png";

    public static void Render()
    {
        Camera camera = null; GameObject quad = null; RenderTexture target = null;
        Texture2D parallaxTexture = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required.");
            CheckInspector();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera = new GameObject("NXSG Spatial Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(.5f, 0, -3); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 1.4f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0);
            target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;

            var baseline = Draw(SurfaceGraph("spatial-baseline", false, false), camera, quad, target);
            var neutral = SurfaceGraph("spatial-neutral-render", true, false);
            var neutralDeform = neutral.Nodes.Single(node => node.Id == "deform");
            neutralDeform.Properties["translation"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0);
            neutralDeform.Properties["rotation"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0);
            neutralDeform.Properties["scale"] = new Newtonsoft.Json.Linq.JArray(1, 1, 1);
            var neutralPixels = Draw(neutral, camera, quad, target);
            SaveGraph(neutral);
            Require(baseline[ImageSize / 2, ImageSize / 2].r > .5f, "Baseline quad did not render");
            Require(Changed(baseline, neutralPixels) < 8, "Neutral vertex deformation changed rendered pixels");

            var transformed = SurfaceGraph("spatial-transform-render", true, false);
            transformed.Nodes.Single(node => node.Id == "deform").Properties["translation"] = new Newtonsoft.Json.Linq.JArray(1.4, 0, 0);
            SaveGraph(transformed);
            var moved = Draw(transformed, camera, quad, target);
            Require(Changed(neutralPixels, moved) > 100, "XYZ vertex transform did not change rendered output");

            foreach (var geometry in new[] { "core.tessellation", "core.fur", "core.surfaceParticles", "core.outline", "core.geometryDissolve" })
            {
                var graph = GeometryGraph("spatial-" + geometry, geometry);
                SaveGraph(graph);
                var emitted = ShaderEmitter.Emit(graph);
                Require(emitted.Succeeded, geometry + " deformation graph failed to emit: " + string.Join(";", emitted.Diagnostics.Select(item => item.Message)));
                using (var preview = GraphPreview.Create(graph, null))
                {
                    Require(!ShaderUtil.ShaderHasError(preview.Material.shader), geometry + " deformation shader has compile errors");
                    Require(emitted.ShaderSource.Contains("ShadowCaster"), geometry + " deformation output missed shadow pass");
                }
            }
            parallaxTexture = MakeParallaxTexture();
            var parallax = ParallaxGraph(1);
            SaveGraph(parallax);
            var parallaxPixels = Draw(parallax, camera, quad, target, parallaxTexture);
            Require(parallaxPixels.Cast<Color>().All(color => !float.IsNaN(color.r) && !float.IsInfinity(color.r)), "Infinity parallax rendered nonfinite pixels");
            var frontGraph = TextureGraph(); SaveGraph(frontGraph);
            var frontPixels = Draw(frontGraph, camera, quad, target, parallaxTexture);
            var maskedGraph = ParallaxGraph(0); SaveGraph(maskedGraph);
            var maskedPixels = Draw(maskedGraph, camera, quad, target, parallaxTexture);
            Require(Changed(frontPixels, maskedPixels) < 8, "Zero parallax mask did not preserve the front texture");
            var activeGraph = ParallaxGraph(1, 1.2, .22); activeGraph.GraphId = "spatial-parallax-deep"; SaveGraph(activeGraph);
            var activePixels = Draw(activeGraph, camera, quad, target, parallaxTexture);
            Require(Changed(frontPixels, activePixels) > 40, "Depth and view parallax did not alter the layered pattern");
            Require(activePixels.Cast<Color>().Zip(frontPixels.Cast<Color>(), (layered, front) => layered.a > front.a + .08f).Count(visible => visible) > 8,
                "Layered samples did not contribute through low-alpha front texels");
            foreach (var blend in new[] { 1, 2 })
            {
                var blendGraph = GraphJson.Parse(GraphJson.Serialize(activeGraph)); blendGraph.GraphId = "spatial-parallax-blend-" + blend;
                blendGraph.Nodes.Single(node => node.Id == "interior").Properties["blend"] = blend; SaveGraph(blendGraph);
                var pixels = Draw(blendGraph, camera, quad, target, parallaxTexture);
                Require(pixels.Cast<Color>().All(Finite), "Infinity parallax blend rendered nonfinite pixels");
            }
            Debug.Log("NXSG SPATIAL RENDER SMOKE PASSED: neutral identity, moved geometry, shadow-capable geometry variants, and layered texture output");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
        }
    }

    static void CheckInspector()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        GraphWindow window = null;
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 900, 700);
            typeof(GraphWindow).GetField("livePreview", privateInstance).SetValue(window, false);
            typeof(GraphWindow).GetMethod("NewGraph", privateInstance).Invoke(window, null);
            var graph = (ShaderGraph)typeof(GraphWindow).GetField("graph", privateInstance).GetValue(window);
            graph.Nodes.Clear(); graph.Connections.Clear();
            var deform = NodeCatalog.Create("core.vertexDeform"); deform.Id = "deform"; graph.Nodes.Add(deform);
            typeof(GraphWindow).GetMethod("Rebuild", privateInstance).Invoke(window, null);
            typeof(GraphWindow).GetMethod("SelectNode", privateInstance).Invoke(window, new object[] { "deform", false });
            var inspector = (VisualElement)typeof(GraphWindow).GetField("inspector", privateInstance).GetValue(window);
            var controls = inspector.Query<Vector3Field>().ToList();
            Require(controls.Count(item => item.label == "Translation") == 1 && controls.Count(item => item.label == "Rotation (degrees)") == 1 &&
                controls.Count(item => item.label == "Scale") == 1 && controls.Count(item => item.label == "Pivot") == 1,
                "Vertex Deform XYZ controls are not Vector3 fields");
            foreach (var pair in new[] { new { Port = "translation", Label = "Translation", Z = 2.25f }, new { Port = "rotation", Label = "Rotation (degrees)", Z = 27f },
                new { Port = "scale", Label = "Scale", Z = 1.75f }, new { Port = "pivot", Label = "Pivot", Z = -.5f } })
            {
                inspector = (VisualElement)typeof(GraphWindow).GetField("inspector", privateInstance).GetValue(window);
                var field = inspector.Query<Vector3Field>().ToList().Single(item => item.label == pair.Label);
                field.value = new Vector3(1, 2, pair.Z);
                graph = (ShaderGraph)typeof(GraphWindow).GetField("graph", privateInstance).GetValue(window);
                deform = graph.Nodes.Single(item => item.Id == "deform");
                var saved = (Newtonsoft.Json.Linq.JArray)deform.Properties[pair.Port];
                Require(saved.Count == 3 && Mathf.Abs((float)saved[2] - pair.Z) < .0001f, pair.Port + " Z value did not round-trip");
            }
            var source = NodeCatalog.Create("core.constant"); source.Id = "vector"; source.Properties["valueType"] = "vector3"; source.Properties["value"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0); graph.Nodes.Add(source);
            graph.Connections.Add(new GraphConnection { Id = "drive-translation", From = new GraphPortRef { NodeId = "vector", PortId = "value" }, To = new GraphPortRef { NodeId = "deform", PortId = "translation" } });
            typeof(GraphWindow).GetMethod("Rebuild", privateInstance).Invoke(window, null);
            typeof(GraphWindow).GetMethod("SelectNode", privateInstance).Invoke(window, new object[] { "deform", false });
            inspector = (VisualElement)typeof(GraphWindow).GetField("inspector", privateInstance).GetValue(window);
            Require(!inspector.Query<Vector3Field>().ToList().Single(item => item.label == "Translation").enabledSelf, "Connected translation vector field remained editable");
        }
        finally
        {
            if (window != null) { window.DiscardChanges(); window.Close(); UnityEngine.Object.DestroyImmediate(window); }
        }
    }

    public static void Run(Action<bool, string> assert)
    {
        var neutral = SurfaceGraph("spatial-neutral", true, false);
        var neutralNode = neutral.Nodes.Single(node => node.Id == "deform");
        neutralNode.Properties["translation"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0);
        neutralNode.Properties["rotation"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0);
        neutralNode.Properties["scale"] = new Newtonsoft.Json.Linq.JArray(1, 1, 1);
        var neutralBuild = ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(neutral)));
        assert(GraphValidator.Validate(neutral).IsValid && neutralBuild.Succeeded, "neutral deformation graph builds");
        var transform = SpatialGraph("spatial-transform", true);
        var transformedBuild = ShaderEmitter.Emit(transform);
        assert(GraphValidator.Validate(transform).IsValid && transformedBuild.Succeeded, "full XYZ deformation graph builds");
        assert(transformedBuild.ShaderSource.Contains("NX_Deform") && transformedBuild.ShaderSource.Contains("v.normal=d.normal") &&
            transformedBuild.ShaderSource.Contains("v.tangent=d.tangent"), "vertex deformation updates position, normal and tangent");
        assert(transformedBuild.ShaderSource.Contains("ForwardAdd") && transformedBuild.ShaderSource.Contains("ShadowCaster"),
            "deformation remains present in additive and shadow passes");

        var tessellated = SurfaceGraph("spatial-tessellation", true, true);
        var tessBuild = ShaderEmitter.Emit(tessellated);
        assert(tessBuild.Succeeded && tessBuild.ShaderSource.Contains("domainTess") && tessBuild.ShaderSource.Contains("domainTessAdd") &&
            tessBuild.ShaderSource.Contains("domainTessShadow"), "deformation remains active through tessellated forward, add and shadow stages");

        var parallax = ParallaxGraph();
        var parallaxBuild = ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(parallax)));
        assert(GraphValidator.Validate(parallax).IsValid && parallaxBuild.Succeeded, "infinity parallax resource graph builds after portable round trip");
        assert(parallaxBuild.ShaderSource.Contains("nxI<8") && parallaxBuild.ShaderSource.Contains("nxTint") &&
            parallaxBuild.ShaderSource.Contains("nxSample.a"), "parallax uses bounded depth stack, tint and texture alpha");
    }

    static ShaderGraph SurfaceGraph(string id, bool deformed, bool tessellation)
    {
        var graph = new ShaderGraph { GraphId = id };
        Add(graph, "core.unlitSurface", "surface");
        string source = "surface";
        if (tessellation)
        {
            Add(graph, "core.tessellation", "tess"); Edge(graph, source, "surface", "tess", "base"); source = "tess";
        }
        if (deformed)
        {
            var deform = Add(graph, "core.vertexDeform", "deform");
            if (!id.Contains("neutral"))
            {
                deform.Properties["translation"] = new Newtonsoft.Json.Linq.JArray(.1, .2, .3);
                deform.Properties["rotation"] = new Newtonsoft.Json.Linq.JArray(15, 20, 30);
                deform.Properties["scale"] = new Newtonsoft.Json.Linq.JArray(1.2, .8, 1.1);
                deform.Properties["space"] = 1; deform.Properties["shape"] = 1;
            }
            Edge(graph, source, "surface", "deform", "base"); source = "deform";
        }
        Add(graph, "core.output", "output"); Edge(graph, source, "surface", "output", "surface");
        return graph;
    }

    static ShaderGraph SpatialGraph(string id, bool transform) { return SurfaceGraph(id, transform, false); }

    static ShaderGraph ParallaxGraph(double mask = 1, double depth = .3, double strength = .05)
    {
        var graph = new ShaderGraph { GraphId = mask == 0 ? "spatial-parallax-mask-zero" : "spatial-parallax" };
        var texture = Add(graph, "core.infinityParallax", "interior");
        texture.Properties["resourceId"] = "interior-tex";
        texture.Properties["mask"] = mask; texture.Properties["depth"] = depth; texture.Properties["strength"] = strength;
        AddResource(graph, "interior-tex", ParallaxTexturePath);
        var surface = Add(graph, "core.unlitSurface", "surface"); surface.Properties["useAlbedoAlpha"] = 0;
        var output = Add(graph, "core.output", "output"); output.Properties["renderMode"] = 3;
        Edge(graph, "interior", "color", "surface", "albedo"); Edge(graph, "interior", "alpha", "surface", "opacity"); Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static ShaderGraph TextureGraph()
    {
        var graph = new ShaderGraph { GraphId = "spatial-parallax-front" };
        AddResource(graph, "interior-tex", ParallaxTexturePath);
        Add(graph, "core.uv0", "uv"); var texture = Add(graph, "core.texture2D", "front"); texture.Properties["resourceId"] = "interior-tex";
        var surface = Add(graph, "core.unlitSurface", "surface"); surface.Properties["useAlbedoAlpha"] = 0;
        var output = Add(graph, "core.output", "output"); output.Properties["renderMode"] = 3;
        Edge(graph, "uv", "uv", "front", "uv"); Edge(graph, "front", "color", "surface", "albedo"); Edge(graph, "front", "alpha", "surface", "opacity"); Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static void AddResource(ShaderGraph graph, string id, string path)
    {
        graph.Resources.Add(new GraphResource { Id = id, Name = id, Kind = "texture2D", Uri = path.Replace('\\', '/') });
        graph.Adapter = new Newtonsoft.Json.Linq.JObject { ["textures"] = new Newtonsoft.Json.Linq.JObject { [id] = AssetDatabase.AssetPathToGUID(path) } };
    }

    static Texture2D MakeParallaxTexture()
    {
        const int size = 64;
        Directory.CreateDirectory("Assets/SmokeResults/Spatial");
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var stripe = (x / 8) % 3;
            var alpha = ((x / 8 + y / 8) % 5 == 0) ? .15f : 1f;
            tex.SetPixel(x, y, stripe == 0 ? new Color(1, .05f, .02f, alpha) : stripe == 1 ? new Color(.02f, 1, .08f, alpha) : new Color(.03f, .12f, 1, alpha));
        }
        tex.Apply(false, false);
        File.WriteAllBytes(ParallaxTexturePath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ParallaxTexturePath, ImportAssetOptions.ForceSynchronousImport);
        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(ParallaxTexturePath);
        var importer = AssetImporter.GetAtPath(ParallaxTexturePath) as TextureImporter;
        if (importer != null) { importer.sRGBTexture = true; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Repeat; importer.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ParallaxTexturePath);
    }

    static ShaderGraph GeometryGraph(string id, string geometry)
    {
        var graph = new ShaderGraph { GraphId = id };
        Add(graph, "core.unlitSurface", "surface"); Add(graph, geometry, "geometry"); Add(graph, "core.vertexDeform", "deform");
        Edge(graph, "surface", "surface", "geometry", "base"); Edge(graph, "geometry", "surface", "deform", "base");
        var deform = graph.Nodes.Single(node => node.Id == "deform"); deform.Properties["translation"] = new Newtonsoft.Json.Linq.JArray(.15, .08, 0);
        deform.Properties["rotation"] = new Newtonsoft.Json.Linq.JArray(10, 15, 5);
        deform.Properties["scale"] = new Newtonsoft.Json.Linq.JArray(1.1, .9, 1.05);
        Add(graph, "core.output", "output"); Edge(graph, "deform", "surface", "output", "surface");
        return graph;
    }

    static void SaveGraph(ShaderGraph graph)
    {
        Directory.CreateDirectory("Assets/SmokeResults/Spatial");
        File.WriteAllText("Assets/SmokeResults/Spatial/" + graph.GraphId + ".nxsg", GraphJson.Serialize(graph));
    }

    static Color[,] Draw(ShaderGraph graph, Camera camera, GameObject subject, RenderTexture target, Texture texture = null)
    {
        var emitted = ShaderEmitter.Emit(graph);
        Require(emitted.Succeeded, "Graph emission failed: " + string.Join(";", emitted.Diagnostics.Select(item => item.Message)));
        using (var preview = GraphPreview.Create(graph, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), "Generated shader has compile errors");
            if (texture != null) for (var i = 0; i < ShaderUtil.GetPropertyCount(preview.Material.shader); i++)
                if (ShaderUtil.GetPropertyType(preview.Material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    preview.Material.SetTexture(ShaderUtil.GetPropertyName(preview.Material.shader, i), texture);
            var renderer = subject.GetComponent<Renderer>(); renderer.sharedMaterial = preview.Material;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0); image.Apply();
            var pixels = image.GetPixels(); var result = new Color[ImageSize, ImageSize];
            for (var i = 0; i < pixels.Length; i++) result[i % ImageSize, i / ImageSize] = pixels[i];
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return result;
        }
    }

    static int Changed(Color[,] a, Color[,] b)
    {
        var changed = 0;
        for (var y = 0; y < ImageSize; y++) for (var x = 0; x < ImageSize; x++)
            if (Mathf.Abs(a[x,y].r-b[x,y].r)+Mathf.Abs(a[x,y].g-b[x,y].g)+Mathf.Abs(a[x,y].b-b[x,y].b) > .02f) changed++;
        return changed;
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    static bool Finite(Color color) => new[] { color.r, color.g, color.b, color.a }.All(value => !float.IsNaN(value) && !float.IsInfinity(value));

    static GraphNode Add(ShaderGraph graph, string operation, string id)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node); return node;
    }
    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
}

using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Isolated graphics smoke test for coordinate and scalar utility nodes.
public static class DistortionRenderSmoke
{
    static GameObject quad, cameraObject;
    static Camera camera;
    static RenderTexture target;
    static Mesh mesh;

    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh = UnityEngine.Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
            quad.GetComponent<MeshFilter>().sharedMesh = mesh;
            SetUV(new Vector2(.3f, .4f));

            cameraObject = new GameObject("NXSG Distortion Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = .7f;
            camera.transform.position = new Vector3(0, 0, -3);
            camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;

            var modes = new[] { "noise", "waves", "swirl", "ripple", "flow", "pixelate", "lens" };
            foreach (var mode in modes)
            {
                var graph = UVGraph(mode, "uv", .35f, 1f, 2f, 1f);
                using (var preview = GraphPreview.Create(graph, null))
                    RequireFinite(Render(preview.Material), "uvDistort " + mode + " produced non-finite output");
                foreach (var identity in new[] { UVGraph(mode,"uv",0,1,0,1), UVGraph(mode,"uv",.35,1,0,0) })
                    using(var p=GraphPreview.Create(identity,null)) RequireClose(Render(p.Material)[0],new Color(.3f,.4f,0,1),.025f,"zero strength/mask identity: "+mode);
            }

            foreach (var graph in new[] {
                UVGraph("noise", "uv", 0, 1, 2, 1),
                UVGraph("noise", "uv", .35f, 1, 2, 0),
                UVGraph("flow", "uv", .35f, 1, 2, 1, false) })
            {
                using (var preview = GraphPreview.Create(graph, null))
                    RequireClose(Render(preview.Material)[0], new Color(.3f, .4f, 0, 1), .025f, "identity UV contract failed");
            }

            SetUV(new Vector2(.5f, .5f));
            using (var preview = GraphPreview.Create(UVGraph("swirl", "uv", .8f, 1, 2, 1), null))
                RequireClose(Render(preview.Material)[0], new Color(.5f, .5f, 0, 1), .025f, "swirl center moved");
            SetUV(new Vector2(.3f, .4f));

            var offsetGraph = UVGraph("ripple", "offset", .35f, 1, 0, 1);
            var uvGraph = UVGraph("ripple", "uv", .35f, 1, 0, 1);
            using (var uvPreview = GraphPreview.Create(uvGraph, null))
            using (var offsetPreview = GraphPreview.Create(offsetGraph, null))
            {
                var uv = Render(uvPreview.Material)[0];
                var offset = Render(offsetPreview.Material)[0];
                Require(Mathf.Abs(offset.r - (uv.r - .3f)) < .03f && Mathf.Abs(offset.g - (uv.g - .4f)) < .03f,
                    "uvDistort offset is not distortedUV minus baseUV");
            }

            var evolving = UVGraph("noise", "uv", .35f, 1, 2, 1);
            var time = new GraphNode { Id = "time", Operation = "core.value", Properties = new JObject { ["value"] = .1 } };
            evolving.Nodes.Add(time); Edge(evolving, "time", "value", "distort", "time");
            Color first, second;
            using (var preview = GraphPreview.Create(evolving, null)) first = Render(preview.Material)[0];
            time.Properties["value"] = .9;
            using (var preview = GraphPreview.Create(evolving, null)) second = Render(preview.Material)[0];
            Require(ColorDistance(first, second) > .002f, "noise time input did not change output");

            SetUV(new Vector2(.3f,.4f));
            foreach (var mode in new[] { "linear", "radial", "angular" })
            {
                using (var preview = GraphPreview.Create(ScalarGraph("core.gradient", mode), null))
                    RequireFinite(Render(preview.Material), "gradient " + mode + " produced non-finite output");
                using (var preview = GraphPreview.Create(GradientColorGraph(mode), null))
                    RequireFinite(Render(preview.Material), "gradient color " + mode + " produced non-finite output");
            }
            CheckGradient("linear"); CheckGradient("radial"); CheckGradient("angular");
            foreach (var mode in new[] { "repeat", "mirror", "clamp" })
                using (var preview = GraphPreview.Create(TileGraph(mode), null))
                    RequireFinite(Render(preview.Material), "uvTile " + mode + " produced non-finite output");
            CheckTileClamp(); CheckPosterize();

            SetUV(new Vector2(-.25f,1.25f));
            var mirrored=TileGraph("mirror");var tile=mirrored.Nodes.Single(n=>n.Id=="tile");tile.Properties["tiling"]=new JArray(1,1);tile.Properties["offset"]=new JArray(0,0);
            using(var p=GraphPreview.Create(mirrored,null))RequireClose(Render(p.Material)[0],new Color(.25f,.75f,0,1),.025f,"mirrored negative/odd tiles");
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/dev.nerdrx.nxsg");
            var sample=GraphJson.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(package.resolvedPath,"Samples~/Ripple Tiles.nxsg")));
            using(var p=GraphPreview.Create(sample,null))RequireFinite(Render(p.Material),"Ripple Tiles sample");

            Debug.Log("NXSG DISTORTION RENDER SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static ShaderGraph UVGraph(string mode, string output, double strength, double scale, double speed, double mask, bool connectFlow = true)
    {
        var graph = new ShaderGraph { GraphId = "distortion-render" };
        var uv = new GraphNode { Id = "uv", Operation = "core.uv0" };
        var distort = NodeCatalog.Create("core.uvDistort"); distort.Id = "distort";
        distort.Properties["mode"] = ModeIndex(mode, new[] { "noise", "waves", "swirl", "ripple", "flow", "pixelate", "lens" }); distort.Properties["strength"] = strength; distort.Properties["scale"] = scale;
        distort.Properties["speed"] = speed; distort.Properties["mask"] = mask; distort.Properties["center"] = new JArray(.5, .5);
        distort.Properties["direction"] = new JArray(1, 0); distort.Properties["axes"] = new JArray(1, 1);
        distort.Properties["radius"] = 1; distort.Properties["falloff"] = 1; distort.Properties["detail"] = 3;
        var preview = new GraphNode { Id = "preview", Operation = "core.previewVector" };
        var surface = new GraphNode { Id = "surface", Operation = "core.unlitSurface" };
        var outputNode = new GraphNode { Id = "output", Operation = "core.output" };
        graph.Nodes.Add(uv); graph.Nodes.Add(distort); graph.Nodes.Add(preview); graph.Nodes.Add(surface); graph.Nodes.Add(outputNode);
        Edge(graph, "uv", "uv", "distort", "uv"); Edge(graph, "distort", output, "preview", "uv");
        Edge(graph, "preview", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface");
        if (connectFlow && mode == "flow")
        {
            var flow = new GraphNode { Id = "flow", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(.7, .2, 0, 1) } };
            graph.Nodes.Add(flow); Edge(graph, "flow", "value", "distort", "flow");
        }
        return graph;
    }

    static ShaderGraph ScalarGraph(string operation, string mode)
    {
        var graph = BaseScalarGraph(); var node = NodeCatalog.Create(operation); node.Id = "scalar";
        node.Properties["mode"] = ModeIndex(mode, new[] { "linear", "radial", "angular" }); graph.Nodes.Insert(1, node); Edge(graph, "uv", "uv", "scalar", "uv"); Edge(graph, "scalar", "value", "surface", "albedo"); return graph;
    }

    static ShaderGraph GradientColorGraph(string mode)
    {
        var graph = BaseGraph(); var node = NodeCatalog.Create("core.gradient"); node.Id = "gradient"; node.Properties["mode"] = ModeIndex(mode, new[] { "linear", "radial", "angular" }); graph.Nodes.Insert(1, node);
        Edge(graph, "uv", "uv", "gradient", "uv"); Edge(graph, "gradient", "color", "surface", "albedo"); return graph;
    }

    static ShaderGraph TileGraph(string mode)
    {
        var graph = BaseVectorGraph(); var node = NodeCatalog.Create("core.uvTile"); node.Id = "tile"; node.Properties["mode"] = ModeIndex(mode, new[] { "repeat", "mirror", "clamp" }); node.Properties["tiling"] = new JArray(2, 3); node.Properties["offset"] = new JArray(.1, -.2); graph.Nodes.Insert(1, node); Edge(graph, "uv", "uv", "tile", "uv"); Edge(graph, "tile", "uv", "preview", "uv"); return graph;
    }

    static void CheckGradient(string mode)
    {
        using(var preview=GraphPreview.Create(ScalarGraph("core.gradient",mode),null))
        {
            var actual=Render(preview.Material)[0].r;
            var expected=mode=="linear"?.3f:mode=="radial"?Mathf.Sqrt(.05f)/.5f:Mathf.Atan2(-.1f,-.2f)/(2*Mathf.PI)+.5f;
            Require(Mathf.Abs(actual-expected)<.025f,"gradient formula: "+mode+" "+actual+"/"+expected);
        }
    }

    static void CheckTileClamp() { using (var p = GraphPreview.Create(TileGraph("clamp"), null)) Require(Render(p.Material).All(c => c.r >= -.02f && c.r <= 1.02f && c.g >= -.02f && c.g <= 1.02f), "uvTile clamp escaped range"); }

    static void CheckPosterize()
    {
        var graph = BaseScalarGraph(); var node = NodeCatalog.Create("core.posterize"); node.Id = "posterize"; node.Properties["levels"] = 4; graph.Nodes.Insert(1, node); Edge(graph, "value", "value", "posterize", "value"); Edge(graph, "posterize", "value", "surface", "albedo");
        graph.Nodes.Add(new GraphNode { Id = "value", Operation = "core.value", Properties = new JObject { ["value"] = .63 } });
        using (var p = GraphPreview.Create(graph, null)) { var value = Render(p.Material)[0].r; Require(Mathf.Abs(value - 2f/3f) < .08f, "posterize default levels mismatch"); }
    }

    static ShaderGraph BaseVectorGraph() { var g = BaseGraph(); g.Nodes.Add(new GraphNode { Id = "preview", Operation = "core.previewVector" }); Edge(g, "preview", "color", "surface", "albedo"); return g; }
    static ShaderGraph BaseScalarGraph() { var g = BaseGraph(); return g; }
    static ShaderGraph BaseGraph() { var g = new ShaderGraph { GraphId = "scalar-render" }; g.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" }); g.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); g.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(g, "surface", "surface", "output", "surface"); return g; }
    static void SetUV(Vector2 uv) { mesh.uv = Enumerable.Repeat(uv, mesh.vertexCount).ToArray(); }
    static void SetUVs() { mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }; }
    static Color[] Render(Material material) { quad.GetComponent<Renderer>().sharedMaterial = material; camera.Render(); RenderTexture.active = target; var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply(); var pixels = image.GetPixels(28, 28, 8, 8); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static Texture2D RenderImage(Material material) { quad.GetComponent<Renderer>().sharedMaterial = material; camera.Render(); RenderTexture.active = target; var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply(); RenderTexture.active = null; return image; }
    static void Edge(ShaderGraph g, string from, string fp, string to, string tp) { g.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + tp, From = new GraphPortRef { NodeId = from, PortId = fp }, To = new GraphPortRef { NodeId = to, PortId = tp } }); }
    static void RequireFinite(Color[] colors, string message) { Require(colors.All(IsFinite), message); }
    static bool IsFinite(Color c) { return !(float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsNaN(c.a) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b) || float.IsInfinity(c.a)); }
    static float ColorDistance(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b); }
    static void RequireClose(Color a, Color b, float tolerance, string message) { Require(ColorDistance(a, b) <= tolerance, message + ": " + a); }
    static int ModeIndex(string mode, string[] modes) { var index = Array.IndexOf(modes, mode); if (index < 0) throw new InvalidOperationException("Unknown mode: " + mode); return index; }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

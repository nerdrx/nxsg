using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in an isolated graphics-enabled Unity fixture.
public static class GemSparkleSmoke
{
    const int Size = 256;
    const string Root = "Assets/SmokeResults/GemCompletion";
    static Camera camera;
    static GameObject subject, background;
    static Texture2D backgroundTexture;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            Directory.CreateDirectory(Root + "/D3DGraphs");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            camera = new GameObject("Gem sparkle camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -4); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.orthographic = true; camera.orthographicSize = 1.6f; camera.nearClipPlane = .05f; camera.farClipPlane = 20;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            backgroundTexture = MakeGradient();
            background = GameObject.CreatePrimitive(PrimitiveType.Quad); background.name = "Gem sparkle gradient";
            background.transform.position = new Vector3(0, 0, .8f); background.transform.localScale = new Vector3(5, 5, 1);
            var backgroundMaterial = new Material(Shader.Find("Unlit/Texture")); backgroundMaterial.mainTexture = backgroundTexture;
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere); subject.name = "Gem sparkle subject";
            subject.transform.localScale = Vector3.one * 1.35f;

            var neutral = MakeGraph("neutral");
            var disabled = MakeGraph("disabled-with-controls");
            SetSparkles(disabled, 0, 1, .5, 2);
            var baseline = Capture(neutral);
            var zeroStrength = Capture(disabled);
            Require(Changed(baseline, zeroStrength) == 0, "Sparkle strength zero changed old Gem appearance.");
            Save(baseline, "gem-neutral.png");

            var live = MakeGraph("sparkles"); SetSparkles(live, 6, 1, .5, 2);
            var sparkling = Capture(live);
            Require(Changed(baseline, sparkling) > 8, "Enabled procedural interior sparkles did not change rendered pixels.");
            Save(sparkling, "gem-sparkles.png");
            var colored = MakeGraph("color"); SetSparkles(colored, 6, 1, .5, 2);
            colored.Nodes.Single(node => node.Operation == "core.gem").Properties["sparkleColor"] = new JArray(.05, .2, 1, 1);
            Require(Changed(Capture(colored), sparkling) > 8, "Sparkle color property did not change rendered result.");

            var strength = MakeGraph("strength"); SetSparkles(strength, 2, 1, .5, 2);
            Require(Changed(Capture(strength), sparkling) > 8, "Sparkle strength did not change rendered result.");
            var sizeSmall = MakeGraph("size-small"); SetSparkles(sizeSmall, 6, 1, .035, 2);
            Require(Changed(Capture(sizeSmall), sparkling) > 8, "Sparkle size did not change rendered result.");
            var densityLow = MakeGraph("density-low"); SetSparkles(densityLow, 6, .05, .5, 2);
            Require(Changed(Capture(densityLow), sparkling) > 8, "Sparkle density did not change rendered result.");
            var depthShort = MakeGraph("depth-short"); SetSparkles(depthShort, 6, 1, .5, 0);
            Require(Changed(Capture(depthShort), sparkling) > 8, "Sparkle depth did not change rendered result.");

            var previousCenter = camera.WorldToScreenPoint(subject.transform.position);
            var oldSparkleDelta = SparkleDelta(baseline, sparkling);
            camera.transform.position += new Vector3(.1f, 0, 0);
            var movedCenter = camera.WorldToScreenPoint(subject.transform.position);
            var pixelShift = Mathf.RoundToInt(previousCenter.x - movedCenter.x);
            var neutralParallax = Capture(neutral);
            var parallax = Capture(live);
            Require(ReprojectionChanged(oldSparkleDelta, SparkleDelta(neutralParallax, parallax), pixelShift) > 8,
                "Camera motion did not change sparkle appearance at matched object-space pixels.");
            Save(parallax, "gem-sparkles-parallax.png");

            var degenerate = MakeGraph("degenerate"); SetSparkles(degenerate, 6, 1, 0, 0);
            var zeroNormal = NodeCatalog.Create("core.constant"); zeroNormal.Id = "zero-normal";
            zeroNormal.Properties["valueType"] = "vector3"; zeroNormal.Properties["value"] = new JArray(0, 0, 0);
            degenerate.Nodes.Add(zeroNormal);
            degenerate.Connections.Add(new GraphConnection
            {
                Id = "zero-normal-to-gem",
                From = new GraphPortRef { NodeId = zeroNormal.Id, PortId = "value" },
                To = new GraphPortRef { NodeId = "gem", PortId = "normal" }
            });
            var safePixels = Capture(degenerate);
            Require(safePixels.All(pixel => IsFinite(pixel.r) && IsFinite(pixel.g) && IsFinite(pixel.b)), "Degenerate normal/path produced non-finite pixels.");

            foreach (var graph in new[] { neutral, disabled, live, colored, strength, sizeSmall, densityLow, depthShort, degenerate })
                File.WriteAllText(Root + "/D3DGraphs/" + graph.GraphId + ".nxsg", GraphJson.Serialize(graph));
            AssetDatabase.Refresh();
            Debug.Log("NXSG GEM SPARKLE SMOKE PASSED: neutral baseline, four controls, camera parallax, finite degenerate path; graphs exported for D3D compile.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (backgroundTexture != null) UnityEngine.Object.DestroyImmediate(backgroundTexture);
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (background != null)
            {
                var material = background.GetComponent<Renderer>().sharedMaterial;
                UnityEngine.Object.DestroyImmediate(background);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }

    static Color[] Capture(ShaderGraph graph)
    {
        using (var preview = GraphPreview.Create(graph, null))
        {
            var renderer = subject.GetComponent<Renderer>(); renderer.sharedMaterial = preview.Material;
            var pass = preview.Material.FindPass("ForwardBase");
            Require(pass >= 0 && preview.Material.SetPass(pass), "Gem sparkle forward pass failed.");
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), "Gem sparkle shader has errors.");
            camera.Render(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try { image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
    }

    static ShaderGraph MakeGraph(string id)
    {
        var graph = new ShaderGraph { GraphId = "gem-sparkle-" + id };
        var gem = NodeCatalog.Create("core.gem"); gem.Id = "gem"; gem.Properties["color"] = new JArray(1, 1, 1, 1);
        gem.Properties["refraction"] = 0; gem.Properties["reflection"] = 0; gem.Properties["dispersion"] = 0;
        graph.Nodes.Add(gem);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Edge(graph, gem.Id, "color", surface.Id, "albedo"); Edge(graph, surface.Id, "surface", output.Id, "surface");
        return graph;
    }

    static void SetSparkles(ShaderGraph graph, double strength, double density, double size, double depth)
    {
        var gem = graph.Nodes.Single(node => node.Operation == "core.gem");
        gem.Properties["sparkleStrength"] = strength; gem.Properties["sparkleDensity"] = density;
        gem.Properties["sparkleSize"] = size; gem.Properties["sparkleDepth"] = depth;
    }

    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static int Changed(Color[] a, Color[] b) => a.Zip(b, (x, y) => Mathf.Abs(x.r-y.r) + Mathf.Abs(x.g-y.g) + Mathf.Abs(x.b-y.b)).Count(delta => delta > .05f);
    static Color[] SparkleDelta(Color[] baseline, Color[] sparkling) => baseline.Zip(sparkling, (basePixel, sparklePixel) => new Color(sparklePixel.r-basePixel.r, sparklePixel.g-basePixel.g, sparklePixel.b-basePixel.b, 1)).ToArray();
    static int ReprojectionChanged(Color[] before, Color[] after, int shift)
    {
        var changed = 0;
        for (var y = 64; y < Size - 64; y++) for (var x = 64; x < Size - 64; x++)
        {
            var movedX = x - shift;
            if (movedX < 0 || movedX >= Size) continue;
            var a = before[y * Size + x]; var b = after[y * Size + movedX];
            if (Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b) > .05f) changed++;
        }
        return changed;
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Save(Color[] pixels, string name)
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        try { image.SetPixels(pixels); image.Apply(); File.WriteAllBytes(Root + "/" + name, image.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
    }
    static Texture2D MakeGradient()
    {
        var texture = new Texture2D(128, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (var x = 0; x < texture.width; x++) { var t = x / (float)(texture.width - 1); texture.SetPixel(x, 0, new Color(1-t*.9f, .2f, .1f+t*.9f, 1)); }
        texture.Apply(); return texture;
    }
}

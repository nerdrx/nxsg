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

// Run in hidden graphics-enabled Unity with -executeMethod ToonOutlineDetailSmoke.Run.
public static class ToonOutlineDetailSmoke
{
    const int Size = 128;
    const string Root = "Assets/SmokeResults/ToonOutlineDetail";
    static Camera camera;
    static RenderTexture target;
    static GameObject quad, subject;

    public static void Run()
    {
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(Root);
            Setup();
            var keyObject = new GameObject("Toon detail key");
            var key = keyObject.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1; key.color = Color.white;
            try { CheckBands(key); CheckOutlineLightingAndEmission(key); }
            finally { UnityEngine.Object.DestroyImmediate(keyObject); }
            CheckOutlineWidths();
            Debug.Log("NXSG TOON/OUTLINE DETAIL SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        }
    }

    static void Setup()
    {
        camera = new GameObject("Toon detail camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f, .05f, .07f); camera.allowHDR = true;
        camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
        camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.fieldOfView = 42;
        target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.localScale = Vector3.one * 2;
        quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = 0;
    }

    static void CheckBands(Light key)
    {
        var graph = ToonGraph(1); SaveGraph(graph, "three-band-toon");
        Color[] strip = null;
        var samples = new[] { .1f, .4f, .7f, .95f };
        var expected = new[] { new Color(.8f,.04f,.03f), new Color(.02f,.8f,.05f), new Color(.03f,.08f,.85f), new Color(.03f,.08f,.85f) };
        var colors = new Color[samples.Length];
        using (var preview = GraphPreview.Create(graph, null))
        {
            Compile(preview.Material, "three-band toon"); quad.GetComponent<Renderer>().sharedMaterial = preview.Material;
            for (var i = 0; i < samples.Length; i++)
            {
                var d = samples[i]; key.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sqrt(1 - d * d), 0, -d));
                var pixels = Capture(); colors[i] = pixels[(Size / 2) * Size + Size / 2];
                if (strip == null) strip = new Color[pixels.Length * samples.Length]; PlaceTile(strip, Size * samples.Length, pixels, i);
            }
        }
        Require(colors.All(Finite), "Three-band toon produced non-finite pixels.");
        for (var i = 0; i < colors.Length; i++) Require(Distance(colors[i], expected[i]) < .4f, "Unexpected Toon band color at angle sample " + i + ": " + colors[i] + " expected near " + expected[i]);

        var flat = ToonGraph(0); key.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sqrt(1 - .4f * .4f), 0, -.4f));
        using (var preview = GraphPreview.Create(flat, null)) { Compile(preview.Material, "zero-strength toon"); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; var pixel = Capture()[(Size / 2) * Size + Size / 2]; Require(Distance(pixel, colors[1]) > .05f, "Zero layer strengths did not skip colored shade layers."); }

        var normalZero = ToonGraph(1, 0); var normalOne = ToonGraph(1, 1);
        foreach (var test in new[] { normalZero, normalOne })
        {
            test.Nodes.Add(new GraphNode { Id = "tiltedNormal", Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(1, 0, 0) } });
            Edge(test, "tiltedNormal", "value", "toon", "normal");
        }
        key.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sqrt(1 - .4f * .4f), 0, -.4f));
        Color normalFlat, normalMapped;
        using (var preview = GraphPreview.Create(normalZero, null)) { Compile(preview.Material, "geometric toon normal"); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; normalFlat = Capture()[(Size / 2) * Size + Size / 2]; }
        using (var preview = GraphPreview.Create(normalOne, null)) { Compile(preview.Material, "mapped toon normal"); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; normalMapped = Capture()[(Size / 2) * Size + Size / 2]; }
        Require(Distance(normalFlat, normalMapped) > .15f, "Mapped tangent normal did not change Toon layer response.");
        Save(strip, Size * samples.Length, Size, "toon-light-angle-bands.png");
    }

    static ShaderGraph ToonGraph(double strength, double normalStrength = 1)
    {
        var graph = new ShaderGraph { GraphId = "toon-detail-three-band" };
        var toon = NodeCatalog.Create("core.toonSurface"); toon.Id = "toon";
        toon.Properties["lightingMode"] = 3; toon.Properties["shadowLayers"] = 3;
        toon.Properties["shadeColor"] = new JArray(.8, .04, .03, 1); toon.Properties["shadeColor2"] = new JArray(.02, .8, .05, 1); toon.Properties["shadeColor3"] = new JArray(.03, .08, .85, 1);
        toon.Properties["threshold"] = .5; toon.Properties["threshold2"] = .35; toon.Properties["threshold3"] = .2;
        toon.Properties["softness"] = .04; toon.Properties["softness2"] = .04; toon.Properties["softness3"] = .04;
        toon.Properties["shadowStrength"] = strength; toon.Properties["shadowStrength2"] = strength; toon.Properties["shadowStrength3"] = strength;
        foreach (var suffix in new[] { "", "2", "3" }) toon.Properties["normalStrength" + suffix] = normalStrength;
        graph.Nodes.Add(toon); graph.Nodes.Add(ColorNode("albedo", Color.white)); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "albedo", "value", "toon", "albedo"); Edge(graph, "toon", "surface", "output", "surface");
        var validation = GraphValidator.Validate(graph); Require(validation.IsValid, "Three-band Toon graph failed validation: " + Describe(validation.Diagnostics)); return graph;
    }

    static void CheckOutlineWidths()
    {
        if (quad != null) quad.GetComponent<Renderer>().enabled = false;
        if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        camera.transform.position = Vector3.zero; camera.transform.LookAt(Vector3.forward);
        var counts = new int[4]; var saved = new Color[Size * Size * 4];
        var cases = new[] { new { mode = 0, distance = 3f }, new { mode = 0, distance = 6f }, new { mode = 1, distance = 3f }, new { mode = 1, distance = 6f } };
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var graph = OutlineGraph(c.mode); subject.transform.position = new Vector3(0, 0, c.distance); subject.transform.localScale = Vector3.one * (.8f * c.distance / 3f);
            using (var preview = GraphPreview.Create(graph, null))
            {
                Compile(preview.Material, "outline width mode " + c.mode); subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                var pixels = Capture(); PlaceTile(saved, Size * cases.Length, pixels, i);
                counts[i] = pixels.Count(p => p.r > .7f && p.b > .7f && p.g < .3f);
                Require(pixels.All(Finite), "Outline produced non-finite pixels.");
            }
        }
        Require(counts[0] > counts[1] * 1.35f, "World-width outline did not shrink in screen space with distance: " + string.Join(",", counts));
        Require(Mathf.Abs(counts[2] - counts[3]) < Math.Max(12, counts[2] / 3), "Pixel-width outline changed with camera distance: " + string.Join(",", counts));
        Save(saved, Size * cases.Length, Size, "outline-world-vs-pixel-width.png");
        SaveGraph(OutlineGraph(1), "pixel-width-outline");
    }

    static void CheckOutlineLightingAndEmission(Light key)
    {
        SaveGraph(OutlineGraph(0), "world-width-outline");
        SaveGraph(OutlineGraph(1, 1, .25), "lit-emissive-pixel-outline");
        if (quad != null) quad.GetComponent<Renderer>().enabled = false;
        subject = GameObject.CreatePrimitive(PrimitiveType.Sphere); camera.transform.position = Vector3.zero; camera.transform.LookAt(Vector3.forward);
        subject.transform.position = new Vector3(0, 0, 3); subject.transform.localScale = Vector3.one * .8f;
        Color[] plainPixels, emissionPixels;
        using (var preview = GraphPreview.Create(OutlineGraph(0, 0, 0), null)) { Compile(preview.Material, "unlit outline"); subject.GetComponent<Renderer>().sharedMaterial = preview.Material; plainPixels = Capture(); }
        using (var preview = GraphPreview.Create(OutlineGraph(0, 0, .25), null)) { Compile(preview.Material, "emissive outline"); subject.GetComponent<Renderer>().sharedMaterial = preview.Material; emissionPixels = Capture(); }
        Require(Enumerable.Range(0, plainPixels.Length).Count(i => emissionPixels[i].r > plainPixels[i].r + .15f && emissionPixels[i].b > .7f) > 8, "Outline emission did not brighten rendered hull pixels.");
        using (var preview = GraphPreview.Create(OutlineGraph(0, 1, 0), null))
        {
            Compile(preview.Material, "lit outline"); subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            var rotation = key.transform.rotation; key.enabled = false; var dark = Capture(); key.enabled = true; key.transform.rotation = Quaternion.Euler(0, 180, 0); var bright = Capture(); key.transform.rotation = rotation;
            Require(Changed(dark, bright) > 12, "Outline lighting did not respond to directional light.");
        }
    }

    static ShaderGraph OutlineGraph(int widthMode, double lighting = 0, double emission = 0)
    {
        var graph = new ShaderGraph { GraphId = "outline-detail-mode-" + widthMode };
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "base";
        var outline = NodeCatalog.Create("core.outline"); outline.Id = "outline"; outline.Properties["widthMode"] = widthMode; outline.Properties["width"] = .04; outline.Properties["pixelWidth"] = 2;
        outline.Properties["color"] = new JArray(1, 0, 1, 1); outline.Properties["directionStrength"] = 1; outline.Properties["lighting"] = lighting;
        var graphColor = ColorNode("surfaceColor", new Color(.2f, .65f, .9f, 1)); var zeroDirection = new GraphNode { Id = "zeroDirection", Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(0, 0, 0) } }; var glow = ColorNode("emission", new Color((float)emission, 0, 0, 1));
        graph.Nodes.Add(surface); graph.Nodes.Add(outline); graph.Nodes.Add(graphColor); graph.Nodes.Add(zeroDirection); graph.Nodes.Add(glow); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "surfaceColor", "value", "base", "albedo"); Edge(graph, "zeroDirection", "value", "outline", "direction"); Edge(graph, "emission", "value", "outline", "emission"); Edge(graph, "base", "surface", "outline", "base"); Edge(graph, "outline", "surface", "output", "surface");
        var validation = GraphValidator.Validate(graph); Require(validation.IsValid, "Outline graph failed validation: " + Describe(validation.Diagnostics)); return graph;
    }

    static GraphNode ColorNode(string id, Color color) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) } }; }
    static void Edge(ShaderGraph g, string from, string port, string to, string input) { g.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static void Compile(Material material, string name) { Require(material.shader != null && !ShaderUtil.ShaderHasError(material.shader), name + " shader import failed."); for (var i = 0; i < material.passCount; i++) if (!material.SetPass(i)) throw new InvalidOperationException(name + " pass failed: " + i); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static void Save(Color[] pixels, int width, int height, string name) { var image = new Texture2D(width, height, TextureFormat.RGBA32, false, true); image.SetPixels(pixels); image.Apply(); File.WriteAllBytes(Path.Combine(Root, name), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
    static void PlaceTile(Color[] canvas, int width, Color[] tile, int column) { for (var y = 0; y < Size; y++) Array.Copy(tile, y * Size, canvas, y * width + column * Size, Size); }
    static void SaveGraph(ShaderGraph graph, string name) { File.WriteAllText(Path.Combine(Root, name + ".nxsg"), GraphJson.Serialize(graph)); }
    static bool Finite(Color c) { return !float.IsNaN(c.r) && !float.IsInfinity(c.r) && !float.IsNaN(c.g) && !float.IsInfinity(c.g) && !float.IsNaN(c.b) && !float.IsInfinity(c.b); }
    static float Distance(Color a, Color b) { return Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b)); }
    static int Changed(Color[] a, Color[] b) { return Enumerable.Range(0, a.Length).Count(i => Distance(a[i], b[i]) > .03f); }
    static string Describe(System.Collections.Generic.IReadOnlyList<Diagnostic> diagnostics) { return string.Join("; ", diagnostics.Select(d => d.Code + " at " + d.Path + ": " + d.Message).ToArray()); }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

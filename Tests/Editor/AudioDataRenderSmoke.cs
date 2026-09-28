using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in a hidden graphics-enabled Unity project with -executeMethod AudioDataRenderSmoke.Run.
public static class AudioDataRenderSmoke
{
    const int Size = 64;

    public static void Run()
    {
        RenderTexture target = null;
        Texture2D capture = null;
        Texture2D audio = null;
        Texture2D visualizerAudio = null;
        var previousAudio = Shader.GetGlobalTexture("_AudioTexture");
        var previousSize = Shader.GetGlobalVector("_AudioTexture_TexelSize");
        var previousAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required for AudioLink data render smoke.");
            ShaderUtil.allowAsyncCompilation = false;
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            audio = MakeAudioTexture();
            Shader.SetGlobalTexture("_AudioTexture", audio);
            Shader.SetGlobalVector("_AudioTexture_TexelSize", new Vector4(1f / 128f, 1f / 64f, 128f, 64f));

            Check("audioSpectrum", new JObject { ["frequency"] = 13.75 * Math.Pow(2.0, .5 / 24.0) }, new Color(.5f, .5f, .5f, 1), target, capture, "frequency interpolation");
            Check("audioSpectrumBin", new JObject { ["bin"] = 2, ["channel"] = 2 }, new Color(.9f, .9f, .9f, 1), target, capture, "bin and filtered channel");
            Check("audioThemeColor", new JObject { ["index"] = 2 }, new Color(.2f, .4f, .6f, 1), target, capture, "theme color row");
            Check("audioChronotensity", new JObject { ["index"] = 3, ["band"] = 2 }, new Color(.02148f, .02148f, .02148f, 1), target, capture, "decoded chronotensity");

            Shader.SetGlobalTexture("_AudioTexture", Texture2D.whiteTexture);
            Shader.SetGlobalVector("_AudioTexture_TexelSize", new Vector4(1, 1, 1, 1));
            Check("audioSpectrum", new JObject { ["fallback"] = .37 }, new Color(.37f, .37f, .37f, 1), target, capture, "missing-provider spectrum fallback");
            Check("audioThemeColor", new JObject { ["fallback"] = new JArray(.3, .6, .9, 1) }, new Color(.3f, .6f, .9f, 1), target, capture, "missing-provider theme fallback");

            visualizerAudio = MakeVisualizerAudioTexture();
            Shader.SetGlobalTexture("_AudioTexture", visualizerAudio);
            Shader.SetGlobalVector("_AudioTexture_TexelSize", new Vector4(1f / 128f, 1f / 64f, 128f, 64f));
            var bars = new JObject { ["bars"] = 8, ["radial"] = 0, ["minFrequency"] = 13.75, ["maxFrequency"] = 14080, ["gain"] = 1, ["gap"] = 0 };
            var horizontal = RenderVisualizer(bars, target, capture, "horizontal spectrum bars");
            var horizontalBright = CountBright(horizontal);
            if (horizontalBright < Size * Size * .4 || horizontalBright > Size * Size * .6)
                throw new InvalidOperationException("Horizontal bars expected about half coverage but rendered " + horizontalBright + " bright pixels");

            bars["radial"] = 1;
            var radial = RenderVisualizer(bars, target, capture, "radial spectrum bars");
            var radialBright = CountBright(radial);
            if (radialBright < Size * Size * .14 || radialBright > Size * Size * .26 || radialBright >= horizontalBright * .65)
                throw new InvalidOperationException("Radial bars expected a smaller central disc than horizontal bars but rendered " + radialBright + " of " + horizontalBright + " bright pixels");

            bars["radial"] = 0;
            bars["gap"] = 1;
            var maximumGap = RenderVisualizer(bars, target, capture, "maximum bar gap");
            if (CountBright(maximumGap) >= horizontalBright * .02)
                throw new InvalidOperationException("Maximum gap should suppress bar interiors");

            bars["bars"] = 128;
            bars["gap"] = -100;
            bars["minFrequency"] = 1e-30;
            bars["maxFrequency"] = 1e30;
            var extremeRange = RenderVisualizer(bars, target, capture, "extreme frequency range and gap clamp");
            if (CountBright(extremeRange) < Size * Size * .4 || extremeRange.Any(c => float.IsNaN(c.r) || float.IsInfinity(c.r)))
                throw new InvalidOperationException("Extreme frequency/gap inputs should clamp to finite spectrum output");

            Shader.SetGlobalTexture("_AudioTexture", Texture2D.whiteTexture);
            Shader.SetGlobalVector("_AudioTexture_TexelSize", new Vector4(1, 1, 1, 1));
            bars["gap"] = 0;
            var noProvider = RenderVisualizer(bars, target, capture, "visualizer without provider");
            if (CountBright(noProvider) != 0)
                throw new InvalidOperationException("Visualizer without AudioLink data should render its zero fallback");

            CheckPreview("audioSpectrum", new JObject(), "_NXSG_AudioSpectrumPreview", .62f, new Color(.62f, .62f, .62f, 1), target, capture, null);
            CheckPreview("audioChronotensity", new JObject { ["normalized"] = 1 }, "_NXSG_AudioChronotensityPreview", 1.25f, new Color(.25f, .25f, .25f, 1), target, capture, null);
            CheckPreview("audioThemeColor", new JObject(), "_NXSG_AudioThemePreview", 0, new Color(.7f, .25f, .4f, 1), target, capture, new Color(.7f, .25f, .4f, 1));

            Debug.Log("NXSG AUDIOLINK DATA RENDER SMOKE PASSED: DFT interpolation/bin channels, theme colors, chronotensity decode, provider fallbacks/previews, horizontal/radial bars, gap/frequency clamps");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            Shader.SetGlobalTexture("_AudioTexture", previousAudio);
            Shader.SetGlobalVector("_AudioTexture_TexelSize", previousSize);
            ShaderUtil.allowAsyncCompilation = previousAsync;
            RenderTexture.active = null;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (audio != null) UnityEngine.Object.DestroyImmediate(audio);
            if (visualizerAudio != null) UnityEngine.Object.DestroyImmediate(visualizerAudio);
        }
    }

    static Texture2D MakeAudioTexture()
    {
        var texture = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true)
        {
            name = "NXSG synthetic AudioLink data",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[128 * 64];
        pixels[4 * 128] = new Color(.2f, .1f, .1f, .1f);
        pixels[4 * 128 + 1] = new Color(.8f, .4f, .4f, .4f);
        pixels[4 * 128 + 2] = new Color(.1f, .2f, .9f, .3f);
        pixels[23 * 128 + 2] = new Color(.2f, .4f, .6f, 1f);
        pixels[30 * 128 + 16 + 3] = new Color(100, 2, 0, 0); // index 3, band 2: 100 + 2*1024
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static Texture2D MakeVisualizerAudioTexture()
    {
        var texture = new Texture2D(128, 64, TextureFormat.RGBAFloat, false, true)
        {
            name = "NXSG synthetic AudioLink spectrum",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[128 * 64];
        for (var bin = 0; bin < 240; bin++) pixels[4 * 128 + bin] = new Color(.5f, .5f, .5f, .5f);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static Color[] RenderVisualizer(JObject properties, RenderTexture target, Texture2D capture, string label)
    {
        Color[] pixels;
        using (var preview = GraphPreview.Create(MakeGraph("audioVisualizer", properties), null))
            pixels = Render(preview.Material, target, capture);
        var center = pixels[(Size / 2) * Size + Size / 2];
        if (float.IsNaN(center.r) || float.IsInfinity(center.r)) throw new InvalidOperationException(label + " produced a non-finite center pixel");
        return pixels;
    }

    static int CountBright(Color[] pixels) => pixels.Count(c => c.r > .1f);

    static void Check(string operation, JObject properties, Color expected, RenderTexture target, Texture2D capture, string label)
    {
        using (var preview = GraphPreview.Create(MakeGraph(operation, properties), null))
            RenderAndCheck(preview.Material, expected, target, capture, label);
    }

    static void CheckPreview(string operation, JObject properties, string previewProperty, float previewValue, Color expected, RenderTexture target, Texture2D capture, Color? themePreview)
    {
        Shader.SetGlobalTexture("_AudioTexture", Texture2D.whiteTexture);
        Shader.SetGlobalVector("_AudioTexture_TexelSize", new Vector4(1, 1, 1, 1));
        using (var preview = GraphPreview.Create(MakeGraph(operation, properties), null))
        {
            var material = preview.Material;
            material.SetFloat("_NXSG_AudioDataPreview", 1);
            if (previewProperty == "_NXSG_AudioThemePreview") material.SetColor(previewProperty, themePreview.Value);
            else material.SetFloat(previewProperty, previewValue);
            RenderAndCheck(material, expected, target, capture, operation + " preview");
        }
    }

    static ShaderGraph MakeGraph(string operation, JObject properties)
    {
        var graph = new ShaderGraph { GraphId = "audio-data-render-smoke" };
        var input = NodeCatalog.Create("core." + operation);
        input.Id = "audio";
        foreach (var property in properties.Properties()) input.Properties[property.Name] = property.Value.DeepClone();
        graph.Nodes.Add(input);
        graph.Nodes.Add(NodeCatalog.Create("core.unlitSurface") ?? new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        graph.Nodes[1].Id = "surface";
        graph.Nodes[1].Properties["useAlbedoAlpha"] = 0;
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        graph.Connections.Add(new GraphConnection { Id = "audio-to-albedo", From = new GraphPortRef { NodeId = "audio", PortId = operation == "audioThemeColor" ? "color" : "value" }, To = new GraphPortRef { NodeId = "surface", PortId = "albedo" } });
        graph.Connections.Add(new GraphConnection { Id = "surface-to-output", From = new GraphPortRef { NodeId = "surface", PortId = "surface" }, To = new GraphPortRef { NodeId = "output", PortId = "surface" } });
        return graph;
    }

    static Color[] Render(Material material, RenderTexture target, Texture2D capture)
    {
        Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
        var old = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            capture.Apply();
            return capture.GetPixels();
        }
        finally { RenderTexture.active = old; }
    }

    static void RenderAndCheck(Material material, Color expected, RenderTexture target, Texture2D capture, string label)
    {
        var pixels = Render(material, target, capture);
        var actual = pixels[(Size / 2) * Size + Size / 2];
        if (Mathf.Abs(actual.r - expected.r) > .025f || Mathf.Abs(actual.g - expected.g) > .025f || Mathf.Abs(actual.b - expected.b) > .025f || Mathf.Abs(actual.a - expected.a) > .025f)
            throw new InvalidOperationException(label + " expected " + expected + " but rendered " + actual);
    }
}

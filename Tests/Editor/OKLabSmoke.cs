using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in a hidden graphics-enabled Unity project with -executeMethod OKLabSmoke.Run.
public static class OKLabSmoke
{
    const int Size = 32;
    const float Tau = 6.2831853071795864769f;

    public static void Run()
    {
        RenderTexture target = null; Texture2D capture = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required for hue shift smoke.");
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            Check(CreateGraph(new Color(.19f, .47f, .83f, .37f), "core.hueShift"), target, capture, .137f, "hueShift OKLab rotation");
            Check(CreateGraph(new Color(.19f, .47f, .83f, .37f), "core.colorAdjust"), target, capture, .137f, "colorAdjust OKLab rotation");
            Check(CreateGraph(new Color(.4f, .4f, .4f, .62f), "core.hueShift"), target, capture, .37f, "gray preserves alpha");
            Check(CreateGraph(new Color(2f, 0f, 0f, .35f), "core.colorAdjust"), target, capture, 1f, "HDR full-turn wrap");
            Check(CreateGraph(new Color(-.3f, -.1f, .05f, .4f), "core.hueShift"), target, capture, -.21f, "negative HDR and backward rotation");
            Debug.Log("NXSG OKLAB SMOKE PASSED: OKLab hueShift/colorAdjust, signed cube roots, rotation reference, gamma/linear, gray, HDR wrap, alpha"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (capture != null) UnityEngine.Object.DestroyImmediate(capture); if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } }
    }

    static void Check(ShaderGraph graph, RenderTexture target, Texture2D capture, float hue, string label)
    {
        var value = (JArray)graph.Nodes.Single(n => n.Id == "input").Properties["value"];
        var input = new Color((float)value[0], (float)value[1], (float)value[2], (float)value[3]);
        using (var preview = GraphPreview.Create(graph, null))
        {
            var material = preview.Material; ShaderUtil.allowAsyncCompilation = false;
            Require(material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "Generated hue shader failed Unity compilation.");
            var property = material.shader.FindPropertyIndex("_NXSG_P_hue"); Require(property >= 0, "Hue material property missing.");
            material.SetFloat(material.shader.GetPropertyName(property), hue);
            var old = RenderTexture.active;
            try { Graphics.Blit(Texture2D.whiteTexture, target, material, 0); RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply(); RequireClose(capture.GetPixel(Size / 2, Size / 2), Reference(input, hue), label); }
            finally { RenderTexture.active = old; }
        }
    }

    static ShaderGraph CreateGraph(Color color, string operation)
    {
        var graph = new ShaderGraph { GraphId = "hue-shift-smoke" };
        graph.Parameters.Add(new GraphParameter { Id = "hue", Name = "Hue", Type = GraphValueType.Float, Binding = GraphBindingKind.Material, DefaultValue = new JValue(0d), Exposed = true });
        graph.Nodes.Add(new GraphNode { Id = "input", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) } });
        graph.Nodes.Add(new GraphNode { Id = "hue", Operation = "core.parameter", Properties = new JObject { ["parameterId"] = "hue" } });
        var shift = NodeCatalog.Create(operation); shift.Id = "shift"; shift.Properties["hueSpace"] = 1; graph.Nodes.Add(shift);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; surface.Properties["useAlbedoAlpha"] = 1; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Edge(graph, "input", "value", "shift", "color"); Edge(graph, "hue", "value", "shift", "hue"); Edge(graph, "shift", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static void Edge(ShaderGraph graph, string from, string port, string to, string input) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }

    // Ottosson OKLab matrices. Signed cube roots preserve negative HDR channels.
    static Color Reference(Color source, float turns)
    {
        var r = source.r; var g = source.g; var b = source.b;
        if (QualitySettings.activeColorSpace == ColorSpace.Gamma) { r = SrgbToLinear(r); g = SrgbToLinear(g); b = SrgbToLinear(b); }
        var l = Cbrt(.4122214708f*r + .5363325363f*g + .0514459929f*b); var m = Cbrt(.2119034982f*r + .6806995451f*g + .1073969566f*b); var s = Cbrt(.0883024619f*r + .2817188376f*g + .6299787005f*b);
        var L = .2104542553f*l + .793617785f*m - .0040720468f*s; var a = 1.977998495f*l - 2.428592205f*m + .4505937099f*s; var bb = .0259040371f*l + .7827717662f*m - .808675766f*s;
        var angle = Tau * turns; var ca = Mathf.Cos(angle); var sa = Mathf.Sin(angle); var ar = ca*a - sa*bb; var br = sa*a + ca*bb;
        l = L + .3963377774f*ar + .2158037573f*br; m = L - .1055613458f*ar - .0638541728f*br; s = L - .0894841775f*ar - 1.291485548f*br;
        l=l*l*l; m=m*m*m; s=s*s*s;
        r = 4.0767416621f*l - 3.3077115913f*m + .2309699292f*s; g = -1.2684380046f*l + 2.6097574011f*m - .3413193965f*s; b = -.0041960863f*l - .7034186147f*m + 1.707614701f*s;
        if (QualitySettings.activeColorSpace == ColorSpace.Gamma) { r = LinearToSrgb(r); g = LinearToSrgb(g); b = LinearToSrgb(b); }
        return new Color(r, g, b, source.a);
    }

    static float LinearToSrgb(float value) { var sign = Mathf.Sign(value); value = Mathf.Abs(value); return sign * (value <= .0031308f ? value * 12.92f : 1.055f * Mathf.Pow(value, 1f/2.4f) - .055f); }
    static float Cbrt(float value) { return Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), 1f / 3f); }
    static float SrgbToLinear(float value) { var sign = Mathf.Sign(value); value = Mathf.Abs(value); return sign * (value <= .04045f ? value / 12.92f : Mathf.Pow((value + .055f) / 1.055f, 2.4f)); }
    static void RequireClose(Color actual, Color expected, string label) { const float tolerance = .005f; if (Mathf.Abs(actual.r - expected.r) > tolerance || Mathf.Abs(actual.g - expected.g) > tolerance || Mathf.Abs(actual.b - expected.b) > tolerance || Mathf.Abs(actual.a - expected.a) > .03f) throw new InvalidOperationException(label + " mismatch: expected " + expected + ", got " + actual); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

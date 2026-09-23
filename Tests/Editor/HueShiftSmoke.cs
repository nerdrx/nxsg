using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in a hidden graphics-enabled Unity project with -executeMethod HueShiftSmoke.Run.
public static class HueShiftSmoke
{
    const int Size = 32;

    public static void Run()
    {
        RenderTexture target = null;
        Texture2D capture = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required for hue shift smoke.");

            var graph = CreateGraph(new JArray(2, 0, 0, .35));
            Require(NodeCatalog.Ports("core.hueShift", false).SequenceEqual(new[] { "color", "hue" }),
                "Hue Shift inputs must be color and hue.");
            Require(NodeCatalog.Ports("core.hueShift", true).SequenceEqual(new[] { "color" }),
                "Hue Shift output must be color.");
            var hue = graph.Parameters.Single(p => p.Id == "hue");
            Require(hue.Type == GraphValueType.Float && hue.Name == "Hue" && (double)hue.DefaultValue == 0,
                "Hue parameter must be float, named Hue, default 0.");

            var emitted = ShaderEmitter.Emit(graph);
            Require(emitted.Succeeded, string.Join("; ", emitted.Diagnostics.Select(d => d.Message)));
            Require(emitted.ShaderSource.Contains("NX_HueShift(float4 color, float"),
                "Generated shader missing NX_HueShift helper.");

            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);

            Check(graph, target, capture, 0, new Color(2, 0, 0, .35f), "zero shift");
            Check(graph, target, capture, 1f / 3f, new Color(0, 2, 0, .35f), "one third shift");
            Check(graph, target, capture, 2f / 3f, new Color(0, 0, 2, .35f), "two thirds shift");
            Check(graph, target, capture, 1, new Color(2, 0, 0, .35f), "full turn");
            Check(graph, target, capture, -1f / 3f, new Color(0, 0, 2, .35f), "negative third shift");

            graph = CreateGraph(new JArray(.4, .4, .4, .6));
            Check(graph, target, capture, .37f, new Color(.4f, .4f, .4f, .6f), "grayscale");
            Debug.Log("NXSG HUE SHIFT SMOKE PASSED: generated helper, parameter default, wrap, negative hue, grayscale, alpha, HDR");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            RenderTexture.active = null;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }

    static void Check(ShaderGraph graph, RenderTexture target, Texture2D capture, float hue, Color expected, string label)
    {
        using (var preview = GraphPreview.Create(graph, null))
        {
            var material = preview.Material;
            ShaderUtil.allowAsyncCompilation = false;
            Require(material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),
                "Generated hue shift shader failed Unity compilation.");
            var property = material.shader.FindPropertyIndex("_NXSG_P_hue");
            Require(property >= 0, "Hue material property missing.");
            material.SetFloat(material.shader.GetPropertyName(property), hue);
            var old = RenderTexture.active;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                capture.Apply();
                var actual = capture.GetPixel(Size / 2, Size / 2);
                RequireClose(actual, expected, label);
            }
            finally { RenderTexture.active = old; }
        }
    }

    static ShaderGraph CreateGraph(JArray color)
    {
        var graph = new ShaderGraph { GraphId = "hue-shift-smoke" };
        graph.Parameters.Add(new GraphParameter { Id = "hue", Name = "Hue", Type = GraphValueType.Float,
            Binding = GraphBindingKind.Material, DefaultValue = new JValue(0d), Exposed = true });
        graph.Nodes.Add(new GraphNode { Id = "input", Operation = "core.constant",
            Properties = new JObject { ["valueType"] = "color", ["value"] = color } });
        graph.Nodes.Add(new GraphNode { Id = "hue", Operation = "core.parameter",
            Properties = new JObject { ["parameterId"] = "hue" } });
        graph.Nodes.Add(new GraphNode { Id = "shift", Operation = "core.hueShift" });
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "input", "value", "shift", "color");
        Edge(graph, "hue", "value", "shift", "hue");
        Edge(graph, "shift", "color", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to,
            From = new GraphPortRef { NodeId = from, PortId = port },
            To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static void RequireClose(Color actual, Color expected, string label)
    {
        const float tolerance = .08f;
        if (Mathf.Abs(actual.r - expected.r) > tolerance || Mathf.Abs(actual.g - expected.g) > tolerance ||
            Mathf.Abs(actual.b - expected.b) > tolerance || Mathf.Abs(actual.a - expected.a) > .03f)
            throw new InvalidOperationException(label + " mismatch: expected " + expected + ", got " + actual);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

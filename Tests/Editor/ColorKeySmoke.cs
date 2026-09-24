using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in a hidden graphics-enabled Unity project with -executeMethod ColorKeySmoke.Run.
public static class ColorKeySmoke
{
    const int Size = 32;

    public static void Run()
    {
        RenderTexture target = null;
        Texture2D capture = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required for color-key smoke.");
            target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);

            Check(MaskGraph(new Color(1, 0, 0, .37f), .1f, .1f), target, capture, new Color(1, 1, 1, 1), "mask exact target");
            Check(MaskGraph(new Color(0, 1, 0, .37f), .1f, .1f), target, capture, Color.black, "mask outside target");
            Check(MaskGraph(new Color(.85f, 0, 0, .37f), .1f, .1f), target, capture, new Color(.5f, .5f, .5f, 1), "mask softness midpoint");
            Check(MaskGraph(new Color(1, 0, 0, .37f), 0, 0), target, capture, Color.white, "mask zero softness boundary");

            var live = LiveReplaceGraph();
            Check(live, target, capture, new Color(1, 0, 0, .37f), "replace factor zero", new Dictionary<string, object> { ["factor"] = 0f });
            Check(live, target, capture, new Color(0, 0, 1, .37f), "replace factor one", new Dictionary<string, object> { ["factor"] = 1f });
            Check(live, target, capture, new Color(0, 0, 1, .37f), "replace factor saturated", new Dictionary<string, object> { ["factor"] = 2f });
            Check(live, target, capture, new Color(0, 0, 1, .37f), "negative tolerance clamps", new Dictionary<string, object> { ["factor"] = 1f, ["tolerance"] = -1f });
            Check(live, target, capture, new Color(0, 1, 0, .37f), "connected replacement influences result", new Dictionary<string, object> { ["factor"] = 1f, ["replacement"] = Color.green });
            Check(live, target, capture, new Color(0, 0, 1, .37f), "replacement alpha ignored", new Dictionary<string, object> { ["factor"] = 1f, ["replacement"] = new Color(0, 0, 1, .1f) });
            live.Nodes.Single(n => n.Id == "input").Properties["value"] = new JArray(.85, 0, 0, .37);
            Check(live, target, capture, new Color(.425f, 0, .5f, .37f), "clamped factor preserves soft match", new Dictionary<string, object> { ["factor"] = 2f });
            live.Connections.RemoveAll(edge => edge.To.NodeId == "effect" && (edge.To.PortId == "target" || edge.To.PortId == "replacement"));
            live.Nodes.Single(n => n.Id == "effect").Properties["target"] = new JArray(.85,0,0,1);
            live.Nodes.Single(n => n.Id == "effect").Properties["replacement"] = new JArray(0,.25,.5,.1);
            Check(live, target, capture, new Color(0, .25f, .5f, .37f), "edited color defaults", new Dictionary<string, object> { ["factor"] = 1f });

            Debug.Log("NXSG COLOR KEY SMOKE PASSED: mask distance/softness/clamp and replace blend/factor/alpha/live inputs");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }

    static void Check(ShaderGraph graph, RenderTexture target, Texture2D capture, Color expected, string label, Dictionary<string, object> values = null)
    {
        using (var preview = GraphPreview.Create(graph, null))
        {
            var material = preview.Material; ShaderUtil.allowAsyncCompilation = false;
            Require(material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "Generated color-key shader failed Unity compilation.");
            if (values != null) foreach (var pair in values) SetParameter(material, pair.Key, pair.Value);
            var old = RenderTexture.active;
            try
            {
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply();
                RequireClose(capture.GetPixel(Size / 2, Size / 2), expected, label);
            }
            finally { RenderTexture.active = old; }
        }
    }

    static void SetParameter(Material material, string id, object value)
    {
        var index = material.shader.FindPropertyIndex("_NXSG_P_" + id);
        Require(index >= 0, "Material property missing: " + id);
        var name = material.shader.GetPropertyName(index);
        if (value is Color color) material.SetColor(name, color); else material.SetFloat(name, Convert.ToSingle(value));
    }

    static ShaderGraph MaskGraph(Color input, float tolerance, float softness)
    {
        var graph = BaseGraph("core.colorMask", input, new Color(1, 0, 0, 1), Color.blue);
        graph.Nodes.Single(n => n.Id == "effect").Properties["tolerance"] = tolerance;
        graph.Nodes.Single(n => n.Id == "effect").Properties["softness"] = softness;
        return graph;
    }

    static ShaderGraph LiveReplaceGraph()
    {
        var graph = BaseGraph("core.replaceColor", new Color(1, 0, 0, .37f), Color.red, Color.blue);
        graph.Connections.RemoveAll(edge => edge.To.NodeId == "effect" && edge.To.PortId == "replacement");
        foreach (var id in new[] { "tolerance", "softness", "factor" })
            graph.Parameters.Add(new GraphParameter { Id = id, Name = id, Type = GraphValueType.Float, Binding = GraphBindingKind.Material, DefaultValue = new JValue(id == "factor" ? 1d : .1d), Exposed = true });
        graph.Parameters.Add(new GraphParameter { Id = "replacement", Name = "Replacement", Type = GraphValueType.Color, Binding = GraphBindingKind.Material, DefaultValue = new JArray(0, 0, 1, .1), Exposed = true });
        foreach (var id in new[] { "tolerance", "softness", "factor" })
        {
            var node = new GraphNode { Id = id + "-source", Operation = "core.parameter", Properties = new JObject { ["parameterId"] = id } }; graph.Nodes.Add(node);
            Link(graph, node.Id, "value", "effect", id);
        }
        var replacement = new GraphNode { Id = "replacement-source", Operation = "core.parameter", Properties = new JObject { ["parameterId"] = "replacement" } }; graph.Nodes.Add(replacement);
        Link(graph, replacement.Id, "value", "effect", "replacement");
        return graph;
    }

    static ShaderGraph BaseGraph(string operation, Color input, Color target, Color replacement)
    {
        var graph = new ShaderGraph { GraphId = "color-key-smoke" };
        graph.Nodes.Add(ColorNode("input", input)); graph.Nodes.Add(ColorNode("target", target)); graph.Nodes.Add(ColorNode("replacement", replacement));
        var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; surface.Properties["useAlbedoAlpha"] = operation == "core.replaceColor" ? 1 : 0; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "input", "value", "effect", "color"); Link(graph, "target", "value", "effect", "target");
        if (operation == "core.replaceColor") Link(graph, "replacement", "value", "effect", "replacement");
        Link(graph, "effect", operation == "core.colorMask" ? "value" : "color", "surface", "albedo");
        Link(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static GraphNode ColorNode(string id, Color color)
    {
        return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) } };
    }

    static void Link(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }

    static void RequireClose(Color actual, Color expected, string label)
    {
        if (float.IsNaN(actual.r) || float.IsNaN(actual.g) || float.IsNaN(actual.b) || float.IsNaN(actual.a) ||
            float.IsInfinity(actual.r) || float.IsInfinity(actual.g) || float.IsInfinity(actual.b) || float.IsInfinity(actual.a))
            throw new InvalidOperationException(label + " produced non-finite output: " + actual);
        const float tolerance = .005f;
        if (Mathf.Abs(actual.r - expected.r) > tolerance || Mathf.Abs(actual.g - expected.g) > tolerance || Mathf.Abs(actual.b - expected.b) > tolerance || Mathf.Abs(actual.a - expected.a) > .03f)
            throw new InvalidOperationException(label + " mismatch: expected " + expected + ", got " + actual);
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

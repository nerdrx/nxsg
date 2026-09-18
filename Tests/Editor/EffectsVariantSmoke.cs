using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod EffectsVariantSmoke.Run.
public static class EffectsVariantSmoke
{
    static readonly List<string> failures = new List<string>();

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            foreach (var operation in NodeCatalog.All)
            {
                if (operation == "core.parameter" || operation == "core.output" || operation == "core.shell") continue;
                Case(operation, () => CompileNode(operation));
            }
            Case("float constant", () => CompileConstant("float"));
            Case("color constant", () => CompileConstant("color"));
            Case("multiple texture bindings", CompileMultipleTextures);
            Case("legacy toon texture chain", CompileLegacyChain);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log("NXSG EFFECTS VARIANT SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static void Case(string name, Action action) { try { action(); Debug.Log("NXSG variant passed: " + name); } catch (Exception e) { failures.Add(name + ": " + e.Message); Debug.LogError("NXSG variant failed: " + name + ": " + e.Message); } }

    static void CompileNode(string operation)
    {
        var graph = new ShaderGraph { GraphId = "effects-variant-" + operation };
        var node = NodeCatalog.Create(operation); node.Id = "source"; graph.Nodes.Add(node);
        AddInputFixtures(graph, node);
        string sourcePort = NodeCatalog.Ports(operation, true).FirstOrDefault();
        if (sourcePort == null) throw new InvalidOperationException("No output port.");
        var type = NodeCatalog.PortType(node, sourcePort);
        if (type == "surface")
        {
            graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "source", sourcePort, "output", "surface", "source-output");
        }
        else if (type == "vector3")
        {
            graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.pbrSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "source", sourcePort, "surface", "normal", "source-normal"); Connect(graph, "surface", "surface", "output", "surface", "surface-output");
        }
        else if (type == "vector2")
        {
            graph.Resources.Add(new GraphResource { Id="sample", Kind="texture2D", Uri="builtin://white" });
            graph.Nodes.Add(new GraphNode { Id="sample", Operation="core.texture2D", Properties=new JObject {["resourceId"]="sample"} });
            graph.Nodes.Add(new GraphNode { Id="surface", Operation="core.unlitSurface" });
            graph.Nodes.Add(new GraphNode { Id="output", Operation="core.output" });
            Connect(graph,"source",sourcePort,"sample","uv","sample-uv"); Connect(graph,"sample","color","surface","albedo","sample-albedo"); Connect(graph,"surface","surface","output","surface","output");
        }
        else
        {
            graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "source", sourcePort, "surface", "albedo", "source-albedo"); Connect(graph, "surface", "surface", "output", "surface", "surface-output");
        }
        using (var preview = GraphPreview.Create(graph, null))
            for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("SetPass failed: " + pass);
    }

    static void AddInputFixtures(ShaderGraph graph, GraphNode node)
    {
        foreach (var input in NodeCatalog.Ports(node.Operation, false))
        {
            var type = NodeCatalog.PortType(node, input);
            if (input == "uv" || type == "vector2") { graph.Nodes.Add(new GraphNode { Id = input + "-uv", Operation = "core.uv0" }); Connect(graph, input + "-uv", "uv", node.Id, input, input + "-fixture"); }
            else if (type == "color") { graph.Nodes.Add(ColorNode(input + "-color", Color.white)); Connect(graph, input + "-color", "value", node.Id, input, input + "-fixture"); }
            else if (type == "float") { graph.Nodes.Add(Float(input + "-float", .5)); Connect(graph, input + "-float", "value", node.Id, input, input + "-fixture"); }
        }
        if (node.Operation == "core.texture2D" || node.Operation == "core.sticker")
        {
            node.Properties["resourceId"] = "white";
            graph.Resources.Add(new GraphResource { Id = "white", Kind = "texture2D", Uri = "builtin://white" });
        }
    }

    static void CompileConstant(string type)
    {
        var graph = new ShaderGraph { GraphId = "constant-" + type }; var constant = new GraphNode { Id = "constant", Operation = "core.constant", Properties = new JObject { ["valueType"] = type, ["value"] = type == "float" ? (JToken)0.5 : (JToken)new JArray(1, 0, 1, 1) } }; graph.Nodes.Add(constant); graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "constant", "value", "surface", "albedo", "constant-albedo"); Connect(graph, "surface", "surface", "output", "surface", "constant-output"); using (var preview = GraphPreview.Create(graph, null)) if (!preview.Material.SetPass(0)) throw new InvalidOperationException("constant SetPass failed");
    }

    static void CompileMultipleTextures()
    {
        var graph = new ShaderGraph { GraphId = "multiple-textures" }; graph.Resources.Add(new GraphResource { Id = "a", Kind = "texture2D", Uri = "builtin://white" }); graph.Resources.Add(new GraphResource { Id = "b", Kind = "texture2D", Uri = "builtin://white" }); graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" }); graph.Nodes.Add(new GraphNode { Id = "a", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "a" } }); graph.Nodes.Add(new GraphNode { Id = "b", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "b" } }); graph.Nodes.Add(new GraphNode { Id = "layer", Operation = "core.layer" }); graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "uv", "uv", "a", "uv", "a-uv"); Connect(graph, "uv", "uv", "b", "uv", "b-uv"); Connect(graph, "a", "color", "layer", "base", "a-layer"); Connect(graph, "b", "color", "layer", "overlay", "b-layer"); Connect(graph, "layer", "color", "surface", "albedo", "layer-surface"); Connect(graph, "surface", "surface", "output", "surface", "output"); using (var preview = GraphPreview.Create(graph, null)) for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("multiple texture SetPass failed");
    }

    static void CompileLegacyChain()
    {
        var graph = new ShaderGraph { GraphId = "legacy-chain" }; graph.Resources.Add(new GraphResource { Id = "legacy", Kind = "texture2D", Uri = "builtin://white" }); graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" }); graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "legacy" } }); graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, "uv", "uv", "texture", "uv", "uv-texture"); Connect(graph, "texture", "color", "toon", "albedo", "texture-toon"); Connect(graph, "toon", "surface", "output", "surface", "toon-output"); using (var preview = GraphPreview.Create(graph, null)) if (!preview.Material.SetPass(0)) throw new InvalidOperationException("legacy chain SetPass failed");
    }

    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static GraphNode ColorNode(string id, Color value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(value.r, value.g, value.b, value.a) } }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}

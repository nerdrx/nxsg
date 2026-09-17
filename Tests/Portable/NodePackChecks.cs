using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class NodePackChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var graph = CreateGraph(0f, true);
        var validation = GraphValidator.Validate(graph);
        assert(validation.IsValid, "node pack representative graph validates");

        var emission = ShaderEmitter.Emit(graph);
        assert(emission.Succeeded && !string.IsNullOrEmpty(emission.ShaderSource), "node pack representative graph emits");
        assert(emission.Succeeded && emission.ShaderSource.Length > 0, "node pack emits nonempty shader");

        var baseline = CreateGraph(0f, false);
        var withDeadNodes = CreateGraph(0f, false);
        withDeadNodes.Nodes.Add(new GraphNode { Id = "dead-time", Operation = "core.time", Properties = new JObject() });
        withDeadNodes.Nodes.Add(new GraphNode { Id = "dead-noise", Operation = "core.noise", Properties = new JObject { ["scale"] = 5, ["speed"] = 1 } });
        var baseEmission = ShaderEmitter.Emit(baseline);
        var deadEmission = ShaderEmitter.Emit(withDeadNodes);
        assert(baseEmission.Succeeded && deadEmission.Succeeded, "dead node graph emits");
        assert(baseEmission.Succeeded && deadEmission.Succeeded && baseEmission.ShaderSource == deadEmission.ShaderSource,
            "unreachable noise and time do not alter emitted shader");

        var mismatch = CreateGraph(0f, false);
        Connect(mismatch, "uv", "uv", "noise", "time", "bad-type");
        var mismatchResult = GraphValidator.Validate(mismatch);
        assert(!mismatchResult.IsValid && mismatchResult.Diagnostics.Any(d => d.Code == "connection.type"), "node pack type mismatch rejected");

        foreach (var operation in new[] { "core.add", "core.mix", "core.emission", "core.oneMinus", "core.clamp", "core.noise" })
        {
            var defaults = new ShaderGraph { GraphId = "defaults" };
            var node = NodeCatalog.Create(operation); node.Id = "test"; defaults.Nodes.Add(node);
            defaults.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
            defaults.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
            Connect(defaults, "test", NodeCatalog.Ports(operation, true)[0], "toon", "albedo", "test-toon");
            Connect(defaults, "toon", "surface", "out", "surface", "toon-out");
            assert(ShaderEmitter.Emit(defaults).Succeeded, operation + " compiles with documented defaults");
        }
        var textureGraph = GraphSamples.CreateDefault();
        var transform = NodeCatalog.Create("core.uvTransform"); transform.Id = "transform";
        var clock = NodeCatalog.Create("core.time"); clock.Id = "clock";
        var scroll = NodeCatalog.Create("core.uvScroll"); scroll.Id = "scroll";
        textureGraph.Nodes.Add(transform); textureGraph.Nodes.Add(clock); textureGraph.Nodes.Add(scroll);
        textureGraph.Connections.RemoveAll(e => e.To.NodeId == "texture");
        Connect(textureGraph, "uv0", "uv", "transform", "uv", "uv-transform");
        Connect(textureGraph, "transform", "uv", "scroll", "uv", "transform-scroll");
        Connect(textureGraph, "clock", "value", "scroll", "time", "clock-scroll");
        Connect(textureGraph, "scroll", "uv", "texture", "uv", "scroll-texture");
        var animatedTexture = ShaderEmitter.Emit(textureGraph);
        assert(animatedTexture.Succeeded && animatedTexture.ShaderSource.Contains("_Time.y") && animatedTexture.ShaderSource.Contains("_MainTex_ST.xy"), "transformed scrolling texture emits with material tiling");
        var valueGraph = CreateGraph(0, false);
        valueGraph.Nodes.Add(new GraphNode { Id = "strength", Operation = "core.value", Properties = new JObject { ["value"] = 2 } });
        Connect(valueGraph, "strength", "value", "emission", "strength", "strength-emission");
        var valueEmission = ShaderEmitter.Emit(valueGraph);
        assert(valueEmission.Succeeded && valueEmission.ShaderSource.Contains(" * 2)"), "connected emission strength is emitted");

        var copied = GraphClipboard.Copy(graph, new[] { "uv", "noise", "mix", "emission" });
        var pasted = GraphClipboard.Paste(new ShaderGraph { GraphId = "paste" }, copied, 0, 0).Graph;
        assert(pasted.Nodes.Any(n => n.Operation == "core.noise") && pasted.Nodes.Any(n => n.Operation == "core.emission"),
            "clipboard accepts node pack operations");
        assert(pasted.Connections.Count == 3, "clipboard preserves node pack internal edges");
    }

    static ShaderGraph CreateGraph(float timeValue, bool includeOptional)
    {
        var graph = new ShaderGraph { GraphId = "node-pack-check" };
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0", Properties = new JObject() });
        graph.Nodes.Add(new GraphNode { Id = "time", Operation = "core.value", Properties = new JObject { ["value"] = timeValue } });
        graph.Nodes.Add(new GraphNode { Id = "scroll", Operation = "core.uvScroll", Properties = new JObject { ["speed"] = new JArray(.1, 0) } });
        graph.Nodes.Add(new GraphNode { Id = "noise", Operation = "core.noise", Properties = new JObject { ["scale"] = 5, ["speed"] = 1 } });
        graph.Nodes.Add(new GraphNode { Id = "other", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 1, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "mix", Operation = "core.mix", Properties = new JObject { ["factor"] = .5 } });
        graph.Nodes.Add(new GraphNode { Id = "emission", Operation = "core.emission", Properties = new JObject { ["strength"] = 1 } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "uv", "uv", "scroll", "uv", "uv-scroll");
        Connect(graph, "time", "value", "scroll", "time", "time-scroll");
        Connect(graph, "scroll", "uv", "noise", "uv", "scroll-noise");
        Connect(graph, "time", "value", "noise", "time", "time-noise");
        Connect(graph, "noise", "color", "mix", "a", "noise-mix");
        Connect(graph, "other", "value", "mix", "b", "other-mix");
        Connect(graph, "noise", "value", "mix", "factor", "noise-factor");
        Connect(graph, "mix", "value", "emission", "color", "mix-emission");
        Connect(graph, "emission", "color", "toon", "emission", "emission-toon");
        Connect(graph, "mix", "value", "toon", "albedo", "mix-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        if (includeOptional)
        {
            graph.Nodes.Add(new GraphNode { Id = "one-minus", Operation = "core.oneMinus", Properties = new JObject() });
            graph.Nodes.Add(new GraphNode { Id = "clamp", Operation = "core.clamp", Properties = new JObject() });
            graph.Nodes.Add(new GraphNode { Id = "add", Operation = "core.add", Properties = new JObject() });
        }
        return graph;
    }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

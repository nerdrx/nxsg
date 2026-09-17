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

        CheckUvContracts(assert);
        CheckUvChains(assert);
        CheckUvDceAndClipboard(assert);
        CheckColorMath(assert);
    }

    static void CheckColorMath(Action<bool, string> assert)
    {
        foreach (var operation in new[] { "core.subtract", "core.divide", "core.minimum", "core.maximum" })
        {
            var node = NodeCatalog.Create(operation);
            assert(NodeCatalog.IsKnown(operation) && NodeCatalog.Ports(operation, false).SequenceEqual(new[] { "a", "b" }) &&
                NodeCatalog.Ports(operation, true).SequenceEqual(new[] { "value" }) &&
                NodeCatalog.PortType(node, "a") == "color" && NodeCatalog.PortType(node, "b") == "color" &&
                NodeCatalog.PortType(node, "value") == "color", operation + " has typed color ports");
            assert(!string.IsNullOrEmpty(NodeCatalog.Title(operation)) && !string.IsNullOrEmpty(NodeCatalog.Description(operation)) &&
                !string.IsNullOrEmpty(NodeCatalog.Aliases(operation)), operation + " has catalog metadata");
            var graph = new ShaderGraph { GraphId = operation };
            node.Id = "math"; graph.Nodes.Add(node);
            graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
            graph.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
            Connect(graph, "math", "value", "toon", "albedo", operation + "-toon");
            Connect(graph, "toon", "surface", "out", "surface", operation + "-out");
            var result = ShaderEmitter.Emit(graph);
            assert(GraphValidator.Validate(graph).IsValid && result.Succeeded, operation + " defaults validate and emit");
            if (operation == "core.divide") assert(result.ShaderSource.Contains("NXSG_SafeDivide") && result.ShaderSource.Contains("1e-5"), "divide emits signed safe denominator helper");
        }

        var typed = new ShaderGraph { GraphId = "color-math-typed" };
        typed.Nodes.Add(new GraphNode { Id = "a", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0.8, 0.4, 0.2, 1) } });
        typed.Nodes.Add(new GraphNode { Id = "b", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0.2, 0.1, 0.1, 1) } });
        typed.Nodes.Add(new GraphNode { Id = "subtract", Operation = "core.subtract" });
        typed.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        typed.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        Connect(typed, "a", "value", "subtract", "a", "a-subtract"); Connect(typed, "b", "value", "subtract", "b", "b-subtract");
        Connect(typed, "subtract", "value", "toon", "albedo", "subtract-toon"); Connect(typed, "toon", "surface", "out", "surface", "toon-out");
        assert(GraphValidator.Validate(typed).IsValid && ShaderEmitter.Emit(typed).Succeeded, "typed color math graph validates and emits");
        var copied = GraphClipboard.Copy(typed, new[] { "a", "b", "subtract" });
        var pasted = GraphClipboard.Paste(new ShaderGraph { GraphId = "math-paste" }, copied, 0, 0).Graph;
        assert(pasted.Nodes.Any(n => n.Operation == "core.subtract") && pasted.Connections.Count == 2, "color math clipboard preserves typed edges");
    }

    static void CheckUvContracts(Action<bool, string> assert)
    {
        var expected = new[] { "core.polarUV", "core.uvRotate", "core.objectUV", "core.worldUV" };
        foreach (var operation in expected)
            assert(NodeCatalog.IsKnown(operation), operation + " is registered");

        var polar = NodeCatalog.Create("core.polarUV");
        assert(NodeCatalog.Ports("core.polarUV", false).SequenceEqual(new[] { "uv" }) &&
            NodeCatalog.Ports("core.polarUV", true).SequenceEqual(new[] { "uv" }) &&
            NodeCatalog.PortType(polar, "uv") == "vector2", "polar UV has vector2 uv input/output");
        assert(VectorProperty(polar, "center", .5, .5) && NumberProperty(polar, "radialScale", 1) &&
            NumberProperty(polar, "angleScale", 1), "polar UV defaults are documented");

        var rotate = NodeCatalog.Create("core.uvRotate");
        assert(NodeCatalog.Ports("core.uvRotate", false).SequenceEqual(new[] { "uv", "angle" }) &&
            NodeCatalog.Ports("core.uvRotate", true).SequenceEqual(new[] { "uv" }) &&
            NodeCatalog.PortType(rotate, "uv") == "vector2" && NodeCatalog.PortType(rotate, "angle") == "float",
            "UV rotate ports have documented types");
        assert(VectorProperty(rotate, "center", .5, .5) && NumberProperty(rotate, "angle", 0),
            "UV rotate defaults are documented");

        foreach (var operation in new[] { "core.objectUV", "core.worldUV" })
        {
            var node = NodeCatalog.Create(operation);
            assert(NodeCatalog.Ports(operation, false).Length == 0 && NodeCatalog.Ports(operation, true).SequenceEqual(new[] { "uv" }) &&
                NodeCatalog.PortType(node, "uv") == "vector2", operation + " is planar vector2 source");
        }
    }

    static void CheckUvChains(Action<bool, string> assert)
    {
        var procedural = CreateUvNoiseGraph();
        var validation = GraphValidator.Validate(procedural);
        var emission = ShaderEmitter.Emit(procedural);
        assert(validation.IsValid && emission.Succeeded, "UV nodes chain into noise and compile");
        var scaled = CreateUvNoiseGraph();
        scaled.Nodes.Single(n => n.Id == "polar").Properties["radialScale"] = 2;
        var scaledEmission = ShaderEmitter.Emit(scaled);
        assert(emission.Succeeded && scaledEmission.Succeeded && emission.ShaderSource != scaledEmission.ShaderSource,
            "polar UV scale affects emitted semantics");

        foreach (var sourceOperation in new[] { "core.objectUV", "core.worldUV" })
        {
            var texture = GraphSamples.CreateDefault();
            texture.Nodes.RemoveAll(n => n.Id == "uv0");
            texture.Connections.RemoveAll(e => e.From.NodeId == "uv0");
            var source = NodeCatalog.Create(sourceOperation); source.Id = "uv-source";
            texture.Nodes.Add(source);
            Connect(texture, "uv-source", "uv", "texture", "uv", sourceOperation + "-texture");
            assert(GraphValidator.Validate(texture).IsValid && ShaderEmitter.Emit(texture).Succeeded,
                sourceOperation + " chains into texture");
        }

        var mismatch = CreateUvNoiseGraph();
        Connect(mismatch, "uv0", "uv", "rotate", "angle", "bad-angle-type");
        var mismatchResult = GraphValidator.Validate(mismatch);
        assert(!mismatchResult.IsValid && mismatchResult.Diagnostics.Any(d => d.Code == "connection.type"),
            "UV rotate rejects vector2 angle connection");

        var badCenter = CreateUvNoiseGraph();
        badCenter.Nodes.Single(n => n.Id == "polar").Properties["center"] = new JArray(.5);
        var badCenterResult = GraphValidator.Validate(badCenter);
        assert(!badCenterResult.IsValid && badCenterResult.Diagnostics.Any(d => d.Code == "property.type"),
            "polar UV rejects malformed center property");

        var badAngle = CreateUvNoiseGraph();
        badAngle.Nodes.Single(n => n.Id == "rotate").Properties["angle"] = new JObject { ["degrees"] = 30 };
        var badAngleResult = GraphValidator.Validate(badAngle);
        assert(!badAngleResult.IsValid && badAngleResult.Diagnostics.Any(d => d.Code == "property.type"),
            "UV rotate rejects malformed angle property");

        var longChain = CreateAlternatingUvGraph(20);
        var longEmission = ShaderEmitter.Emit(longChain);
        assert(longEmission.Succeeded && longEmission.ShaderSource.Length < 30000,
            "alternating UV chain stays within bounded source size");
    }

    static void CheckUvDceAndClipboard(Action<bool, string> assert)
    {
        var baseline = GraphSamples.CreateDefault();
        var withDeadUv = GraphSamples.CreateDefault();
        withDeadUv.Nodes.Add(NodeCatalog.Create("core.objectUV"));
        withDeadUv.Nodes[withDeadUv.Nodes.Count - 1].Id = "dead-object-uv";
        withDeadUv.Nodes.Add(NodeCatalog.Create("core.worldUV"));
        withDeadUv.Nodes[withDeadUv.Nodes.Count - 1].Id = "dead-world-uv";
        var baseEmission = ShaderEmitter.Emit(baseline);
        var deadEmission = ShaderEmitter.Emit(withDeadUv);
        assert(baseEmission.Succeeded && deadEmission.Succeeded && baseEmission.ShaderSource == deadEmission.ShaderSource,
            "disconnected UV sources do not alter emitted shader");

        var graph = CreateUvNoiseGraph();
        var copied = GraphClipboard.Copy(graph, new[] { "uv0", "polar", "rotate", "angle" });
        var pasted = GraphClipboard.Paste(new ShaderGraph { GraphId = "uv-paste" }, copied, 0, 0).Graph;
        assert(pasted.Nodes.Any(n => n.Operation == "core.polarUV") && pasted.Nodes.Any(n => n.Operation == "core.uvRotate"),
            "clipboard preserves UV nodes");
        assert(pasted.Connections.Count == 3 && VectorProperty(pasted.Nodes.Single(n => n.Operation == "core.polarUV"), "center", .5, .5),
            "clipboard preserves UV edges and properties");
    }

    static ShaderGraph CreateUvNoiseGraph()
    {
        var graph = new ShaderGraph { GraphId = "uv-noise-check" };
        graph.Nodes.Add(new GraphNode { Id = "uv0", Operation = "core.uv0" });
        graph.Nodes.Add(NodeCatalog.Create("core.polarUV")); graph.Nodes[1].Id = "polar";
        graph.Nodes.Add(NodeCatalog.Create("core.uvRotate")); graph.Nodes[2].Id = "rotate";
        graph.Nodes.Add(new GraphNode { Id = "angle", Operation = "core.value", Properties = new JObject { ["value"] = 30 } });
        graph.Nodes.Add(NodeCatalog.Create("core.noise")); graph.Nodes[4].Id = "noise";
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "uv0", "uv", "polar", "uv", "uv-polar");
        Connect(graph, "polar", "uv", "rotate", "uv", "polar-rotate");
        Connect(graph, "angle", "value", "rotate", "angle", "angle-rotate");
        Connect(graph, "rotate", "uv", "noise", "uv", "rotate-noise");
        Connect(graph, "noise", "color", "toon", "albedo", "noise-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    static ShaderGraph CreateAlternatingUvGraph(int count)
    {
        var graph = new ShaderGraph { GraphId = "uv-alternating-check" };
        graph.Nodes.Add(new GraphNode { Id = "uv0", Operation = "core.uv0" });
        var previous = "uv0";
        for (var i = 0; i < count; i++)
        {
            var operation = i % 2 == 0 ? "core.polarUV" : "core.uvRotate";
            var node = NodeCatalog.Create(operation); node.Id = "uv-" + i;
            graph.Nodes.Add(node);
            Connect(graph, previous, "uv", node.Id, "uv", "uv-edge-" + i);
            if (operation == "core.uvRotate")
            {
                var angle = new GraphNode { Id = "angle-" + i, Operation = "core.value", Properties = new JObject { ["value"] = 5 } };
                graph.Nodes.Add(angle);
                Connect(graph, angle.Id, "value", node.Id, "angle", "angle-edge-" + i);
            }
            previous = node.Id;
        }
        graph.Nodes.Add(NodeCatalog.Create("core.noise")); graph.Nodes[graph.Nodes.Count - 1].Id = "noise";
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, previous, "uv", "noise", "uv", "uv-noise");
        Connect(graph, "noise", "color", "toon", "albedo", "noise-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    static bool NumberProperty(GraphNode node, string name, double expected)
    {
        return node.Properties[name] != null && Math.Abs(node.Properties[name].Value<double>() - expected) < 0.0001;
    }

    static bool VectorProperty(GraphNode node, string name, double x, double y)
    {
        var value = node.Properties[name] as JArray;
        return value != null && value.Count == 2 && Math.Abs(value[0].Value<double>() - x) < 0.0001 &&
            Math.Abs(value[1].Value<double>() - y) < 0.0001;
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

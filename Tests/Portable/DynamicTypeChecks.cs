using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class DynamicTypeChecks
{
    public static void Run(Action<bool, string> assert)
    {
        assert(GraphTypes.IsDynamic("core.add") && !GraphTypes.IsDynamic("core.uvRotate"), "dynamic operation catalog");
        CheckFloatMix(assert);
        CheckFloatUvAngle(assert);
        CheckMixedMath(assert);
        CheckRejectedColorScalar(assert);
        CheckOrderIndependentChain(assert);
        CheckCycleBound(assert);
        CheckScalarToAlbedo(assert);
        CheckNoiseSemantics(assert);
        CheckClipboardInference(assert);
        CheckRamp(assert);
        CheckUnboundDemand(assert);
    }

    private static void CheckFloatMix(Action<bool, string> assert)
    {
        var graph = ColorMixGraph();
        var inferred = GraphTypes.Infer(graph);
        assert(inferred["math"] == "float" && GraphTypes.PortType(graph, Node(graph, "mix"), "factor", inferred) == "float",
            "float math drives Mix factor");
        assert(GraphValidator.Validate(graph).IsValid && ShaderEmitter.Emit(graph).Succeeded,
            "float math Mix graph validates and emits");
    }

    private static void CheckFloatUvAngle(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "float-uv-angle" };
        graph.Nodes.Add(Value("value", 0.25));
        graph.Nodes.Add(Math("math"));
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        graph.Nodes.Add(new GraphNode { Id = "rotate", Operation = "core.uvRotate" });
        Connect(graph, "value", "value", "math", "a", "value-math");
        Connect(graph, "value", "value", "math", "b", "value-math-b");
        Connect(graph, "uv", "uv", "rotate", "uv", "uv-rotate");
        Connect(graph, "math", "value", "rotate", "angle", "math-angle");
        assert(GraphTypes.PortType(graph, Node(graph, "rotate"), "angle") == "float" && GraphValidator.Validate(graph).IsValid,
            "float math drives UV rotate angle");
    }

    private static void CheckMixedMath(Action<bool, string> assert)
    {
        var graph = ColorMixGraph();
        Connect(graph, "color", "value", "math", "b", "color-math");
        var inferred = GraphTypes.Infer(graph);
        assert(inferred["math"] == "color", "mixed color and scalar math infers color");
    }

    private static void CheckRejectedColorScalar(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "color-to-scalar" };
        graph.Nodes.Add(new GraphNode { Id = "color", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "rotate", Operation = "core.uvRotate" });
        Connect(graph, "color", "value", "rotate", "angle", "bad-color-angle");
        var result = GraphValidator.Validate(graph);
        assert(!result.IsValid && result.Diagnostics.Any(d => d.Code == "connection.type"), "color to scalar connection rejects");
    }

    private static void CheckOrderIndependentChain(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "order-independent" };
        graph.Nodes.Add(Math("last"));
        graph.Nodes.Add(Math("first"));
        graph.Nodes.Add(Value("value", 1));
        Connect(graph, "value", "value", "first", "a", "value-first");
        Connect(graph, "value", "value", "first", "b", "value-first-b");
        Connect(graph, "first", "value", "last", "a", "first-last");
        Connect(graph, "value", "value", "last", "b", "value-last-b");
        assert(GraphTypes.Infer(graph)["last"] == "float", "float chain inference independent of node order");
    }

    private static void CheckCycleBound(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "dynamic-cycle" };
        graph.Nodes.Add(Math("a")); graph.Nodes.Add(Math("b"));
        Connect(graph, "a", "value", "b", "a", "a-b");
        Connect(graph, "b", "value", "a", "a", "b-a");
        var timer = Stopwatch.StartNew();
        var inferred = GraphTypes.Infer(graph);
        timer.Stop();
        assert(timer.ElapsedMilliseconds < 1000 && inferred.Count == 2 && !GraphValidator.Validate(graph).IsValid,
            "dynamic cycle inference bounded and validator rejects");
    }

    private static void CheckScalarToAlbedo(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "scalar-albedo" };
        graph.Nodes.Add(Value("value", 0.5));
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "value", "value", "toon", "albedo", "value-albedo");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        assert(GraphValidator.Validate(graph).IsValid && ShaderEmitter.Emit(graph).Succeeded, "scalar directly drives Toon albedo");
    }

    private static void CheckNoiseSemantics(Action<bool, string> assert)
    {
        var floatGraph = ColorMixGraph();
        floatGraph.Nodes.Add(new GraphNode { Id = "noise", Operation = "core.noise", Properties = new JObject { ["scale"] = 1, ["speed"] = 1 } });
        Connect(floatGraph, "noise", "value", "math", "b", "noise-value");
        assert(GraphTypes.Infer(floatGraph)["math"] == "float", "Noise value keeps scalar semantics");
        var colorGraph = ColorMixGraph();
        colorGraph.Nodes.Add(new GraphNode { Id = "noise", Operation = "core.noise", Properties = new JObject { ["scale"] = 1, ["speed"] = 1 } });
        Connect(colorGraph, "noise", "color", "math", "b", "noise-color");
        assert(GraphTypes.Infer(colorGraph)["math"] == "color", "Noise color keeps color semantics");
    }

    private static void CheckClipboardInference(Action<bool, string> assert)
    {
        var graph = ColorMixGraph();
        var before = GraphJson.Serialize(graph);
        GraphTypes.Infer(graph);
        GraphClipboard.Copy(graph, graph.Nodes.Select(n => n.Id));
        assert(GraphJson.Serialize(graph) == before, "type inference and clipboard do not mutate graph");
    }

    private static void CheckUnboundDemand(Action<bool, string> assert)
    {
        var graph = ColorMixGraph();
        graph.Connections.RemoveAll(e => e.To.NodeId == "math" || e.Id == "math-factor");
        graph.Nodes.Add(Math("second"));
        Connect(graph, "math", "value", "second", "a", "empty-chain");
        Connect(graph, "second", "value", "mix", "factor", "scalar-demand");
        var types = GraphTypes.Infer(graph);
        assert(types["math"] == "float" && types["second"] == "float" && ShaderEmitter.Emit(graph).Succeeded,
            "empty math chain adapts to downstream Mix factor");
        graph.Nodes.Reverse();
        var reverse = GraphTypes.Infer(graph);
        assert(reverse["math"] == "float" && reverse["second"] == "float", "scalar demand is order independent");
        graph.Connections.Add(new GraphConnection { Id = "bad-endpoint", From = new GraphPortRef { NodeId = null, PortId = "value" },
            To = new GraphPortRef { NodeId = "math", PortId = "a" } });
        assert(!GraphValidator.Validate(graph).IsValid, "malformed endpoint rejects without crashing inference");
    }

    private static void CheckRamp(Action<bool, string> assert)
    {
        foreach (var points in new[] { new[] { 0.0, 1.0 }, new[] { 1.0, 0.0 }, new[] { 0.5, 0.5 } })
        {
            var graph = ColorMixGraph();
            graph.Nodes.Add(new GraphNode { Id = "ramp", Operation = "core.ramp", Properties = new JObject { ["blackPoint"] = points[0], ["whitePoint"] = points[1], ["smoothness"] = 0.0 } });
            graph.Connections.RemoveAll(e => e.To.NodeId == "mix" && e.To.PortId == "factor");
            Connect(graph, "math", "value", "ramp", "value", "math-ramp");
            Connect(graph, "ramp", "value", "mix", "factor", "ramp-factor");
            var result = GraphValidator.Validate(graph);
            assert(result.IsValid && ShaderEmitter.Emit(graph).Succeeded, "ramp numeric configurations validate and emit");
        }
        var invalid = ColorMixGraph();
        invalid.Nodes.Add(new GraphNode { Id = "ramp", Operation = "core.ramp", Properties = new JObject { ["blackPoint"] = "bad", ["whitePoint"] = 1, ["smoothness"] = 0 } });
        assert(!GraphValidator.Validate(invalid).IsValid, "ramp invalid numeric property rejects");
    }

    private static ShaderGraph ColorMixGraph()
    {
        var graph = new ShaderGraph { GraphId = "color-mix" };
        graph.Nodes.Add(Value("value", 0.5)); graph.Nodes.Add(Math("math"));
        graph.Nodes.Add(new GraphNode { Id = "color", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "mix", Operation = "core.mix" });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "value", "value", "math", "a", "value-math"); Connect(graph, "value", "value", "math", "b", "value-math-b"); Connect(graph, "math", "value", "mix", "factor", "math-factor");
        Connect(graph, "color", "value", "mix", "a", "color-mix-a"); Connect(graph, "color", "value", "mix", "b", "color-mix-b"); Connect(graph, "mix", "value", "toon", "albedo", "mix-albedo"); Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    private static GraphNode Value(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    private static GraphNode Math(string id) { return new GraphNode { Id = id, Operation = "core.add" }; }
    private static GraphNode Node(ShaderGraph graph, string id) { return graph.Nodes.Single(n => n.Id == id); }
    private static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

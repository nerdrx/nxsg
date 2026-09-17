using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Tests/Fixtures"));
        var defaultGraph = Load(fixtures, "default-texture-toon-output.nxsg");
        Assert(GraphValidator.Validate(defaultGraph).IsValid, "default graph validates");
        Assert(GraphValidator.Validate(GraphJson.Parse(GraphJson.Serialize(defaultGraph))).IsValid, "default graph round trips");
        Assert(GraphValidator.Validate(GraphSamples.CreateDefault()).IsValid, "default factory validates");
        var reordered = GraphSamples.CreateDefault();
        reordered.Nodes.Reverse();
        reordered.Connections.Reverse();
        Assert(GraphJson.Serialize(GraphSamples.CreateDefault()) == GraphJson.Serialize(reordered), "known collections save deterministically");

        var layoutA = Load(fixtures, "layout-a.nxsg");
        var layoutB = Load(fixtures, "layout-b.nxsg");
        Assert(GraphJson.ComputeSemanticHash(layoutA) == GraphJson.ComputeSemanticHash(layoutB), "layout does not affect semantic hash");

        var mutable = Load(fixtures, "mutable-parameter.nxsg");
        Assert(mutable.Parameters[0].Binding == GraphBindingKind.AnimatedMaterial, "mutable binding survives parse");
        Assert(GraphJson.Serialize(mutable).Contains("animatedMaterial"), "mutable binding survives save");

        var unknown = Load(fixtures, "unknown-node.nxsg");
        Assert(!GraphValidator.Validate(unknown).IsValid, "unknown operation blocks build");
        Assert(GraphJson.Serialize(unknown).Contains("packData"), "unknown payload survives save");

        var futureNode = GraphSamples.CreateDefault();
        futureNode.Nodes[2].Version = 2;
        var futureRoundTrip = GraphJson.Parse(GraphJson.Serialize(futureNode));
        Assert(futureRoundTrip.Nodes[2].Version == 2, "future node version survives round trip");
        var futureEmission = ShaderEmitter.Emit(futureNode);
        Assert(!futureEmission.Succeeded && futureEmission.ShaderSource == null, "future known node version blocks emission");

        var cycle = Load(fixtures, "cycle.nxsg");
        Assert(HasCode(GraphValidator.Validate(cycle), "graph.cycle"), "cycle is rejected");

        ExpectParseFailure("duplicate JSON properties", "{\"format\":\"nxsg\",\"format\":\"nxsg\"}", "json.parse");
        ExpectParseFailure("trailing JSON value", "{\"format\":\"nxsg\"} []", "json.parse");
        ExpectParseFailure("integer enum value", "{\"format\":\"nxsg\",\"schemaVersion\":1,\"graphId\":\"enum\",\"nodes\":[],\"connections\":[],\"parameters\":[{\"id\":\"p\",\"name\":\"P\",\"type\":1,\"binding\":\"constant\"}]}", "json.parse");
        ExpectParseFailure("unknown enum value", "{\"format\":\"nxsg\",\"schemaVersion\":1,\"graphId\":\"enum\",\"nodes\":[],\"connections\":[],\"parameters\":[{\"id\":\"p\",\"name\":\"P\",\"type\":\"matrix\",\"binding\":\"constant\"}]}", "json.parse");
        var nonFinite = GraphSamples.CreateDefault();
        nonFinite.Nodes[1].Properties["bad"] = double.NaN;
        Assert(HasCode(GraphValidator.Validate(nonFinite), "value.nonfinite"), "nonfinite value is rejected");
        var escapedResource = GraphSamples.CreateDefault();
        escapedResource.Resources[0].Uri = "project://../outside.asset";
        Assert(HasCode(GraphValidator.Validate(escapedResource), "resource.path"), "resource path escape is rejected");

        var invalidPorts = GraphSamples.CreateDefault();
        invalidPorts.Connections[0].To.PortId = "not-a-port";
        invalidPorts.Connections.Add(new GraphConnection
        {
            Id = "bad-type",
            From = new GraphPortRef { NodeId = "uv0", PortId = "uv" },
            To = new GraphPortRef { NodeId = "toon", PortId = "normal" }
        });
        var invalidPortResult = GraphValidator.Validate(invalidPorts);
        Assert(HasCode(invalidPortResult, "connection.port.unknown"), "unknown port is rejected");
        Assert(HasCode(invalidPortResult, "connection.type"), "incompatible type is rejected");

        var large = CreateChain(4096);
        Assert(GraphValidator.Validate(large).IsValid, "4096 node chain validates without recursive traversal");

        var textureEmission = ShaderEmitter.Emit(GraphSamples.CreateDefault());
        Assert(textureEmission.Succeeded, "default texture graph emits");
        Assert(textureEmission.ShaderSource.Contains("_MainTex") && textureEmission.ShaderSource.Contains("_Color"), "default emission uses stable material names");

        var constantGraph = CreateConstantColorGraph();
        var constantEmission = ShaderEmitter.Emit(constantGraph);
        Assert(constantEmission.Succeeded, "constant albedo graph emits");
        Assert(constantEmission.ShaderSource.Contains("(0.2,0.3,0.4,1)"), "constant albedo is folded into emitted source");

        var unusedGraph = CreateConstantColorGraph();
        AddUnusedMath(unusedGraph);
        var unusedEmission = ShaderEmitter.Emit(unusedGraph);
        Assert(unusedEmission.Succeeded, "unused math graph emits");
        Assert(unusedEmission.ShaderSource == constantEmission.ShaderSource, "unused disconnected math does not affect emitted source");

        var malformedEmission = ShaderEmitter.Emit(new ShaderGraph { GraphId = "malformed" });
        Assert(!malformedEmission.Succeeded && malformedEmission.ShaderSource == null, "malformed graph emits no source");

        var hostileGraph = GraphSamples.CreateDefault();
        hostileGraph.Nodes[2].Id = "toon\"; float injected";
        var hostileEmission = ShaderEmitter.Emit(hostileGraph);
        Assert(!hostileEmission.Succeeded && hostileEmission.ShaderSource == null, "hostile node ID is rejected before emission");
        var hostileName = ShaderEmitter.Emit(GraphSamples.CreateDefault(), new EmitterOptions { ShaderName = "NXSG/ok\"; float injected" });
        Assert(!hostileName.Succeeded && hostileName.ShaderSource == null, "hostile shader name is rejected before emission");
        var deepEmission = ShaderEmitter.Emit(CreateDeepColorChain(65));
        Assert(!deepEmission.Succeeded && HasCode(deepEmission.Diagnostics, "backend.traversal.depth"), "deep expression graph is rejected before recursive emission");
        var repeatedEmission = ShaderEmitter.Emit(CreateRepeatedMultiplyGraph(14));
        Assert(!repeatedEmission.Succeeded && HasCode(repeatedEmission.Diagnostics, "backend.traversal.work"), "repeated-input expression work is bounded before expansion");

        var mutableEmission = ShaderEmitter.Emit(CreateMutableMultiplyGraph());
        DumpDiagnostics("mutable-multiply", mutableEmission.Diagnostics);
        Assert(mutableEmission.Succeeded, "mutable parameter through multiply emits");
        Assert(mutableEmission.ShaderSource != null && mutableEmission.ShaderSource.Contains("_NXSG_P_tint"), "mutable parameter through multiply remains a uniform");
        Assert(HasMaterialProperty(mutableEmission.Properties, "tint", GraphBindingKind.AnimatedMaterial), "mutable parameter has an animated material property");

        var collisionEmission = ShaderEmitter.Emit(CreateParameterCollisionGraph());
        DumpDiagnostics("parameter-collision", collisionEmission.Diagnostics);
        Assert(!collisionEmission.Succeeded, "sanitized parameter name collision is rejected");
        if (failures != 0)
        {
            Console.Error.WriteLine("NXSG portable smoke checks failed: " + failures);
            return 1;
        }
        Console.WriteLine("NXSG portable smoke checks passed.");
        return 0;
    }

    private static ShaderGraph Load(string root, string name)
    {
        return GraphJson.Parse(File.ReadAllText(Path.Combine(root, name)));
    }

    private static void ExpectParseFailure(string name, string json, string code)
    {
        try
        {
            GraphJson.Parse(json);
            throw new InvalidOperationException(name + " was accepted");
        }
        catch (GraphParseException exception)
        {
            Assert(HasCode(exception.Diagnostics, code), name + " diagnostic");
        }
    }

    private static bool HasCode(ValidationResult result, string code)
    {
        return HasCode(result.Diagnostics, code);
    }

    private static bool HasCode(IReadOnlyList<Diagnostic> diagnostics, string code)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Code == code) return true;
        }
        return false;
    }

    private static ShaderGraph CreateChain(int count)
    {
        var graph = new ShaderGraph { GraphId = "large-chain" };
        for (var i = 0; i < count; i++)
        {
            var isToon = i == count - 2;
            var isOutput = i == count - 1;
            graph.Nodes.Add(new GraphNode
            {
                Id = "n" + i,
                Operation = isOutput ? "core.output" : (isToon ? "core.toonSurface" : "core.multiply"),
                Properties = new JObject { ["valueType"] = "float" }
            });
            if (i > 0)
            {
                graph.Connections.Add(new GraphConnection
                {
                    Id = "e" + i,
                    From = new GraphPortRef { NodeId = "n" + (i - 1), PortId = isOutput ? "surface" : "value" },
                    To = new GraphPortRef { NodeId = "n" + i, PortId = isOutput ? "surface" : (isToon ? "albedo" : "a") }
                });
            }
        }
        return graph;
    }

    private static ShaderGraph CreateConstantColorGraph()
    {
        var graph = new ShaderGraph { GraphId = "constant-color" };
        graph.Nodes.Add(new GraphNode { Id = "constant", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0.2, 0.3, 0.4, 1.0) } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "e-color", "constant", "value", "toon", "albedo");
        Connect(graph, "e-surface", "toon", "surface", "output", "surface");
        return graph;
    }

    private static ShaderGraph CreateMutableMultiplyGraph()
    {
        var graph = new ShaderGraph { GraphId = "mutable-multiply" };
        graph.Parameters.Add(new GraphParameter { Id = "tint", Name = "Tint", Type = GraphValueType.Color, Binding = GraphBindingKind.AnimatedMaterial, DefaultValue = new JArray(0, 0, 0, 0), Exposed = true });
        graph.Nodes.Add(new GraphNode { Id = "parameter", Operation = "core.parameter", Properties = new JObject { ["parameterId"] = "tint" } });
        graph.Nodes.Add(new GraphNode { Id = "constant", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "multiply", Operation = "core.multiply", Properties = new JObject { ["valueType"] = "color" } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "e-a", "parameter", "value", "multiply", "a");
        Connect(graph, "e-b", "constant", "value", "multiply", "b");
        Connect(graph, "e-color", "multiply", "value", "toon", "albedo");
        Connect(graph, "e-surface", "toon", "surface", "output", "surface");
        return graph;
    }

    private static ShaderGraph CreateDeepColorChain(int depth)
    {
        var graph = new ShaderGraph { GraphId = "deep-color-chain" };
        graph.Nodes.Add(new GraphNode { Id = "constant", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        var previous = "constant";
        for (var i = 0; i < depth; i++)
        {
            var multiply = "multiply" + i;
            graph.Nodes.Add(new GraphNode { Id = multiply, Operation = "core.multiply", Properties = new JObject { ["valueType"] = "color" } });
            Connect(graph, "deep-a" + i, previous, "value", multiply, "a");
            Connect(graph, "deep-b" + i, "constant", "value", multiply, "b");
            previous = multiply;
        }

        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "deep-color", previous, "value", "toon", "albedo");
        Connect(graph, "deep-surface", "toon", "surface", "output", "surface");
        return graph;
    }

    private static ShaderGraph CreateRepeatedMultiplyGraph(int depth)
    {
        var graph = new ShaderGraph { GraphId = "repeated-multiply" };
        graph.Nodes.Add(new GraphNode { Id = "constant", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        var previous = "constant";
        for (var i = 0; i < depth; i++)
        {
            var multiply = "repeat" + i;
            graph.Nodes.Add(new GraphNode { Id = multiply, Operation = "core.multiply", Properties = new JObject { ["valueType"] = "color" } });
            Connect(graph, "repeat-a" + i, previous, "value", multiply, "a");
            Connect(graph, "repeat-b" + i, previous, "value", multiply, "b");
            previous = multiply;
        }

        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "repeat-color", previous, "value", "toon", "albedo");
        Connect(graph, "repeat-surface", "toon", "surface", "output", "surface");
        return graph;
    }

    private static ShaderGraph CreateParameterCollisionGraph()
    {
        var graph = CreateConstantColorGraph();
        graph.Parameters.Add(new GraphParameter { Id = "a-b", Name = "A", Type = GraphValueType.Float, Binding = GraphBindingKind.Material, DefaultValue = 0.2 });
        graph.Parameters.Add(new GraphParameter { Id = "a_b", Name = "B", Type = GraphValueType.Float, Binding = GraphBindingKind.Material, DefaultValue = 0.3 });
        return graph;
    }

    private static void AddUnusedMath(ShaderGraph graph)
    {
        graph.Nodes.Add(new GraphNode { Id = "unused-a", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0.01, 0.02, 0.03, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "unused-b", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0.5, 0.5, 0.5, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "unused-multiply", Operation = "core.multiply", Properties = new JObject { ["valueType"] = "color" } });
        Connect(graph, "unused-a-edge", "unused-a", "value", "unused-multiply", "a");
        Connect(graph, "unused-b-edge", "unused-b", "value", "unused-multiply", "b");
    }

    private static void Connect(ShaderGraph graph, string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = id,
            From = new GraphPortRef { NodeId = fromNode, PortId = fromPort },
            To = new GraphPortRef { NodeId = toNode, PortId = toPort }
        });
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }
    }

    private static void DumpDiagnostics(string label, IReadOnlyList<Diagnostic> diagnostics)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            Console.Error.WriteLine(label + ": " + diagnostics[i].Severity + " " + diagnostics[i].Code + " @ " + diagnostics[i].Path + " - " + diagnostics[i].Message);
        }
    }

    private static bool HasMaterialProperty(IReadOnlyList<MaterialProperty> properties, string parameterId, GraphBindingKind binding)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (properties[i].ParameterId == parameterId && properties[i].Binding == binding) return true;
        }
        return false;
    }
}

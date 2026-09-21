using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class TraversalChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var repeated = CreateRepeatedInputGraph(14, false);
        var result = ShaderEmitter.Emit(repeated);
        assert(!result.Succeeded, "repeated input diamond is bounded");
        var diagnostic = result.Diagnostics.SingleOrDefault(d => d.Code == "backend.traversal.work");
        assert(diagnostic != null && diagnostic.Message.Contains("repeated inlined subgraphs") && diagnostic.Message.Contains("8192"), "repeated input diagnostic explains inline work limit");

        var advanced = ShaderEmitter.Emit(CreateRepeatedInputGraph(14, true));
        assert(advanced.Succeeded, "advanced repeated input diamond emits");
    }

    private static ShaderGraph CreateRepeatedInputGraph(int depth, bool advanced)
    {
        var graph = new ShaderGraph { GraphId = advanced ? "repeated-input-advanced" : "repeated-input-basic" };
        graph.Nodes.Add(new GraphNode
        {
            Id = "constant",
            Operation = "core.constant",
            Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) }
        });
        var previous = "constant";
        for (var i = 0; i < depth; i++)
        {
            var add = "add" + i;
            graph.Nodes.Add(new GraphNode { Id = add, Operation = "core.add", Properties = new JObject { ["valueType"] = "color" } });
            Connect(graph, "a" + i, previous, "value", add, "a");
            Connect(graph, "b" + i, previous, "value", add, "b");
            previous = add;
        }

        var toon = new GraphNode { Id = "toon", Operation = "core.toonSurface" };
        if (advanced) toon.Properties["opacity"] = 0.75;
        graph.Nodes.Add(toon);
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "color", previous, "value", "toon", "albedo");
        Connect(graph, "surface", "toon", "surface", "output", "surface");
        return graph;
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
}

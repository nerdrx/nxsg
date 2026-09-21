using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ColorConversionChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var texture = GraphSamples.CreateDefault();
        texture.Connections.Add(new GraphConnection
        {
            Id = "edge-opacity",
            From = new GraphPortRef { NodeId = "texture", PortId = "color" },
            To = new GraphPortRef { NodeId = "toon", PortId = "opacity" }
        });
        Check(texture, assert, "texture color to opacity");

        foreach (var color in new[] { "red", "green", "blue" })
        {
            var graph = ColorGraph(color);
            Check(graph, assert, color + " luminance conversion");
            assert(GraphTypes.Compatible("color", "float"), color + " color/float sockets compatible");
        }

        var dynamic = ColorGraph("red");
        var add = NodeCatalog.Create("core.add"); add.Id = "add"; dynamic.Nodes.Add(add);
        var green = ColorNode("green", new JArray(0, 1, 0, 1)); dynamic.Nodes.Add(green);
        Edge(dynamic, "red", "value", "add", "a"); Edge(dynamic, "green", "value", "add", "b");
        dynamic.Connections.RemoveAll(e => e.To.NodeId == "toon" && e.To.PortId == "opacity");
        Edge(dynamic, "add", "value", "toon", "opacity");
        var roundTrip = GraphJson.Parse(GraphJson.Serialize(dynamic));
        var result = ShaderEmitter.Emit(roundTrip);
        assert(GraphValidator.Validate(roundTrip).IsValid && result.Succeeded, "dynamic color math round trips and emits");
        assert(result.ShaderSource.Contains(".2126") && result.ShaderSource.Contains(".7152") && result.ShaderSource.Contains(".0722"), "dynamic conversion emits RGB luminance weights");
    }

    private static void Check(ShaderGraph graph, Action<bool, string> assert, string name)
    {
        var validation = GraphValidator.Validate(graph);
        var result = ShaderEmitter.Emit(graph);
        assert(validation.IsValid && result.Succeeded, name + " validates and emits: " + string.Join(";", validation.Diagnostics.Concat(result.Diagnostics).Select(d => d.Message)));
        assert(result.ShaderSource.Contains("dot((") && result.ShaderSource.Contains(".rgb,float3(.2126,.7152,.0722)"), name + " uses RGB luminance dot");
    }

    private static ShaderGraph ColorGraph(string id)
    {
        var graph = new ShaderGraph { GraphId = "color-conversion-" + id };
        graph.Nodes.Add(ColorNode(id, id == "red" ? new JArray(1, 0, 0, 0.17) : id == "green" ? new JArray(0, 1, 0, 0.23) : new JArray(0, 0, 1, 0.91)));
        graph.Nodes.Add(NodeCatalog.Create("core.toonSurface")); graph.Nodes[1].Id = "toon";
        graph.Nodes.Add(NodeCatalog.Create("core.output")); graph.Nodes[2].Id = "output";
        Edge(graph, id, "value", "toon", "opacity"); Edge(graph, "toon", "surface", "output", "surface");
        return graph;
    }

    private static GraphNode ColorNode(string id, JArray value)
    {
        return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } };
    }

    private static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

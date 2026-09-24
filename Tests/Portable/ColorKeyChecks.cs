using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ColorKeyChecks
{
    public static void Run(Action<bool, string> assert)
    {
        foreach (var operation in new[] { "core.colorMask", "core.replaceColor" })
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation), operation + " is catalogued");
            assert(NodeCatalog.PortType(node, operation == "core.colorMask" ? "value" : "color") != null, operation + " output type is catalogued");
            assert(GraphJson.Serialize(GraphJson.Parse(GraphJson.Serialize(Graph(operation)))) == GraphJson.Serialize(Graph(operation)), operation + " round trips");
            var result = ShaderEmitter.Emit(Graph(operation));
            assert(result.Succeeded && result.ShaderSource.Contains("NX_ColorMatch"), operation + " emits shared match helper");
            assert(GraphValidator.Validate(Graph(operation)).IsValid, operation + " direct connections validate");
            var defaults = Graph(operation);
            defaults.Connections.RemoveAll(e => e.To.NodeId == "effect" && (e.To.PortId == "target" || e.To.PortId == "replacement"));
            var defaultResult = ShaderEmitter.Emit(defaults);
            assert(defaultResult.Succeeded && defaultResult.ShaderSource.Contains(operation == "core.colorMask" ? "float4(1,0,0,1)" : "float4(0,0,1,1)"), operation + " uses color defaults when unconnected");
        }

        var invalid = Graph("core.colorMask");
        invalid.Nodes.Single(n => n.Id == "effect").Properties["tolerance"] = double.NaN;
        assert(!GraphValidator.Validate(invalid).IsValid, "colorMask rejects nonfinite tolerance");

        var malformed = Graph("core.replaceColor");
        malformed.Nodes.Single(n => n.Id == "effect").Properties["replacement"] = new JArray(1, 2);
        assert(!GraphValidator.Validate(malformed).IsValid, "replaceColor rejects malformed color defaults");
        var replace = ShaderEmitter.Emit(Graph("core.replaceColor")).ShaderSource;
        assert(replace.Contains("m*saturate("), "replaceColor saturates factor blend");
        assert(replace.Contains(",c.a);"), "replaceColor preserves input alpha");
    }

    private static ShaderGraph Graph(string operation)
    {
        var graph = new ShaderGraph { GraphId = "color-key-check" };
        graph.Nodes.Add(Color("color", .2, .4, .6, .37));
        graph.Nodes.Add(Color("target", 1, 0, 0, 1));
        graph.Nodes.Add(Color("replacement", 0, 0, 1, 1));
        foreach (var id in new[] { "tolerance", "softness", "factor" })
        {
            var value = NodeCatalog.Create("core.value"); value.Id = id; value.Properties["value"] = id == "factor" ? 1.2 : .1; graph.Nodes.Add(value);
        }
        var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "color", "value", "effect", "color");
        Link(graph, "target", "value", "effect", "target");
        if (operation == "core.replaceColor") Link(graph, "replacement", "value", "effect", "replacement");
        Link(graph, "tolerance", "value", "effect", "tolerance");
        Link(graph, "softness", "value", "effect", "softness");
        if (operation == "core.replaceColor") Link(graph, "factor", "value", "effect", "factor");
        Link(graph, "effect", operation == "core.colorMask" ? "value" : "color", "surface", operation == "core.colorMask" ? "opacity" : "albedo");
        Link(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    private static GraphNode Color(string id, double r, double g, double b, double a)
    {
        return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(r, g, b, a) } };
    }

    private static void Link(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

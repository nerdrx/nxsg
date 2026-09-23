using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class UtilityNodeChecks
{
    private static readonly string[] All =
    {
        "core.absolute", "core.power", "core.sqrt", "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round",
        "core.step", "core.smoothstep", "core.remap", "core.pingPong", "core.splitColor", "core.combineColor", "core.luminance",
        "core.contrast", "core.saturation", "core.hueShift", "core.splitUV", "core.combineUV"
    };

    private static readonly string[] Dynamic =
    {
        "core.absolute", "core.power", "core.sqrt", "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round"
    };

    public static void Run(Action<bool, string> assert)
    {
        foreach (var operation in All)
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation), operation + " is catalogued");
            var graph = Build(operation);
            var roundTrip = GraphJson.Parse(GraphJson.Serialize(graph));
            var validation = GraphValidator.Validate(roundTrip);
            var result = ShaderEmitter.Emit(roundTrip);
            assert(validation.IsValid && result.Succeeded, operation + " validates, saves and emits: " + string.Join(";", validation.Diagnostics.Concat(result.Diagnostics).Select(d => d.Message)));
            RejectNonfinite(assert, operation, node);
        }

        foreach (var operation in Dynamic)
        {
            var color = Build(operation, true);
            var scalar = Build(operation, false);
            assert(GraphTypes.PortType(color, color.Nodes.Single(n => n.Id == "effect"), "value") == "color" && ShaderEmitter.Emit(color).Succeeded,
                operation + " infers color");
            assert(GraphTypes.PortType(scalar, scalar.Nodes.Single(n => n.Id == "effect"), "value") == "float" && ShaderEmitter.Emit(scalar).Succeeded,
                operation + " infers scalar");
        }
    }

    private static void RejectNonfinite(Action<bool, string> assert, string operation, GraphNode node)
    {
        var numeric = new[] { "a", "b", "value", "low", "high", "inMin", "inMax", "outMin", "outMax", "length", "hue", "amount", "pivot", "r", "g", "b", "u", "v" };
        foreach (var property in numeric.Where(name => node.Properties[name] != null))
        {
            var graph = Build(operation);
            graph.Nodes.Single(n => n.Id == "effect").Properties[property] = new JValue(double.NaN);
            assert(!GraphValidator.Validate(graph).IsValid, operation + " rejects nonfinite " + property);
        }
    }

    private static ShaderGraph Build(string operation, bool color = false)
    {
        var graph = new ShaderGraph { GraphId = "utility-" + operation + "-" + color };
        graph.Nodes.Add(new GraphNode { Id = "effect", Operation = operation });
        var effect = graph.Nodes[0];
        if (Dynamic.Contains(operation))
        {
            var input = NodeCatalog.Create(color ? "core.constant" : "core.value"); input.Id = "input";
            graph.Nodes.Add(input);
            Edge(graph, "input", "value", "effect", "a");
            if (operation == "core.power")
            {
                var exponent = NodeCatalog.Create("core.value"); exponent.Id = "exponent"; graph.Nodes.Add(exponent);
                Edge(graph, "exponent", "value", "effect", "b");
            }
        }
        else if (operation == "core.splitColor" || operation == "core.luminance" || operation == "core.contrast" || (operation == "core.saturation" || operation == "core.hueShift"))
        {
            var input = NodeCatalog.Create("core.constant"); input.Id = "input"; graph.Nodes.Add(input); Edge(graph, "input", "value", "effect", "color");
        }
        else if (operation == "core.splitUV")
        {
            graph.Nodes.Add(new GraphNode { Id = "input", Operation = "core.uv0" }); Edge(graph, "input", "uv", "effect", "uv");
        }
        else if (operation == "core.combineUV")
        {
            var u = NodeCatalog.Create("core.value"); u.Id = "u"; graph.Nodes.Add(u); Edge(graph, "u", "value", "effect", "u");
            var v = NodeCatalog.Create("core.value"); v.Id = "v"; graph.Nodes.Add(v); Edge(graph, "v", "value", "effect", "v");
        }
        else if (operation == "core.combineColor")
        {
            foreach (var channel in new[] { "r", "g", "b", "a" }) { var input = NodeCatalog.Create("core.value"); input.Id = channel; graph.Nodes.Add(input); Edge(graph, channel, "value", "effect", channel); }
        }

        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        if (operation == "core.combineUV")
        {
            var preview = NodeCatalog.Create("core.previewVector"); preview.Id = "preview"; graph.Nodes.Add(preview);
            Edge(graph, "effect", "uv", "preview", "uv");
            var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
            Edge(graph, "preview", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface");
        }
        else
        {
            var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
            var port = operation == "core.combineColor" || operation == "core.contrast" || (operation == "core.saturation" || operation == "core.hueShift") ? "color" : operation == "core.splitColor" ? "r" : operation == "core.splitUV" ? "u" : "value";
            var scalarOutput = operation == "core.luminance" || operation == "core.splitColor" || operation == "core.splitUV" || operation == "core.step" || operation == "core.smoothstep" || operation == "core.remap" || operation == "core.pingPong" || (Dynamic.Contains(operation) && !color);
            Edge(graph, "effect", port, "surface", scalarOutput ? "opacity" : "albedo");
            if (scalarOutput) { var albedo = NodeCatalog.Create("core.constant"); albedo.Id = "albedo"; graph.Nodes.Add(albedo); Edge(graph, "albedo", "value", "surface", "albedo"); }
            Edge(graph, "surface", "surface", "output", "surface");
        }
        graph.Nodes.Add(output);
        return graph;
    }

    private static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

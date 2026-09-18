using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class NestedShellChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var single = ShaderEmitter.Emit(GraphSamples.CreateDefault());
        assert(single.Succeeded && single.ShaderSource.Contains("Name \"ForwardBase\"") && single.ShaderSource.Contains("Name \"ShadowCaster\"") && !single.ShaderSource.Contains("Name \"Shell"), "single surface compatibility");

        var baseChain = Graph("nested-base", false, 2);
        var baseResult = ShaderEmitter.Emit(baseChain);
        assert(baseResult.Succeeded && Count(baseResult.ShaderSource, "Name \"Shell") == 2, "nested base emits two shell passes");
        assert(ShadowIsBaseOnly(baseResult.ShaderSource), "nested shells emit base shadow only");

        var layerChain = Graph("nested-layer", true, 2);
        var layerResult = ShaderEmitter.Emit(layerChain);
        assert(layerResult.Succeeded && Count(layerResult.ShaderSource, "Name \"Shell") == 2, "nested layer emits two shell passes");
        assert(ContainsOffsetSum(layerResult.ShaderSource), "nested layer offsets accumulate in source");
        assert(UniqueToonProperties(layerResult.ShaderSource), "nested shell Toon properties have unique names");

        var eight = ShaderEmitter.Emit(Graph("shell-limit", false, 8));
        assert(eight.Succeeded && Count(eight.ShaderSource, "Name \"Shell") == 8, "eight shell passes succeed");
        var nine = ShaderEmitter.Emit(Graph("shell-limit", false, 9));
        assert(!nine.Succeeded, "nine shell passes reject");

        var missing = Graph("missing-shell-input", false, 1);
        missing.Connections.RemoveAll(e => e.To.NodeId == "shell-0" && e.To.PortId == "layer");
        assert(!ShaderEmitter.Emit(missing).Succeeded, "missing shell input rejects");
    }

    private static ShaderGraph Graph(string id, bool nestLayer, int count)
    {
        var graph = new ShaderGraph { GraphId = id };
        AddSurface(graph, "base", .2, .03);
        var previous = "base";
        for (var i = 0; i < count; i++)
        {
            var layer = "layer-" + i;
            AddSurface(graph, layer, .25 + i * .1, .04 + i * .01);
            var shell = NodeCatalog.Create("core.shell"); shell.Id = "shell-" + i; shell.Properties["offset"] = .02 + i * .01;
            graph.Nodes.Add(shell);
            var baseSource = nestLayer && i > 0 ? "base" : previous;
            var layerSource = nestLayer && i == count - 1 && i > 0 ? "shell-" + (i - 1) : layer;
            Connect(graph, baseSource, "surface", shell.Id, "base", "base-" + i);
            Connect(graph, layerSource, "surface", shell.Id, "layer", "layer-" + i);
            previous = shell.Id;
        }
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, previous, "surface", "output", "surface", "output-surface");
        return graph;
    }

    private static void AddSurface(ShaderGraph graph, string id, double threshold, double softness)
    {
        graph.Nodes.Add(new GraphNode
        {
            Id = id,
            Operation = "core.toonSurface",
            Properties = new JObject { ["threshold"] = threshold, ["softness"] = softness, ["shadowStrength"] = 1.0 }
        });
        var color = id + "-color";
        graph.Nodes.Add(new GraphNode { Id = color, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        Connect(graph, color, "value", id, "albedo", color + "-edge");
    }

    private static void Connect(ShaderGraph graph, string fromNode, string fromPort, string toNode, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = fromNode, PortId = fromPort }, To = new GraphPortRef { NodeId = toNode, PortId = toPort } });
    }

    private static bool ContainsOffsetSum(string source)
    {
        return source.Contains("((0+0.03)+0.02)");
    }

    private static bool UniqueToonProperties(string source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("Toon") || !trimmed.Contains("(\"")) continue;
            var name = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (!names.Add(name)) return false;
        }
        return names.Count >= 3;
    }

    private static bool ShadowIsBaseOnly(string source)
    {
        var start = source.IndexOf("Name \"ShadowCaster\"", StringComparison.Ordinal);
        return Count(source, "Name \"ShadowCaster\"") == 1 && start >= 0 && !source.Substring(start).Contains("Shell");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}

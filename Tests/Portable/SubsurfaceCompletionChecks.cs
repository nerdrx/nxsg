using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class SubsurfaceCompletionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var legacy = Graph();
        foreach (var port in new[] { "spread", "distortion", "shadowResponse" }) legacy.Nodes[0].Properties.Remove(port);
        var oldSource = ShaderEmitter.Emit(legacy);
        var neutral = ShaderEmitter.Emit(Graph());
        check(oldSource.Succeeded && neutral.Succeeded && oldSource.ShaderSource == neutral.ShaderSource,
            "New subsurface defaults preserve legacy source exactly");
        check(!neutral.ShaderSource.Contains("nxScatterShadow") && !neutral.ShaderSource.Contains("scatterSpread"),
            "Neutral subsurface prunes new calculations and scene shadow sampling");
        foreach (var port in new[] { "spread", "distortion", "shadowResponse" })
        {
            var g = Graph();
            check(NodeCatalog.PortType(g.Nodes[0], port) == "float", "Subsurface input: " + port);
            g.Nodes[0].Properties[port] = .75;
            var parsed = GraphJson.Parse(GraphJson.Serialize(g));
            check((double)parsed.Nodes.Single(n => n.Id == "scatter").Properties[port] == .75, "Subsurface setting survives save: " + port);
            var result = ShaderEmitter.Emit(parsed);
            check(result.Succeeded && result.ShaderSource != neutral.ShaderSource, "Subsurface property emits: " + port);
            var value = NodeCatalog.Create("core.value"); value.Id = "value"; value.Properties["value"] = 0; g.Nodes.Add(value);
            Edge(g, "value", "value", "scatter", port);
            result = ShaderEmitter.Emit(g);
            check(result.Succeeded && result.ShaderSource != neutral.ShaderSource, "Connected zero retains dynamic subsurface input: " + port);
        }
        var shadows = Graph(); shadows.Nodes[0].Properties["shadowResponse"] = 1;
        var shadowSource = ShaderEmitter.Emit(shadows).ShaderSource;
        check(shadowSource.Contains("nxScatterShadow=UNITY_SHADOW_ATTENUATION(input,input.ws)") &&
              shadowSource.Contains("defined(UNITY_PASS_FORWARDBASE)") && shadowSource.Contains("float nxScatterShadow=1;"),
            "Subsurface uses real forward scene shadows with neutral non-forward fallback");
        shadows.Connections.RemoveAll(e => e.To.NodeId == "surface" && e.To.PortId == "albedo");
        Edge(shadows, "scatter", "color", "surface", "displacement");
        var vertex = ShaderEmitter.Emit(shadows);
        check(!vertex.Succeeded && vertex.Diagnostics.Any(d => d.Message.Contains("fragment shadows")),
            "Subsurface shadow sampling is rejected in vertex inputs");
        shadows.Nodes[0].Properties["shadowResponse"] = 0;
        check(ShaderEmitter.Emit(shadows).Succeeded, "Neutral subsurface remains valid in vertex stage");
        var finite = Graph(); finite.Nodes[0].Properties["spread"] = 1000;
        check(GraphValidator.Validate(finite).IsValid, "Typed subsurface spread is not slider-clamped");
        finite.Nodes[0].Properties["spread"] = double.NaN;
        check(!GraphValidator.Validate(finite).IsValid, "Subsurface rejects nonfinite controls");
    }

    static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "subsurface-completion" };
        var scatter = NodeCatalog.Create("core.subsurface"); scatter.Id = "scatter"; graph.Nodes.Add(scatter);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Edge(graph, "scatter", "color", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }
    static void Edge(ShaderGraph g, string from, string output, string to, string input) =>
        g.Connections.Add(new GraphConnection { Id = from + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
}

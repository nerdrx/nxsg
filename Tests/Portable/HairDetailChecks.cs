using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class HairDetailChecks
{
    public static void Run(Action<bool,string> check)
    {
        var node = NodeCatalog.Create("core.anisotropicHighlight");
        var ports = new[] { "longitudinalWidth", "azimuthalWidth", "shiftNoise", "secondaryShiftNoise", "reflectionStrength", "reflectionStretch", "reflectionRoughness" };
        check(ports.All(port => NodeCatalog.PortType(node, port) == "float"), "Hair controls are typed numeric sockets");
        check((double)node.Properties["longitudinalWidth"] == 1 && (double)node.Properties["azimuthalWidth"] == 1 &&
            (double)node.Properties["reflectionStrength"] == 0 && (double)node.Properties["reflectionStretch"] == 0,
            "Hair widths default neutral and probe reflection defaults disabled");

        var neutral = Emit(Graph("core.anisotropicHighlight", check), check, "Neutral anisotropic hair");
        check(!neutral.Contains("UNITY_SAMPLE_TEXCUBE_LOD"),
            "Neutral hair omits reflection probe sampling");
        check(neutral.Contains("float lw=max(abs(longitudinalWidth)") && neutral.Contains("float aw=max(abs(azimuthalWidth)"),
            "Shader clamps independently controlled hair widths");
        var legacy = Graph("core.anisotropicHighlight", check);
        var legacyNode = legacy.Nodes.Single(item => item.Id == "feature");
        foreach (var name in new[] { "shiftNoise", "secondaryShiftNoise", "longitudinalWidth", "azimuthalWidth", "reflectionStrength", "reflectionStretch", "reflectionRoughness" }) legacyNode.Properties.Remove(name);
        legacyNode.Properties["shift"] = .25; legacyNode.Properties["secondaryShift"] = -.15; legacyNode.Properties["dualLobe"] = .7;
        var legacySource = Emit(legacy, check, "Legacy shifted dual-lobe hair");
        check(legacySource.Contains("NX_AnisoLegacy(input,") && legacySource.Contains("float3 secondaryT=NX_SafeNormal(t+n*secondaryShift)"),
            "Legacy shifted dual-lobe graphs keep original emitted lighting helper");
        var vertexGraph = VertexGraph();
        check(Emit(vertexGraph, check, "Neutral vertex displacement hair").Contains("NX_AnisoLegacy(input,"),
            "Neutral anisotropic highlight remains usable for vertex displacement");
        vertexGraph.Nodes.Single(item => item.Id == "feature").Properties["reflectionStrength"] = .5;
        var activeProbe = ShaderEmitter.Emit(vertexGraph);
        check(!activeProbe.Succeeded && activeProbe.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("fragment-only")),
            "Probe reflection is rejected on vertex displacement path");

        var reflection = Graph("core.anisotropicHighlight", check);
        reflection.Nodes.Single(item => item.Id == "feature").Properties["reflectionStrength"] = .8;
        reflection.Nodes.Single(item => item.Id == "feature").Properties["reflectionStretch"] = 1.5;
        var reflected = Emit(reflection, check, "Stretched probe hair");
        check(reflected.Contains("UNITY_SAMPLE_TEXCUBE_LOD") && reflected.Contains("float stretch=1.5"),
            "Enabled probe samples stretched reflection direction");

        var routedNoise = Graph("core.anisotropicHighlight", check);
        Value(routedNoise, "shiftNoise", .4);
        Value(routedNoise, "secondaryShiftNoise", -.2);
        var noisy = Emit(routedNoise, check, "Shift noise hair");
        check(noisy.Contains(".4") && noisy.Contains("(-0.2)"), "Primary and secondary shift noise route independently");

        var oversized = Graph("core.anisotropicHighlight", check);
        oversized.Nodes.Single(item => item.Id == "feature").Properties["longitudinalWidth"] = 1000;
        oversized.Nodes.Single(item => item.Id == "feature").Properties["azimuthalWidth"] = 0;
        var bounded = Emit(oversized, check, "Out of slider hair widths");
        check(bounded.Contains(",1000,0,"), "Large finite width values remain connectable and reach shader runtime guards");

        var nonfinite = Graph("core.anisotropicHighlight", check);
        nonfinite.Nodes.Single(item => item.Id == "feature").Properties["longitudinalWidth"] = double.PositiveInfinity;
        check(!GraphValidator.Validate(nonfinite).IsValid, "Nonfinite hair control values fail graph validation");
    }

    static ShaderGraph Graph(string operation, Action<bool,string> check)
    {
        var graph = new ShaderGraph { GraphId = "hair-detail-check" };
        var feature = NodeCatalog.Create(operation); feature.Id = "feature"; graph.Nodes.Add(feature);
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "feature", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static ShaderGraph VertexGraph()
    {
        var graph = new ShaderGraph { GraphId = "hair-vertex-check" };
        var feature = NodeCatalog.Create("core.anisotropicHighlight"); feature.Id = "feature"; graph.Nodes.Add(feature);
        graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" });
        var tessellation = NodeCatalog.Create("core.tessellation"); tessellation.Id = "tessellation"; graph.Nodes.Add(tessellation);
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "base", "surface", "tessellation", "base");
        Edge(graph, "feature", "color", "tessellation", "height");
        Edge(graph, "tessellation", "surface", "output", "surface");
        return graph;
    }

    static void Value(ShaderGraph graph, string port, double value)
    {
        var source = NodeCatalog.Create("core.value"); source.Id = "noise-" + port; source.Properties["value"] = value; graph.Nodes.Add(source);
        Edge(graph, source.Id, "value", "feature", port);
    }

    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } }); }

    static string Emit(ShaderGraph graph, Action<bool,string> check, string label)
    {
        var result = ShaderEmitter.Emit(graph);
        check(result.Succeeded, label + " emits: " + string.Join(";", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.ShaderSource ?? "";
    }
}

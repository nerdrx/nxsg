using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class GemSparkleChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var neutralGraph = MakeGraph();
        var neutral = ShaderEmitter.Emit(neutralGraph);
        var gem = neutralGraph.Nodes.Single(n => n.Operation == "core.gem");
        assert(neutral.Succeeded && (double)gem.Properties["sparkleStrength"] == 0 &&
            !neutral.ShaderSource.Contains("NX_GemWithSparkles") &&
            !neutral.ShaderSource.Contains("NX_GemInteriorSparkle"),
            "Gem sparkle defaults neutral and prunes helper/evaluation");
        var activeGraph = MakeGraph(); activeGraph.Nodes.Single(n => n.Operation == "core.gem").Properties["sparkleStrength"] = 1;
        var active = ShaderEmitter.Emit(activeGraph);
        assert(active.Succeeded && active.ShaderSource.Contains("NX_GemWithSparkles") && active.ShaderSource.Contains("if(sparkleStrength>0)") &&
            active.ShaderSource.Contains("NX_GemInteriorSparkle") &&
            active.ShaderSource.Contains("[unroll] for(int i=0;i<4;i++)") &&
            active.ShaderSource.Contains("unity_WorldToObject") &&
            active.ShaderSource.Contains("refract(-worldView,worldNormal,1/max(ior,1))") &&
            active.ShaderSource.Contains("refractedWorld=-worldView"),
            "Gem has four bounded object-space sparkle samples and degenerate ray fallback");

        foreach (var property in new[] { "sparkleStrength", "sparkleDensity", "sparkleSize", "sparkleDepth" })
        {
            var changed = MakeGraph(); changed.Nodes.Single(n => n.Operation == "core.gem").Properties["sparkleStrength"] = 1;
            changed.Nodes.Single(n => n.Operation == "core.gem").Properties[property] = property == "sparkleStrength" ? 2.0 : .8;
            var result = ShaderEmitter.Emit(changed);
            assert(result.Succeeded && result.ShaderSource != active.ShaderSource,
                "Gem " + property + " changes emitted sparkle controls");
        }

        var connected = MakeGraph();
        var effect = connected.Nodes.Single(n => n.Operation == "core.gem");
        foreach (var port in new[] { "sparkleStrength", "sparkleDensity", "sparkleSize", "sparkleDepth" })
        {
            var value = NodeCatalog.Create("core.value"); value.Id = "value-" + port; value.Properties["value"] = .25;
            connected.Nodes.Add(value);
            connected.Connections.Add(new GraphConnection
            {
                Id = "connect-" + port,
                From = new GraphPortRef { NodeId = value.Id, PortId = "value" },
                To = new GraphPortRef { NodeId = effect.Id, PortId = port }
            });
        }
        var connectedResult = ShaderEmitter.Emit(connected);
        assert(GraphValidator.Validate(connected).IsValid && connectedResult.Succeeded &&
            connectedResult.ShaderSource.Contains("NX_GemInteriorSparkle"),
            "Gem exposes numeric sparkle controls as connectable inputs");

        var degenerate = MakeGraph();
        var malformed = MakeGraph();
        malformed.Nodes.Single(n => n.Operation == "core.gem").Properties["sparkleColor"] = "invalid";
        assert(!GraphValidator.Validate(malformed).IsValid, "Malformed Gem sparkle color is rejected before emission");
        var normal = NodeCatalog.Create("core.constant"); normal.Id = "zero-normal";
        normal.Properties["valueType"] = "vector3"; normal.Properties["value"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0);
        degenerate.Nodes.Add(normal);
        degenerate.Connections.Add(new GraphConnection
        {
            Id = "zero-normal-to-gem",
            From = new GraphPortRef { NodeId = normal.Id, PortId = "value" },
            To = new GraphPortRef { NodeId = "gem", PortId = "normal" }
        });
        degenerate.Nodes.Single(n => n.Operation == "core.gem").Properties["sparkleStrength"] = 1;
        var degenerateResult = ShaderEmitter.Emit(degenerate);
        assert(degenerateResult.Succeeded && degenerateResult.ShaderSource.Contains("NX_DetailSafeNormal") &&
            degenerateResult.ShaderSource.Contains("dot(refractedWorld,refractedWorld)<.000001"),
            "Gem handles zero normal and degenerate refracted endpoint safely");
    }

    static ShaderGraph MakeGraph()
    {
        var graph = new ShaderGraph { GraphId = "gem-sparkle-portable" };
        var gem = NodeCatalog.Create("core.gem"); gem.Id = "gem"; graph.Nodes.Add(gem);
        var surface = NodeCatalog.Create("core.pbrSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection
        {
            Id = "gem-to-surface",
            From = new GraphPortRef { NodeId = gem.Id, PortId = "color" },
            To = new GraphPortRef { NodeId = surface.Id, PortId = "albedo" }
        });
        graph.Connections.Add(new GraphConnection
        {
            Id = "surface-to-output",
            From = new GraphPortRef { NodeId = surface.Id, PortId = "surface" },
            To = new GraphPortRef { NodeId = output.Id, PortId = "surface" }
        });
        return graph;
    }
}

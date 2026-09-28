using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ToonLayerChecks
{
    public static void Run(Action<bool, string> check)
    {
        var graph = RenderingOptionsChecks.Graph("core.toonSurface");
        var surface = graph.Nodes.Single(n => n.Id == "surface");
        surface.Properties["lightingMode"] = 3;
        var roundTrip = GraphJson.Parse(GraphJson.Serialize(graph));
        var layered = ShaderEmitter.Emit(roundTrip);
        check(layered.Succeeded, "Layered toon round trips and emits");
        check(layered.ShaderSource.Contains("toonLayer1Lit") && layered.ShaderSource.Contains("toonLayer2Lit") && layered.ShaderSource.Contains("toonLayer3Lit"), "Layered toon emits all three independent shadow responses");
        check(layered.ShaderSource.Contains("float4(0.35,0.25,0.5,1)") && layered.ShaderSource.Contains("float4(0.08,0.04,0.15,1)"), "Layered toon uses distinct default tints");

        surface.Properties["shadowLayers"] = 1;
        var oneLayer = ShaderEmitter.Emit(graph);
        check(oneLayer.Succeeded && oneLayer.ShaderSource.Contains("toonLayer1Lit") && !oneLayer.ShaderSource.Contains("toonLayer2Lit"), "Shadow layer count limits emitted layers");

        surface.Properties["shadowLayers"] = 3;
        surface.Properties["shadowStrength2"] = 0;
        surface.Properties["shadowStrength3"] = 0;
        var disabled = ShaderEmitter.Emit(graph);
        check(disabled.Succeeded && !disabled.ShaderSource.Contains("toonLayer2Lit") && !disabled.ShaderSource.Contains("toonLayer3Lit"), "Statically zero secondary layers emit no shading code");

        var zero = NodeCatalog.Create("core.constant");
        zero.Id = "zeroStrength";
        zero.Properties["valueType"] = "float";
        zero.Properties["value"] = 0;
        graph.Nodes.Add(zero);
        RenderingOptionsChecks.Edge(graph, "zeroStrength", "value", "surface", "shadowStrength2");
        surface.Properties["shadowLayers"] = 2;
        var wiredZero = ShaderEmitter.Emit(graph);
        check(wiredZero.Succeeded && wiredZero.ShaderSource.Contains("toonLayer2Lit"), "Connected zero strength keeps secondary layer code live");

        foreach (var invalidMode in new[] { -1, 4 })
        {
            surface.Properties["lightingMode"] = invalidMode;
            check(!GraphValidator.Validate(graph).IsValid && !ShaderEmitter.Emit(graph).Succeeded, "Invalid toon lighting mode is rejected: " + invalidMode);
        }
    }
}

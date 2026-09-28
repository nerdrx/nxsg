using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;
using Newtonsoft.Json.Linq;

public static class ToonCompletionChecks
{
    static readonly string[] NewProperties = { "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3", "rimColor", "rimStrength", "rimWidth", "rimSoftness", "rimLightAlignment" };

    public static void Run(Action<bool, string> check)
    {
        var graph = RenderingOptionsChecks.Graph("core.toonSurface");
        var surface = graph.Nodes.Single(n => n.Id == "surface");
        surface.Properties["lightingMode"] = 3;
        var defaults = ShaderEmitter.Emit(graph);
        var legacy = GraphJson.Parse(GraphJson.Serialize(graph));
        foreach (var key in NewProperties) legacy.Nodes.Single(n => n.Id == "surface").Properties.Remove(key);
        var legacyResult = ShaderEmitter.Emit(legacy);
        check(defaults.Succeeded && legacyResult.Succeeded && defaults.ShaderSource == legacyResult.ShaderSource, "Default per-layer shadow and zero rim preserve legacy shader source exactly");
        var bare = new ShaderGraph { GraphId = "bare-rim" };
        bare.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.toonSurface", Properties = new JObject { ["rimStrength"] = .8 } });
        bare.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        RenderingOptionsChecks.Edge(bare, "surface", "surface", "output", "surface");
        var bareRim = ShaderEmitter.Emit(bare);
        check(bareRim.Succeeded && bareRim.ShaderSource.Contains("nxRimViewEdge"), "Rim strength activates integrated rim on a bare legacy toon node");
        check(NodeCatalog.PortType(surface, "layerReceiveShadow") == "float" && NodeCatalog.PortType(surface, "layerReceiveShadow2") == "float" && NodeCatalog.PortType(surface, "layerReceiveShadow3") == "float", "Each layer receive-shadow control has a connectable float socket");
        check(NodeCatalog.PortType(surface, "rimStrength") == "float" && NodeCatalog.PortType(surface, "rimWidth") == "float" && NodeCatalog.PortType(surface, "rimSoftness") == "float" && NodeCatalog.PortType(surface, "rimLightAlignment") == "float", "Rim controls have connectable float sockets");

        foreach (var layer in new[] { 1, 2, 3 })
        {
            var suffix = layer == 1 ? string.Empty : layer.ToString();
            surface.Properties["layerReceiveShadow" + suffix] = 0.0;
            var result = ShaderEmitter.Emit(graph);
            check(result.Succeeded && result.ShaderSource.Contains("nxSceneShadow") && result.ShaderSource.Contains("toonLayer" + layer + "ShadeTarget"), "Layer " + layer + " has independent scene-map response");
            surface.Properties["layerReceiveShadow" + suffix] = 1.0;
        }

        surface.Properties["layerReceiveShadow2"] = .5;
        surface.Properties["rimStrength"] = 1.0;
        var rim = ShaderEmitter.Emit(graph);
        check(rim.Succeeded && rim.ShaderSource.Contains("nxRimViewEdge") && rim.ShaderSource.Contains("nxRimLight") && rim.ShaderSource.Contains("lerp(toonResponse,(float4(1,1,1,1)).rgb*nxToonGlobalScene"), "Rim tint enters direct toon response and tracks view edge and key-light alignment");
        surface.Properties["rimStrength"] = 0.0;
        check(!ShaderEmitter.Emit(graph).ShaderSource.Contains("nxRimViewEdge"), "Static zero rim strength prunes rim shader code");

        surface.Properties["layerReceiveShadow2"] = 2.0;
        surface.Properties["rimWidth"] = -2.0;
        check(GraphValidator.Validate(graph).IsValid, "Finite typed values outside slider range remain valid and saturate in shader math");
        surface.Properties["layerReceiveShadow2"] = 1.0;
        surface.Properties["rimWidth"] = .2;
        var zero = NodeCatalog.Create("core.value");
        zero.Id = "connectedZero";
        zero.Properties["value"] = 0.0;
        graph.Nodes.Add(zero);
        RenderingOptionsChecks.Edge(graph, "connectedZero", "value", "surface", "layerReceiveShadow3");
        var connected = ShaderEmitter.Emit(graph);
        check(connected.Succeeded && connected.ShaderSource.Contains("toonLayer3ShadeTarget"), "Connected layer control remains independently live at zero");
    }
}

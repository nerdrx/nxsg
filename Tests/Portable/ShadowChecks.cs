using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ShadowChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "screen-shadow" };
        var color = new GraphNode { Id = "color", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } };
        var refraction = NodeCatalog.Create("core.refraction"); refraction.Id = "refraction";
        var luminance = NodeCatalog.Create("core.luminance"); luminance.Id = "luminance";
        var toon = NodeCatalog.Create("core.toonSurface"); toon.Id = "toon";
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        graph.Nodes.Add(color); graph.Nodes.Add(refraction); graph.Nodes.Add(luminance); graph.Nodes.Add(toon); graph.Nodes.Add(output);
        Connect(graph, "color", "value", "refraction", "color");
        Connect(graph, "refraction", "color", "luminance", "color");
        Connect(graph, "refraction", "color", "toon", "albedo");
        Connect(graph, "luminance", "value", "toon", "opacity");
        Connect(graph, "toon", "surface", "output", "surface");

        var result = ShaderEmitter.Emit(graph);
        assert(result.Succeeded, "screen-dependent graph still emits: " + string.Join(";", result.Diagnostics.Select(d => d.Message)));
        assert(result.Diagnostics.Any(d => d.Code == "shadow.screenDependent"), "screen-dependent shadow warning emitted");
        assert(!result.ShaderSource.Contains("Name \"ShadowCaster\""), "screen-dependent graph omits invalid shadow caster");
        // A camera-dependent color must not remove depth when its alpha is ignored.
        toon.Properties["useAlbedoAlpha"] = 0;
        graph.Connections.RemoveAll(e => e.To.NodeId == "toon" && e.To.PortId == "opacity");
        foreach (var surface in new[] { "core.toonSurface", "core.pbrSurface", "core.unlitSurface" })
        {
            toon.Operation = surface;
            var opaque = ShaderEmitter.Emit(graph);
            assert(opaque.Succeeded && opaque.ShaderSource.Contains("Name \"ShadowCaster\""), surface + " keeps depth with camera-dependent color and ignored alpha");
            assert(!opaque.Diagnostics.Any(d => d.Code == "shadow.screenDependent"), "ignored albedo alpha has no shadow warning");
        }
        Connect(graph, "luminance", "value", "toon", "opacity");
        var masked = ShaderEmitter.Emit(graph);
        assert(masked.Succeeded && !masked.ShaderSource.Contains("Name \"ShadowCaster\""), "camera-dependent explicit opacity still omits invalid caster");
    }

    static void Connect(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = Guid.NewGuid().ToString("N"),
            From = new GraphPortRef { NodeId = from, PortId = port },
            To = new GraphPortRef { NodeId = to, PortId = input }
        });
    }
}

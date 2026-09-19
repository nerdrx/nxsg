using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class PixelLightChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var toon = Surface("core.toonSurface");
        var toonResult = ShaderEmitter.Emit(toon);
        assert(toonResult.Succeeded, "ForcePixel Toon emits: " + Diagnostics(toonResult));
        assert(toonResult.ShaderSource.Contains("Name \"ForwardAdd\""), "ForcePixel Toon emits ForwardAdd");
        assert(toonResult.ShaderSource.Contains("ForwardAdd"), "ForcePixel Toon tags ForwardAdd");
        assert(toonResult.ShaderSource.Contains("multi_compile_fwdadd"), "ForcePixel Toon compiles additional-light variants");
        assert(!toonResult.Diagnostics.Any(d => d.Code == "lighting.forwardAdd"), "base Toon has no obsolete ForwardAdd warning");

        var pbr = Surface("core.pbrSurface");
        var pbrResult = ShaderEmitter.Emit(pbr);
        assert(pbrResult.Succeeded, "ForcePixel PBR emits: " + Diagnostics(pbrResult));
        assert(pbrResult.ShaderSource.Contains("Name \"ForwardAdd\""), "ForcePixel PBR emits ForwardAdd");

        var advancedToon = Surface("core.toonSurface");
        advancedToon.Nodes.Single(n => n.Id == "surface").Properties["opacity"] = .75;
        var advancedResult = ShaderEmitter.Emit(advancedToon);
        assert(advancedResult.Succeeded, "ForcePixel advanced Toon emits: " + Diagnostics(advancedResult));
        assert(advancedResult.ShaderSource.Contains("Name \"ForwardAdd\""), "ForcePixel advanced Toon emits ForwardAdd");

        var unlit = Surface("core.unlitSurface");
        var unlitResult = ShaderEmitter.Emit(unlit);
        assert(unlitResult.Succeeded, "ForcePixel Unlit emits: " + Diagnostics(unlitResult));
        assert(!unlitResult.ShaderSource.Contains("Name \"ForwardAdd\""), "Unlit remains single pass");

        var shell = new ShaderGraph { GraphId = "pixel-shell" };
        var baseSurface = NodeCatalog.Create("core.toonSurface"); baseSurface.Id = "base";
        var layerSurface = NodeCatalog.Create("core.toonSurface"); layerSurface.Id = "layer";
        var shellNode = NodeCatalog.Create("core.shell"); shellNode.Id = "shell";
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        shell.Nodes.Add(baseSurface); shell.Nodes.Add(layerSurface); shell.Nodes.Add(shellNode); shell.Nodes.Add(output);
        Connect(shell, "base", "surface", "shell", "base");
        Connect(shell, "layer", "surface", "shell", "layer");
        Connect(shell, "shell", "surface", "output", "surface");
        var shellResult = ShaderEmitter.Emit(shell);
        assert(shellResult.Succeeded, "ForcePixel shell emits: " + Diagnostics(shellResult));
        var shellWarnings = shellResult.Diagnostics.Where(d => d.Code == "lighting.forwardAdd").ToArray();
        assert(shellWarnings.Any(), "shell retains additional-light limitation warning");
        assert(shellWarnings.All(d => d.Path.Contains("shell")), "shell warning stays scoped");
    }

    static ShaderGraph Surface(string operation)
    {
        var graph = new ShaderGraph { GraphId = "pixel-light-" + operation };
        graph.Nodes.Add(NodeCatalog.Create(operation)); graph.Nodes[0].Id = "surface";
        var albedo = new GraphNode { Id = "albedo", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } };
        graph.Nodes.Add(albedo);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Connect(graph, "albedo", "value", "surface", "albedo");
        Connect(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static void Connect(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static string Diagnostics(EmissionResult result) { return string.Join(";", result.Diagnostics.Select(d => d.Code + ":" + d.Message)); }
}

using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class VisualNodeChecks
{
    private static readonly string[] Operations =
    {
        "core.position", "core.normalDirection", "core.viewDirection", "core.vertexColor", "core.cameraDistance", "core.screenUV",
        "core.circleMask", "core.boxMask", "core.polygonMask", "core.starMask", "core.radialRays", "core.spiral", "core.brick", "core.hexGrid",
        "core.triplanarTexture", "core.matcapTexture", "core.rimGlow", "core.heightMask", "core.slopeMask", "core.distanceFade", "core.wireframe"
    };

    public static void Run(Action<bool, string> assert)
    {
        foreach (var operation in Operations)
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation), operation + " is catalogued");
            var graph = Build(operation);
            var saved = GraphJson.Parse(GraphJson.Serialize(graph));
            var result = ShaderEmitter.Emit(saved);
            assert(GraphValidator.Validate(saved).IsValid && result.Succeeded, operation + " validates, saves and emits");
            assert(NodeCatalog.Ports(operation, false).Length >= 0 && NodeCatalog.Ports(operation, true).Length > 0, operation + " has ports");
        }
    }

    private static ShaderGraph Build(string operation)
    {
        var graph = new ShaderGraph { GraphId = "visual-" + operation };
        var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        if (operation == "core.triplanarTexture" || operation == "core.matcapTexture")
        {
            effect.Properties["resourceId"] = "texture";
            graph.Resources.Add(new GraphResource { Id = "texture", Kind = "texture2D", Uri = "builtin://white" });
        }
        if (NodeCatalog.Ports(operation, false).Contains("uv"))
        {
            var uv = NodeCatalog.Create("core.uv0"); uv.Id = "uv"; graph.Nodes.Add(uv); Edge(graph, "uv", "uv", "effect", "uv");
        }
        if (operation == "core.triplanarTexture")
        {
            var position = NodeCatalog.Create("core.position"); position.Id = "position"; graph.Nodes.Add(position); Edge(graph, "position", "position", "effect", "position");
            var normal = NodeCatalog.Create("core.normalDirection"); normal.Id = "normal"; graph.Nodes.Add(normal); Edge(graph, "normal", "normal", "effect", "normal");
        }
        if (operation == "core.matcapTexture")
        {
            var normal = NodeCatalog.Create("core.normalDirection"); normal.Id = "normal"; graph.Nodes.Add(normal); Edge(graph, "normal", "normal", "effect", "normal");
        }
        if (operation == "core.rimGlow")
        {
            var color = NodeCatalog.Create("core.constant"); color.Id = "color"; graph.Nodes.Add(color); Edge(graph, "color", "value", "effect", "color");
        }
        if (operation == "core.heightMask")
        {
            var position = NodeCatalog.Create("core.position"); position.Id = "position"; graph.Nodes.Add(position); Edge(graph, "position", "position", "effect", "position");
        }
        if (operation == "core.slopeMask")
        {
            var normal = NodeCatalog.Create("core.normalDirection"); normal.Id = "normal"; graph.Nodes.Add(normal); Edge(graph, "normal", "normal", "effect", "normal");
        }
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        var effectPort = NodeCatalog.Ports(operation, true)[0];
        var effectType = NodeCatalog.PortType(effect, effectPort);
        if (effectType == "vector2" || effectType == "vector3")
        {
            var preview = NodeCatalog.Create("core.previewVector"); preview.Id = "preview"; graph.Nodes.Add(preview); Edge(graph, "effect", effectPort, "preview", effectType == "vector2" ? "uv" : "normal"); Edge(graph, "preview", "color", "surface", "albedo");
            AddSurface(graph); Edge(graph, "surface", "surface", "output", "surface");
        }
        else
        {
            AddSurface(graph);
            Edge(graph, "effect", effectPort, "surface", effectType == "color" ? "albedo" : "opacity");
            if (effectType != "color") { var albedo = NodeCatalog.Create("core.constant"); albedo.Id = "albedo"; graph.Nodes.Add(albedo); Edge(graph, "albedo", "value", "surface", "albedo"); }
            Edge(graph, "surface", "surface", "output", "surface");
        }
        graph.Nodes.Add(output);
        return graph;
    }

    private static void AddSurface(ShaderGraph graph) { var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface); }
    private static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}

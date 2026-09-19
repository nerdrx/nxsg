using System;
using NXSG.Backend;
using NXSG.Core;

public static class ShinyNodeChecks
{
    static readonly string[] Nodes = { "core.iridescence", "core.refraction", "core.interiorMapping", "core.textureBomb", "core.subsurface" };

    public static void Run(Action<bool, string> assert)
    {
        foreach (var operation in Nodes)
        {
            var graph = Build(operation);
            var validation = GraphValidator.Validate(graph);
            var emission = ShaderEmitter.Emit(graph);
            assert(validation.IsValid, operation + " validates with finite defaults");
            assert(emission.Succeeded, operation + " emits a reachable color effect");
            if (operation == "core.textureBomb") assert(emission.ShaderSource.Contains("NX_BombUV"), operation + " uses deterministic cell transform helper");
            if (operation == "core.interiorMapping") assert(emission.ShaderSource.Contains("rooms"), operation + " emits room grid mapping");
        }
    }

    static ShaderGraph Build(string operation)
    {
        var graph = new ShaderGraph { GraphId = "shiny-" + operation };
        var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        if (operation == "core.interiorMapping" || operation == "core.textureBomb")
        {
            effect.Properties["resourceId"] = "atlas";
            graph.Resources.Add(new GraphResource { Id = "atlas", Kind = "texture2D", Uri = "builtin://white" });
        }
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Edge(graph, "effect", "color", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
}

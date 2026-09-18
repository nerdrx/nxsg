using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class DistortionChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var operations = new[] { "core.uvDistort", "core.gradient", "core.uvTile", "core.posterize" };
        foreach (var operation in operations)
            assert(NodeCatalog.Create(operation) != null && NodeCatalog.IsKnown(operation), operation + " catalog create");

        CheckSocketTypes(assert);
        CheckModes(assert);
        CheckInvalidProperties(assert);
        CheckEmissionDoesNotMutateSource(assert);
    }

    private static void CheckSocketTypes(Action<bool, string> assert)
    {
        var uvDistort = NodeCatalog.Create("core.uvDistort");
        assert(NodeCatalog.Ports("core.uvDistort", false).SequenceEqual(new[] { "uv", "strength", "mask", "time", "flow" }), "uvDistort inputs");
        assert(NodeCatalog.Ports("core.uvDistort", true).SequenceEqual(new[] { "uv", "offset" }), "uvDistort outputs");
        assert(NodeCatalog.PortType(uvDistort, "uv") == "vector2" && NodeCatalog.PortType(uvDistort, "strength") == "float" &&
            NodeCatalog.PortType(uvDistort, "mask") == "float" && NodeCatalog.PortType(uvDistort, "time") == "float" &&
            NodeCatalog.PortType(uvDistort, "flow") == "color" && NodeCatalog.PortType(uvDistort, "offset") == "vector2",
            "uvDistort runtime socket types");

        foreach (var operation in new[] { "core.gradient", "core.uvTile", "core.posterize" })
            assert(NodeCatalog.Create(operation) != null, operation + " runtime socket type node");
        var gradient = NodeCatalog.Create("core.gradient");
        assert(NodeCatalog.PortType(gradient, "uv") == "vector2" && NodeCatalog.PortType(gradient, "value") == "float" && NodeCatalog.PortType(gradient, "color") == "color", "gradient runtime socket types");
        var tile = NodeCatalog.Create("core.uvTile");
        assert(NodeCatalog.PortType(tile, "uv") == "vector2", "uvTile runtime socket types");
        var posterize = NodeCatalog.Create("core.posterize");
        assert(NodeCatalog.PortType(posterize, "value") == "float" && NodeCatalog.PortType(posterize, "levels") == "float" && NodeCatalog.PortType(posterize, "value") == "float", "posterize runtime socket types");
    }

    private static void CheckModes(Action<bool, string> assert)
    {
        for (var mode = 0; mode <= 6; mode++) EmitMode(assert, "core.uvDistort", mode, "distort-mode-" + mode);
        for (var mode = 0; mode <= 2; mode++)
        {
            EmitMode(assert, "core.gradient", mode, "gradient-mode-" + mode);
            EmitMode(assert, "core.uvTile", mode, "tile-mode-" + mode);
        }
        EmitMode(assert, "core.posterize", 0, "posterize");
    }

    private static void EmitMode(Action<bool, string> assert, string operation, int mode, string label)
    {
        var graph = BuildGraph(operation, mode);
        var result = ShaderEmitter.Emit(graph);
        assert(result.Succeeded, label + " emits: " + string.Join(";", result.Diagnostics.Select(d => d.Message)));
    }

    private static void CheckInvalidProperties(Action<bool, string> assert)
    {
        Reject(assert, "core.uvDistort", "mode", 7, "uvDistort mode range");
        Reject(assert, "core.uvDistort", "detail", 0, "uvDistort detail range");
        Reject(assert, "core.uvDistort", "radius", 0, "uvDistort radius range");
        Reject(assert, "core.uvDistort", "falloff", -1, "uvDistort falloff range");
        Reject(assert, "core.uvDistort", "coordinateSource", "bad", "uvDistort coordinate source choice");
        Reject(assert, "core.gradient", "mode", 3, "gradient mode range");
        Reject(assert, "core.gradient", "radius", 0, "gradient radius range");
        Reject(assert, "core.uvTile", "mode", 3, "uvTile mode range");
        Reject(assert, "core.posterize", "levels", 1, "posterize levels range");
        Reject(assert, "core.posterize", "levels", 257, "posterize levels range high");
    }

    private static void Reject(Action<bool, string> assert, string operation, string property, object value, string label)
    {
        var node = NodeCatalog.Create(operation);
        node.Properties[property] = JToken.FromObject(value);
        var graph = BuildGraph(operation, 0);
        graph.Nodes.Single(n => n.Operation == operation).Properties[property] = JToken.FromObject(value);
        assert(!GraphValidator.Validate(graph).IsValid, label);
    }

    private static void CheckEmissionDoesNotMutateSource(Action<bool, string> assert)
    {
        foreach (var operation in new[] { "core.uvDistort", "core.gradient", "core.uvTile", "core.posterize" })
        {
            var graph = BuildGraph(operation, 0);
            var before = GraphJson.Serialize(graph);
            var first = ShaderEmitter.Emit(graph);
            var after = GraphJson.Serialize(graph);
            var second = ShaderEmitter.Emit(graph);
            assert(before == after, operation + " emission preserves source");
            assert(first.Succeeded && first.ShaderSource == second.ShaderSource, operation + " emission deterministic");
        }
    }

    private static ShaderGraph BuildGraph(string operation, int mode)
    {
        var graph = new ShaderGraph { GraphId = "distortion-" + operation };
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        var node = NodeCatalog.Create(operation); node.Id = "effect"; node.Properties["mode"] = mode;
        if (operation == "core.uvDistort")
        {
            node.Properties["detail"] = 3; node.Properties["strength"] = .05; node.Properties["mask"] = 1.0;
            node.Properties["time"] = 0.0; node.Properties["flow"] = new JArray(1, 1); node.Properties["radius"] = .5;
            node.Properties["falloff"] = 1.0; node.Properties["coordinateSource"] = "uv0";
        }
        if (operation == "core.gradient") { node.Properties["center"] = new JArray(.5, .5); node.Properties["angle"] = 0.0; node.Properties["radius"] = .5; }
        if (operation == "core.uvTile") { node.Properties["tiling"] = new JArray(1, 1); node.Properties["offset"] = new JArray(0, 0); }
        if (operation == "core.posterize") node.Properties["levels"] = 4.0;
        graph.Nodes.Add(node);
        if (operation == "core.posterize") { graph.Nodes.Add(new GraphNode { Id="number", Operation="core.value", Properties=new JObject { ["value"] = .63 } }); Connect(graph, "number", "value", "effect", "value", "in"); }
        else Connect(graph, "uv", "uv", "effect", "uv", "in");

        if (operation == "core.uvDistort" || operation == "core.uvTile")
        {
            graph.Nodes.Add(new GraphNode { Id = "preview", Operation = "core.previewVector" });
            graph.Nodes.Add(NodeCatalog.Create("core.unlitSurface")); graph.Nodes[graph.Nodes.Count - 1].Id = "surface";
            graph.Nodes.Add(NodeCatalog.Create("core.output")); graph.Nodes[graph.Nodes.Count - 1].Id = "output";
            Connect(graph, "effect", "uv", "preview", "uv", "preview-uv"); Connect(graph, "preview", "color", "surface", "albedo", "preview-color");
            Connect(graph, "surface", "surface", "output", "surface", "out");
        }
        else
        {
            graph.Nodes.Add(NodeCatalog.Create("core.unlitSurface")); graph.Nodes[graph.Nodes.Count - 1].Id = "surface";
            graph.Nodes.Add(NodeCatalog.Create("core.output")); graph.Nodes[graph.Nodes.Count - 1].Id = "output";
            Connect(graph, "effect", operation == "core.gradient" ? "color" : "value", "surface", "albedo", "effect-color");
            Connect(graph, "surface", "surface", "output", "surface", "out");
        }
        return graph;
    }

    private static void Connect(ShaderGraph graph, string from, string port, string to, string input, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
}

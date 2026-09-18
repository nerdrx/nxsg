using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ProceduralChecks
{
    static readonly string[] Operations = { "core.noise", "core.musgrave", "core.voronoi", "core.checker", "core.wave" };

    public static void Run(Action<bool, string> assert)
    {
        CheckCatalog(assert);
        CheckDimensions(assert);
        CheckInvalidValues(assert);
        CheckLegacyNoise(assert);
        CheckCoordinateSources(assert);
    }

    static void CheckCatalog(Action<bool, string> assert)
    {
        foreach (var operation in Operations)
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation), operation + " is catalogued");
            assert((int)node.Properties["dimensions"] == 2, operation + " defaults to 2 dimensions");
            assert((string)node.Properties["coordinateSource"] == "uv0", operation + " defaults to UV0");
        }
        var noise = NodeCatalog.Create("core.noise");
        assert((double)noise.Properties["scale"] == 5 && (double)noise.Properties["speed"] == 1, "noise defaults");
        foreach (var operation in Operations.Skip(1))
            assert((double)NodeCatalog.Create(operation).Properties["scale"] == 5 && (double)NodeCatalog.Create(operation).Properties["speed"] == 0, operation + " defaults");
        var musgrave = NodeCatalog.Create("core.musgrave");
        assert((int)musgrave.Properties["octaves"] == 4 && (double)musgrave.Properties["lacunarity"] == 2 &&
               (double)musgrave.Properties["gain"] == .5 && (int)musgrave.Properties["mode"] == 0, "musgrave defaults");
    }

    static void CheckDimensions(Action<bool, string> assert)
    {
        foreach (var operation in Operations)
            foreach (var dimensions in operation == "core.noise" ? new[] { 1, 2, 3, 4 } : new[] { 2, 3 })
            {
                var graph = Graph(operation, dimensions);
                var validation = GraphValidator.Validate(graph);
                var result = ShaderEmitter.Emit(graph);
                assert(validation.IsValid && result.Succeeded,
                    operation + " dimension " + dimensions + " validates and emits");
            }
    }

    static void CheckInvalidValues(Action<bool, string> assert)
    {
        var badDimension = Graph("core.noise", 2);
        badDimension.Nodes.Single(n => n.Id == "procedural").Properties["dimensions"] = 5;
        assert(!GraphValidator.Validate(badDimension).IsValid, "dimensions outside 1..4 reject");

        var badType = Graph("core.noise", 2);
        badType.Nodes.Single(n => n.Id == "procedural").Properties["scale"] = "five";
        assert(!GraphValidator.Validate(badType).IsValid, "invalid procedural property type rejects");

        foreach (var property in new[] { "coordinateSource", "coordinateSpace" })
        {
            var badEnum = Graph("core.noise", 3);
            badEnum.Nodes.Single(n => n.Id == "procedural").Properties[property] = "invalid";
            assert(!GraphValidator.Validate(badEnum).IsValid, property + " enum rejects invalid value");
        }

        var badMusgrave = Graph("core.musgrave", 2);
        var node = badMusgrave.Nodes.Single(n => n.Id == "procedural");
        node.Properties["octaves"] = 9; node.Properties["lacunarity"] = 5; node.Properties["gain"] = 2; node.Properties["mode"] = 3;
        assert(!GraphValidator.Validate(badMusgrave).IsValid, "musgrave structural ranges reject invalid values");
        var unboundedMusgrave = Graph("core.musgrave", 2);
        node = unboundedMusgrave.Nodes.Single(n => n.Id == "procedural");
        node.Properties["lacunarity"] = 5; node.Properties["gain"] = -2;
        var roundTripMusgrave = GraphJson.Parse(GraphJson.Serialize(unboundedMusgrave));
        assert(GraphValidator.Validate(roundTripMusgrave).IsValid && ShaderEmitter.Emit(roundTripMusgrave).Succeeded, "musgrave values outside sliders round trip and emit");
        var unboundedVoronoi = Graph("core.voronoi", 2);
        unboundedVoronoi.Nodes.Single(n => n.Id == "procedural").Properties["randomness"] = -1;
        var roundTripVoronoi = GraphJson.Parse(GraphJson.Serialize(unboundedVoronoi));
        assert(GraphValidator.Validate(roundTripVoronoi).IsValid && ShaderEmitter.Emit(roundTripVoronoi).Succeeded, "voronoi randomness outside slider round trips and emits");
        var badWave = Graph("core.wave", 2);
        var wave = badWave.Nodes.Single(n => n.Id == "procedural"); wave.Properties["mode"] = 2; wave.Properties["axis"] = 3;
        assert(!GraphValidator.Validate(badWave).IsValid, "wave enum ranges reject");
    }

    static void CheckLegacyNoise(Action<bool, string> assert)
    {
        var legacy = Graph("core.noise", 2, false);
        var explicitDefaults = Graph("core.noise", 2, true);
        foreach (var graph in new[] { legacy, explicitDefaults })
        {
            graph.Nodes.Single(n => n.Id == "surface").Operation = "core.toonSurface";
            graph.Connections.RemoveAll(e => e.To.NodeId == "procedural" && (e.To.PortId == "x" || e.To.PortId == "position"));
        }
        var oldResult = ShaderEmitter.Emit(legacy);
        var newResult = ShaderEmitter.Emit(explicitDefaults);
        assert(oldResult.Succeeded && newResult.Succeeded, "legacy and explicit 2D noise emit");
        assert(oldResult.ShaderSource.Contains("NXSG_ValueNoise") && newResult.ShaderSource.Contains("NXSG_ValueNoise"), "legacy noise keeps old source marker");
        assert(oldResult.ShaderSource == newResult.ShaderSource, "legacy 2D noise source stays equivalent");
    }

    static void CheckCoordinateSources(Action<bool, string> assert)
    {
        foreach (var source in new[] { "uv1", "uv2", "uv3" })
        {
            var graph = Graph("core.noise", 2);
            graph.Nodes.Single(n => n.Id == "procedural").Properties["coordinateSource"] = source;
            var result = ShaderEmitter.Emit(graph);
            assert(result.Succeeded && result.ShaderSource.Contains(source), source + " coordinate source reaches emitted shader");
        }
    }

    static ShaderGraph Graph(string operation, int dimensions, bool explicitProperties = true)
    {
        var graph = new ShaderGraph { GraphId = operation + "-" + dimensions };
        var procedural = NodeCatalog.Create(operation); procedural.Id = "procedural";
        if (explicitProperties) procedural.Properties["dimensions"] = dimensions;
        else foreach (var property in new[] { "dimensions", "coordinateSource", "coordinateSpace", "scale", "speed" }) procedural.Properties.Remove(property);
        if (dimensions < 3) procedural.Properties.Remove("coordinateSpace");
        else procedural.Properties["coordinateSpace"] = "object";
        graph.Nodes.Add(procedural);
        graph.Nodes.Add(Node("x", "core.value", new JObject { ["value"] = .25 }));
        graph.Nodes.Add(Node("uv", "core.uv0"));
        graph.Nodes.Add(Node("position", "core.normalMap"));
        graph.Nodes.Add(Node("positionColor", "core.constant", new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) }));
        graph.Nodes.Add(Node("time", "core.time"));
        graph.Nodes.Add(Node("surface", "core.unlitSurface")); graph.Nodes.Add(Node("output", "core.output"));
        Connect(graph, "positionColor", "value", "position", "color");
        Connect(graph, "procedural", "color", "surface", "albedo");
        Connect(graph, "surface", "surface", "output", "surface");
        if (operation == "core.noise") Connect(graph, "x", "value", "procedural", "x");
        Connect(graph, "uv", "uv", "procedural", "uv");
        Connect(graph, "position", "normal", "procedural", "position");
        Connect(graph, "time", "value", "procedural", "time");
        return graph;
    }

    static GraphNode Node(string id, string operation, JObject properties = null)
    { return new GraphNode { Id = id, Operation = operation, Properties = properties ?? new JObject() }; }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}

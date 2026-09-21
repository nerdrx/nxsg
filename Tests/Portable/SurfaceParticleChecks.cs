using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class SurfaceParticleChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var particles = NodeCatalog.Create("core.surfaceParticles");
        assert(particles != null, "surfaceParticles is registered");
        assert(NodeCatalog.Ports("core.surfaceParticles", false).SequenceEqual(new[] { "base", "albedo", "emission", "opacity", "mask", "time", "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread", "edgeSharpness" }), "surfaceParticles inputs");
        assert(NodeCatalog.Ports("core.surfaceParticles", true).SequenceEqual(new[] { "surface" }), "surfaceParticles output");
        assert(NodeCatalog.PortType(particles, "base") == "surface" && NodeCatalog.PortType(particles, "albedo") == "color" && NodeCatalog.PortType(particles, "emission") == "color" && NodeCatalog.PortType(particles, "opacity") == "float" && NodeCatalog.PortType(particles, "mask") == "float" && NodeCatalog.PortType(particles, "time") == "float" && new[] { "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread", "edgeSharpness" }.All(port => NodeCatalog.PortType(particles, port) == "float"), "surfaceParticles socket types");
        assert((double)particles.Properties["density"] == .1 && (double)particles.Properties["size"] == .03 && (double)particles.Properties["lifetime"] == 2 && (double)particles.Properties["speed"] == .2 && (double)particles.Properties["gravity"] == 0 && (double)particles.Properties["spread"] == .05 && (int)particles.Properties["blendMode"] == 1 && (double)particles.Properties["opacity"] == 1 && (double)particles.Properties["mask"] == 1, "surfaceParticles defaults");
        var graph = Graph();
        assert(GraphValidator.Validate(graph).IsValid, "surfaceParticles graph validates");
        var emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded && emitted.ShaderSource != null, "surfaceParticles graph emits");
        assert(emitted.ShaderSource.Contains("maxvertexcount(16)") && emitted.ShaderSource.Contains("SV_PrimitiveID") && emitted.ShaderSource.Contains("TriangleStream"), "surfaceParticles emits bounded geometry pass");
        assert(emitted.ShaderSource.Contains("ForwardBase") && emitted.ShaderSource.Contains("originalLocal"), "surfaceParticles preserves base pass and source position");
        assert(emitted.ShaderSource.Contains("corner.particleAlpha = fade * particleMask * active;") && !emitted.ShaderSource.Contains("* _Color * input.color;"), "surface particles keep fade separate from source vertex colors");
        Reject(assert, graph, "edgeSharpness", new JValue(double.NaN), "sharpness finite");
        Reject(assert, graph, "lifetime", .0009, "lifetime lower bound"); Reject(assert, graph, "blendMode", 2, "blend mode choice"); Reject(assert, graph, "speed", new JValue(double.NaN), "speed finite");
        var wired = Graph();
        foreach (var port in new[] { "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread", "edgeSharpness" })
        {
            var value = NodeCatalog.Create("core.value"); value.Id = "input-" + port; value.Properties["value"] = .25;
            wired.Nodes.Add(value);
            wired.Connections.Add(new GraphConnection { Id = value.Id, From = new GraphPortRef { NodeId = value.Id, PortId = "value" }, To = new GraphPortRef { NodeId = "particles", PortId = port } });
        }
        var wiredResult = ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(wired)));
        assert(wiredResult.Succeeded && wiredResult.ShaderSource.Contains("#pragma hull hullEmit") && wiredResult.ShaderSource.Contains("tri[0].sourceUV.y"), "numeric sockets round trip and wired budget uses adaptive tessellation");
        var outsideSliders = Graph();
        var outsideNode = outsideSliders.Nodes.Single(n => n.Id == "particles");
        outsideNode.Properties["density"] = 1000; outsideNode.Properties["size"] = 100; outsideNode.Properties["spread"] = -10; outsideNode.Properties["emissionRate"] = -3;
        var roundTrip = GraphJson.Parse(GraphJson.Serialize(outsideSliders));
        assert(GraphValidator.Validate(roundTrip).IsValid && ShaderEmitter.Emit(roundTrip).Succeeded, "surface particle values outside sliders round trip and emit");
        var endpoint = Graph();
        var endpointNode = endpoint.Nodes.Single(n => n.Id == "particles");
        endpointNode.Properties["size"] = .0001f;
        endpointNode.Properties["lifetime"] = .001f;
        assert(GraphValidator.Validate(endpoint).IsValid, "Unity float slider endpoints validate before saving");
        assert(ShaderEmitter.Emit(endpoint).Succeeded, "Unity float endpoints build before saving");
        assert(GraphValidator.Validate(GraphJson.Parse(GraphJson.Serialize(endpoint))).IsValid, "slider endpoints validate after saving");
        endpointNode.Properties["size"] = (double).0001f - 1e-11;
        assert(GraphValidator.Validate(endpoint).IsValid, "values below slider endpoint validate");
        var gradient = NodeCatalog.Create("core.gradient"); gradient.Properties["radius"] = .000001f; endpoint.Nodes.Add(gradient);
        assert(GraphValidator.Validate(endpoint).IsValid, "shared range check accepts gradient float endpoint");
        var uvGraph = Graph();
        uvGraph.Nodes.Single(n => n.Id == "particles").Properties["sourceUV"] = 1;
        assert(ShaderEmitter.Emit(uvGraph).Succeeded, "source UV color mode emits");
        assert((int)NodeCatalog.Create("core.surfaceParticles").Properties["sourceUV"] == 0, "source UV mode preserves sprite default");
        Reject(assert, uvGraph, "sourceUV", 2, "source UV choice bounded");
        var nested = Graph(); nested.Nodes.Single(n => n.Id == "base").Operation = "core.surfaceParticles"; assert(!ShaderEmitter.Emit(nested).Succeeded, "surfaceParticles cannot nest itself");
    }
    private static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "surface-particle-checks" }; graph.Nodes.Add(ColorNode("black", new JArray(0, 0, 0, 1))); graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" }); graph.Nodes.Add(ColorNode("albedo", new JArray(1, 0, 0, 1))); graph.Nodes.Add(ColorNode("emission", new JArray(0, 0, 0, 1))); graph.Nodes.Add(new GraphNode { Id = "particles", Operation = "core.surfaceParticles" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, "black", "value", "base", "albedo"); Edge(graph, "base", "surface", "particles", "base"); Edge(graph, "albedo", "value", "particles", "albedo"); Edge(graph, "emission", "value", "particles", "emission"); Edge(graph, "particles", "surface", "output", "surface"); return graph;
    }
    private static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    private static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    private static void Reject(Action<bool, string> assert, ShaderGraph graph, string property, object value, string label) { var node = graph.Nodes.Single(n => n.Id == "particles"); var old = node.Properties[property]; node.Properties[property] = value as JToken ?? JToken.FromObject(value); assert(!GraphValidator.Validate(graph).IsValid, label); node.Properties[property] = old; }
}

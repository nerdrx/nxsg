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
        assert(NodeCatalog.Ports("core.surfaceParticles", false).SequenceEqual(new[] { "base", "albedo", "emission", "opacity", "mask", "time" }), "surfaceParticles inputs");
        assert(NodeCatalog.Ports("core.surfaceParticles", true).SequenceEqual(new[] { "surface" }), "surfaceParticles output");
        assert(NodeCatalog.PortType(particles, "base") == "surface" && NodeCatalog.PortType(particles, "albedo") == "color" && NodeCatalog.PortType(particles, "emission") == "color" && NodeCatalog.PortType(particles, "opacity") == "float" && NodeCatalog.PortType(particles, "mask") == "float" && NodeCatalog.PortType(particles, "time") == "float", "surfaceParticles socket types");
        assert((double)particles.Properties["density"] == .1 && (double)particles.Properties["size"] == .03 && (double)particles.Properties["lifetime"] == 2 && (double)particles.Properties["speed"] == .2 && (double)particles.Properties["gravity"] == 0 && (double)particles.Properties["spread"] == .05 && (int)particles.Properties["blendMode"] == 1 && (double)particles.Properties["opacity"] == 1 && (double)particles.Properties["mask"] == 1, "surfaceParticles defaults");
        var graph = Graph();
        assert(GraphValidator.Validate(graph).IsValid, "surfaceParticles graph validates");
        var emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded && emitted.ShaderSource != null, "surfaceParticles graph emits");
        assert(emitted.ShaderSource.Contains("maxvertexcount(4)") && emitted.ShaderSource.Contains("SV_PrimitiveID") && emitted.ShaderSource.Contains("TriangleStream"), "surfaceParticles emits bounded geometry pass");
        assert(emitted.ShaderSource.Contains("ForwardBase") && emitted.ShaderSource.Contains("originalLocal"), "surfaceParticles preserves base pass and source position");
        Reject(assert, graph, "density", -.01, "density lower bound"); Reject(assert, graph, "size", .00009, "size lower bound"); Reject(assert, graph, "size", 10.01, "size upper bound"); Reject(assert, graph, "lifetime", .0009, "lifetime lower bound"); Reject(assert, graph, "lifetime", 1000.01, "lifetime upper bound"); Reject(assert, graph, "spread", -.01, "spread lower bound"); Reject(assert, graph, "mask", 1.01, "mask upper bound"); Reject(assert, graph, "opacity", -.01, "opacity lower bound"); Reject(assert, graph, "blendMode", 2, "blend mode choice"); Reject(assert, graph, "speed", new JValue(double.NaN), "speed finite");
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

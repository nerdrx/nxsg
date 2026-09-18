using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class ParticleChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var surface = NodeCatalog.Create("core.particleSurface");
        var color = NodeCatalog.Create("core.particleColor");
        assert(surface != null && color != null, "particle nodes are registered");
        assert(NodeCatalog.Ports("core.particleSurface", false).SequenceEqual(new[] { "albedo", "emission", "opacity" }), "particleSurface inputs");
        assert(NodeCatalog.Ports("core.particleSurface", true).SequenceEqual(new[] { "surface" }), "particleSurface output");
        assert(NodeCatalog.Ports("core.particleColor", false).Length == 0 &&
            NodeCatalog.Ports("core.particleColor", true).SequenceEqual(new[] { "color", "alpha" }), "particleColor ports");
        assert(NodeCatalog.PortType(surface, "albedo") == "color" && NodeCatalog.PortType(surface, "emission") == "color" &&
            NodeCatalog.PortType(surface, "opacity") == "float" && NodeCatalog.PortType(surface, "surface") == "surface" &&
            NodeCatalog.PortType(color, "color") == "color" && NodeCatalog.PortType(color, "alpha") == "float", "particle socket types");
        assert((double)surface.Properties["opacity"] == 1 && (int)surface.Properties["blendMode"] == 0 &&
            (double)surface.Properties["softDistance"] == 0, "particle defaults");

        var graph = Graph();
        assert(GraphValidator.Validate(graph).IsValid, "particle graph validates");
        var emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded && emitted.ShaderSource != null, "particle graph emits");
        assert(emitted.ShaderSource.Contains("Blend SrcAlpha OneMinusSrcAlpha") &&
            emitted.ShaderSource.Contains("ZWrite Off") && emitted.ShaderSource.Contains("Cull Off"), "particle alpha queue state emits");
        assert(!emitted.ShaderSource.Contains("Name \"ShadowCaster\""), "particle surface has no shadow caster");

        surface = graph.Nodes.Single(n => n.Id == "surface");
        surface.Properties["blendMode"] = 1;
        emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded && emitted.ShaderSource.Contains("Blend SrcAlpha One"), "particle additive queue state emits");
        assert(emitted.ShaderSource.Contains("\"VRCFallback\"=\"Particle\""), "particle fallback tag");
        assert(!emitted.ShaderSource.Contains("_CameraDepthTexture"), "disabled soft particles need no depth texture");
        surface.Properties["softDistance"] = .5;
        emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded && emitted.ShaderSource.Contains("_CameraDepthTexture"), "soft particles emit depth sampling");
        surface.Properties["softDistance"] = 0;

        Reject(assert, graph, surface, "opacity", -0.01, "particle opacity lower bound");
        Reject(assert, graph, surface, "opacity", 1.01, "particle opacity upper bound");
        Reject(assert, graph, surface, "blendMode", 2, "particle blend mode choice");
        Reject(assert, graph, surface, "softDistance", -0.01, "particle soft distance lower bound");
        var shell = NodeCatalog.Create("core.shell"); shell.Id = "shell";
        graph.Nodes.Add(shell);
        graph.Connections.RemoveAll(e => e.To.NodeId == "output");
        Connect(graph,"surface","surface","shell","base"); Connect(graph,"surface","surface","shell","layer"); Connect(graph,"shell","surface","output","surface");
        assert(!ShaderEmitter.Emit(graph).Succeeded, "particle surface rejects shell nesting");
    }

    static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "particle-checks" };
        graph.Nodes.Add(Color("albedo", new JArray(1, 0, 0, 1)));
        graph.Nodes.Add(Color("emission", new JArray(0, 0, 0, 1)));
        graph.Nodes.Add(Float("opacity", .5));
        graph.Nodes.Add(NodeCatalog.Create("core.particleColor")); graph.Nodes[^1].Id = "particleColor";
        graph.Nodes.Add(NodeCatalog.Create("core.particleSurface")); graph.Nodes[^1].Id = "surface";
        graph.Nodes.Add(NodeCatalog.Create("core.output")); graph.Nodes[^1].Id = "output";
        Connect(graph, "albedo", "value", "surface", "albedo"); Connect(graph, "emission", "value", "surface", "emission");
        Connect(graph, "particleColor", "alpha", "surface", "opacity"); Connect(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static GraphNode Color(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static void Reject(Action<bool, string> assert, ShaderGraph graph, GraphNode surface, string property, object value, string label)
    {
        var old = surface.Properties[property]; surface.Properties[property] = JToken.FromObject(value);
        assert(!GraphValidator.Validate(graph).IsValid, label); surface.Properties[property] = old;
    }
}

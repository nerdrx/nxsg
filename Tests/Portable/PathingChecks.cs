using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class PathingChecks
{
    public static void Run(Action<bool, string> assert, string fixtures)
    {
        var definitions = new Dictionary<string, FeatureNode>();
        PathingNodes.Register(definitions);
        var definition = definitions[PathingNodes.Operation];
        assert(definition.Outputs.Count == 4 && definition.Outputs["channels"] == "color" &&
            definition.Outputs["direction"] == "vector2", "Pathing exposes channels, value, phase and direction");

        var node = new GraphNode { Operation = PathingNodes.Operation,
            Properties = (JObject)definition.Defaults.DeepClone() };
        var errors = new List<string>();
        PathingNodes.Validate(node, (key, message) => errors.Add(key));
        assert(errors.Count == 0, "Pathing defaults validate");
        node.Properties["end"] = new JArray(.9);
        PathingNodes.Validate(node, (key, message) => errors.Add(key));
        assert(errors.Contains("end"), "Pathing rejects malformed endpoint");
        node.Properties["end"] = new JArray(.9, .5);
        node.Properties["tail"] = 0;
        PathingNodes.Validate(node, (key, message) => errors.Add(key));
        assert(errors.Contains("tail"), "Pathing rejects zero tail");
        assert(PathingShader.Expression("direction", "uv", "start", "end", "time", "audio", "mask", "width", "spacing", "speed", "tail", "travel")
            == "NXSG_PathDirection(start,end)", "Pathing direction follows endpoints");
        assert(PathingShader.Expression("value", "uv", "start", "end", "time", "audio", "mask", "width", "spacing", "speed", "tail", "travel")
            .StartsWith("NXSG_PathMask("), "Pathing union evaluates channels once");

        var samplePath = Path.GetFullPath(Path.Combine(fixtures, "../../Packages/dev.nerdrx.nxsg/Samples~/Pathing.nxsg"));
        var graph = GraphJson.Parse(File.ReadAllText(samplePath));
        assert(GraphValidator.Validate(graph).IsValid, "Pathing sample validates");
        var emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded, "Pathing sample emits: " + string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        assert(emitted.ShaderSource.Contains("NXSG_PathChannels"), "Pathing helper reaches shader source");
        graph.Nodes.First(n => n.Operation == PathingNodes.Operation).Properties["speed"] = 21;
        assert(!GraphValidator.Validate(graph).IsValid, "Pathing rejects excessive speed");
    }
}

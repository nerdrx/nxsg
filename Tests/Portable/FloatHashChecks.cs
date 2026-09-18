using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;

public static class FloatHashChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var graph = new ShaderGraph { GraphId = "float-hash" };
        graph.Nodes.Add(new GraphNode
        {
            Id = "ramp",
            Operation = "core.ramp",
            Properties = new JObject
            {
                ["points"] = new JArray(
                    new JArray(0f, 0f),
                    new JArray(.311698139f, .777249753f),
                    new JArray(1f, .512882233f)),
                ["smoothness"] = .311698139f,
                ["colors"] = new JArray(
                    new JArray(.1f, .2f, .3f, 1f),
                    new JArray(.8f, .7f, .6f, 1f))
            }
        });

        var roundTrip = GraphJson.Parse(GraphJson.Serialize(graph, true));
        assert(GraphJson.ComputeSemanticHash(graph) == GraphJson.ComputeSemanticHash(roundTrip),
            "float graph keeps semantic hash after indented round trip");

        roundTrip.Nodes[0].Properties["smoothness"] = .4f;
        assert(GraphJson.ComputeSemanticHash(graph) != GraphJson.ComputeSemanticHash(roundTrip),
            "changed float changes semantic hash");
    }
}

using System;
using System.IO;
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

        foreach (var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            graph.Nodes[0].Properties["nested"] = new JArray(new JArray(number));
            foreach (var indented in new[] { false, true })
            {
                var rejected = false;
                try { GraphJson.Serialize(graph, indented); }
                catch (InvalidDataException exception) { rejected = exception.Message.Contains("nested"); }
                assert(rejected, "serialization rejects nested nonfinite numbers without changing their type");
            }
            assert(GraphJson.ComputeSemanticHash(graph) == GraphJson.ComputeSemanticHash(graph) &&
                !GraphValidator.Validate(graph).IsValid,
                "invalid numeric drafts can still be tracked and diagnosed without saving");
        }
        graph.Nodes[0].Properties["nested"] = new JArray(double.MaxValue, double.MinValue);
        var extremes = GraphJson.Parse(GraphJson.Serialize(graph, true));
        assert((double)extremes.Nodes[0].Properties["nested"][0] == double.MaxValue &&
            (double)extremes.Nodes[0].Properties["nested"][1] == double.MinValue,
            "finite double extremes still round trip");
    }
}

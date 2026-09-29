using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class ConstellationChecks
{
    public static void Run(Action<bool, string> assert, string fixtures)
    {
        var nodes = new Dictionary<string, FeatureNode>();
        ConstellationNodes.Register(nodes);
        assert(nodes[ConstellationNodes.Operation].Outputs.Count == 3, "Constellation exposes point, line and combined masks");
        var node = new GraphNode { Operation = ConstellationNodes.Operation,
            Properties = (JObject)nodes[ConstellationNodes.Operation].Defaults.DeepClone() };
        var errors = new List<string>();
        ConstellationNodes.Validate(node, (key, message) => errors.Add(key));
        assert(errors.Count == 0, "Constellation defaults validate");
        node.Properties["scale"] = 0;
        ConstellationNodes.Validate(node, (key, message) => errors.Add(key));
        assert(errors.Contains("scale"), "Constellation rejects zero scale");
        assert(ConstellationShader.Expression("mask", "uv", "t", "a", "s", "p", "w", "c", "k", "seed").EndsWith(".z"),
            "Constellation combines point and line masks in one shader call");
        assert(ConstellationShader.Hlsl.Contains("NXSG_ConstLine"), "Constellation emits line distance helper");
        assert(!ConstellationShader.Hlsl.Contains("float point=") && !ConstellationShader.Hlsl.Contains("float line="),
            "Constellation avoids reserved D3D HLSL identifiers");

        var graph = new ShaderGraph { GraphId = "constellation-check" };
        var stars = NodeCatalog.Create(ConstellationNodes.Operation); stars.Id = "stars"; graph.Nodes.Add(stars);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "stars", "mask", "surface", "emission");
        Link(graph, "surface", "surface", "output", "surface");
        var emitted = ShaderEmitter.Emit(graph);
        assert(emitted.Succeeded, "Constellation graph compiles: " + string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        assert(emitted.ShaderSource.Contains("NXSG_Constellation"), "Constellation helper reaches shader source");
        stars.Properties["linkChance"] = 2;
        assert(!GraphValidator.Validate(graph).IsValid, "Constellation rejects invalid link chance");

        var samplePath = Path.GetFullPath(Path.Combine(fixtures, "../../Packages/dev.nerdrx.nxsg/Samples~/Constellation.nxsg"));
        var sample = GraphJson.Parse(File.ReadAllText(samplePath));
        assert(GraphValidator.Validate(sample).IsValid, "Constellation sample validates");
        assert(ShaderEmitter.Emit(sample).Succeeded, "Constellation sample emits");
    }

    static void Link(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + to + toPort,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}

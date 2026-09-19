using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class TessellationChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var node = NodeCatalog.Create("core.tessellation");
        assert(node != null && NodeCatalog.IsKnown("core.tessellation"), "tessellation catalog metadata");
        assert((int)node.Properties["factor"] == 8 && (int)node.Properties["minFactor"] == 1 &&
               (double)node.Properties["nearDistance"] == 2 && (double)node.Properties["farDistance"] == 15 &&
               (double)node.Properties["strength"] == .1 && (double)node.Properties["reference"] == .5 &&
               (double)node.Properties["smoothing"] == 0 && (double)node.Properties["height"] == .5,
               "tessellation defaults");

        var invalidFactor = Build();
        invalidFactor.Nodes.Single(n => n.Id == "tessellation").Properties["factor"] = 64;
        assert(!GraphValidator.Validate(invalidFactor).IsValid, "tessellation factor hardware bound");

        var invalidDistance = Build();
        invalidDistance.Nodes.Single(n => n.Id == "tessellation").Properties["farDistance"] = 2;
        assert(!GraphValidator.Validate(invalidDistance).IsValid, "tessellation far distance ordering");

        var saved = GraphJson.Parse(GraphJson.Serialize(Build()));
        assert(GraphValidator.Validate(saved).IsValid, "tessellation round trips and validates");
        var emitted = ShaderEmitter.Emit(saved);
        assert(emitted.Succeeded, "tessellation emits");
        assert(emitted.ShaderSource.Contains("#pragma hull hullTess") && emitted.ShaderSource.Contains("#pragma domain domainTess"), "tessellation selects GPU subdivision stages");
        var partial=Build(); var partialNode=partial.Nodes.Single(n=>n.Id=="tessellation");
        partialNode.Properties.Remove("farDistance"); partialNode.Properties["nearDistance"]=20;
        assert(!GraphValidator.Validate(partial).IsValid,"distance ordering includes default values");
        var unsafeHeight=Build(); var normal=NodeCatalog.Create("core.normalFromHeight"); normal.Id="normal"; unsafeHeight.Nodes.Add(normal);
        var preview=NodeCatalog.Create("core.previewVector"); preview.Id="preview"; unsafeHeight.Nodes.Add(preview);
        var split=NodeCatalog.Create("core.splitColor"); split.Id="split"; unsafeHeight.Nodes.Add(split);
        Connect(unsafeHeight,"normal","normal","preview","normal"); Connect(unsafeHeight,"preview","color","split","color"); Connect(unsafeHeight,"split","r","tessellation","height");
        assert(!ShaderEmitter.Emit(unsafeHeight).Succeeded,"fragment derivatives rejected in tessellation height");
    }

    private static ShaderGraph Build()
    {
        var graph = new ShaderGraph { GraphId = "tessellation-check" };
        var baseNode = NodeCatalog.Create("core.unlitSurface"); baseNode.Id = "base";
        var tessellation = NodeCatalog.Create("core.tessellation"); tessellation.Id = "tessellation";
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        graph.Nodes.Add(baseNode); graph.Nodes.Add(tessellation); graph.Nodes.Add(output);
        Connect(graph, "base", "surface", "tessellation", "base");
        Connect(graph, "tessellation", "surface", "output", "surface");
        return graph;
    }

    private static void Connect(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = from + "-" + to,
            From = new GraphPortRef { NodeId = from, PortId = port },
            To = new GraphPortRef { NodeId = to, PortId = input }
        });
    }
}

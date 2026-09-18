using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;

public static class EffectsCoreChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var operations = new[] { "core.unlitSurface", "core.pbrSurface", "core.fresnel", "core.colorRamp", "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion", "core.audioLink", "core.shell", "core.normalMap" };
        foreach (var operation in operations)
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation) && NodeCatalog.Description(operation).Length > 0 && NodeCatalog.Aliases(operation).Length > 0, operation + " catalog metadata");
        }

        var graph = new ShaderGraph { GraphId = "effects-core" };
        graph.Resources.Add(new GraphResource { Id = "sticker", Kind = "texture2D", Uri = "project://sticker.png" });
        foreach (var operation in operations)
        {
            var node = NodeCatalog.Create(operation);
            node.Id = operation.Substring(5).Replace(".", "-");
            if (operation == "core.sticker") node.Properties["resourceId"] = "sticker";
            graph.Nodes.Add(node);
        }
        var valid = GraphValidator.Validate(graph);
        assert(valid.IsValid, "effects core defaults validate");

        var ramp = graph.Nodes.Single(node => node.Operation == "core.colorRamp");
        ramp.Properties["stops"] = new JArray(new JArray(0, 0, 0, 0, 1), new JArray(2, 1, 1, 1, 1));
        assert(!GraphValidator.Validate(graph).IsValid, "color ramp bounds validate");

        ramp.Properties["stops"] = new JArray(new JArray(0, 0, 0, 0, 1), new JArray(1, 1, 1, 1, 1));
        var flipbook = graph.Nodes.Single(node => node.Operation == "core.flipbook");
        flipbook.Properties["rows"] = 65;
        assert(!GraphValidator.Validate(graph).IsValid, "flipbook row cap validates");
    }
}

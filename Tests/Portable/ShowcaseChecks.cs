using System;
using System.IO;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class ShowcaseChecks
{
    public static void Run(Action<bool, string> assert, string fixtures)
    {
        var package = Path.GetFullPath(Path.Combine(fixtures, "../../Packages/dev.nerdrx.nxsg/Samples~"));
        foreach (var name in new[] { "Showcase Hologram", "Showcase Pearl", "Showcase Warm Fur" })
        {
            var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(package, name + ".nxsg")));
            var validation = GraphValidator.Validate(graph);
            assert(validation.IsValid, name + " validates: " + string.Join(";", validation.Diagnostics.Select(d => d.Message)));
            var emission = ShaderEmitter.Emit(graph);
            assert(emission.Succeeded, name + " emits: " + string.Join(";", emission.Diagnostics.Select(d => d.Message)));
            assert(graph.Layout.Nodes.Count == graph.Nodes.Count, name + " lays out every node");
            assert(graph.Layout.Nodes.Values.Select(node => node.X + "," + node.Y).Distinct().Count() == graph.Nodes.Count, name + " avoids overlapping node layout");
        }
    }
}

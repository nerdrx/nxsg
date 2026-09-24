using System;
using System.IO;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class LayeredExampleChecks
{
    public static void Run(Action<bool, string> check, string fixtures)
    {
        var samples = Path.GetFullPath(Path.Combine(fixtures, "../../Packages/dev.nerdrx.nxsg/Samples~"));
        foreach (var name in new[] { "Lacquered Surface", "Velvet Fabric" })
        {
            var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(samples, name + ".nxsg")));
            var validation = GraphValidator.Validate(graph);
            check(validation.IsValid, name + " validates: " + string.Join(";", validation.Diagnostics.Select(d => d.Message)));
            var emission = ShaderEmitter.Emit(graph);
            check(emission.Succeeded, name + " emits: " + string.Join(";", emission.Diagnostics.Select(d => d.Message)));
        }
    }
}

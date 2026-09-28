using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class SoftOutlineChecks
{
    public static void Run(Action<bool, string> check)
    {
        var graph = RenderingOptionsChecks.Graph("core.unlitSurface");
        var outline = NodeCatalog.Create("core.softOutline"); outline.Id = "aura"; graph.Nodes.Add(outline);
        graph.Connections[0].To = new GraphPortRef { NodeId = outline.Id, PortId = "base" };
        RenderingOptionsChecks.Edge(graph, outline.Id, "surface", "output", "surface");
        var emitted = ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(graph)));
        check(emitted.Succeeded, "Soft Outline round trips and emits: " + string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        check(emitted.ShaderSource.Contains("[maxvertexcount(4)]") && emitted.ShaderSource.Contains("NX_SoftLerp") && emitted.ShaderSource.Contains("pow(saturate(1-fin.edge)") && emitted.ShaderSource.Contains("One OneMinusSrcAlpha"), "Soft Outline uses bounded silhouette fins, feathering and separate alpha blend");
        outline.Properties["widthMode"] = 1;
        check(ShaderEmitter.Emit(graph).ShaderSource.Contains("unity_StereoScaleOffset[unity_StereoEyeIndex].xy"), "Pixel fins use per-eye viewport");
        outline.Properties["widthMode"] = 4;
        check(!GraphValidator.Validate(graph).IsValid, "Invalid fin width mode rejected"); outline.Properties["widthMode"] = 0;
        outline.Properties["falloff"] = "wrong";
        check(!GraphValidator.Validate(graph).IsValid, "Malformed fin falloff rejected"); outline.Properties["falloff"] = 1;
        graph.Connections.Clear();
        RenderingOptionsChecks.Edge(graph, "surface", "surface", "output", "surface");
        var unused = ShaderEmitter.Emit(graph);
        check(unused.Succeeded && !unused.ShaderSource.Contains("geomSoftOutline"), "Disconnected soft outline emits no geometry");
    }
}

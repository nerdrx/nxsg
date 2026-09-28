using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class SurfaceRenderStateChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var refracted = RenderingOptionsChecks.Graph("core.pbrSurface");
        var refraction = NodeCatalog.Create("core.refraction"); refraction.Id = "refraction";
        refracted.Nodes.Add(refraction);
        RenderingOptionsChecks.Edge(refracted, "refraction", "color", "surface", "albedo");
        var automatic = ShaderEmitter.Emit(refracted);
        assert(automatic.Succeeded && automatic.ShaderSource.Contains("\"Queue\"=\"Transparent\"") &&
            automatic.ShaderSource.Contains("ZWrite Off\nBlend SrcAlpha OneMinusSrcAlpha"),
            "Automatic refraction uses transparent blending and disables default depth writes");

        refracted.Nodes.First(n => n.Operation == "core.output").Properties["renderMode"] = 1;
        var opaque = ShaderEmitter.Emit(refracted);
        assert(opaque.Succeeded && opaque.ShaderSource.Contains("\"Queue\"=\"Geometry\"") &&
            !opaque.ShaderSource.Contains("clip(alpha-") && opaque.ShaderSource.Contains("float alpha=1;"),
            "Explicit Opaque disables alpha clipping and forces opaque output alpha");

        var outlined = RenderingOptionsChecks.Graph("core.pbrSurface");
        var outline = NodeCatalog.Create("core.outline"); outline.Id = "outline"; outlined.Nodes.Add(outline);
        outlined.Connections[0].To = new GraphPortRef { NodeId = "outline", PortId = "base" };
        RenderingOptionsChecks.Edge(outlined, "outline", "surface", "output", "surface");
        outlined.Nodes.First(n => n.Operation == "core.output").Properties["zTest"] = 5;
        var outlineShader = ShaderEmitter.Emit(outlined);
        var outlineStart = outlineShader.ShaderSource.IndexOf("Name \"Outline\"", StringComparison.Ordinal);
        var outlineEnd = outlineShader.ShaderSource.IndexOf("ENDCG", outlineStart, StringComparison.Ordinal);
        assert(outlineShader.Succeeded && outlineStart >= 0 && outlineEnd > outlineStart &&
            outlineShader.ShaderSource.Substring(outlineStart, outlineEnd - outlineStart).Contains("ZTest Always"),
            "Outline pass inherits Output depth test");

        var malformedRamp = RenderingOptionsChecks.Graph("core.toonSurface");
        var toon = malformedRamp.Nodes.First(n => n.Id == "surface");
        toon.Properties["lightingMode"] = 2;
        toon.Properties["resourceId"] = new JArray(1, 2);
        var validation = GraphValidator.Validate(malformedRamp);
        assert(!validation.IsValid, "Malformed toon ramp resource ID returns validation diagnostics");
    }
}

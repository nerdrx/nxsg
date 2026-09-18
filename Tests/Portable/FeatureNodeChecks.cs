using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class FeatureNodeChecks
{
    static readonly string[] Features = { "core.fur", "core.parallaxUV", "core.parallaxOcclusion", "core.furMask", "core.flowMapUV", "core.ditherMask", "core.truchet", "core.weave", "core.scales", "core.dots", "core.scratches", "core.cracks", "core.woodRings", "core.marble", "core.clouds", "core.sparkleMask", "core.scanlines", "core.glitchUV", "core.pixelateUV", "core.kaleidoscopeUV", "core.swapUV", "core.spherizeUV", "core.pinchUV", "core.barrelUV", "core.chromaticTexture", "core.normalBlend", "core.normalStrength", "core.normalFromHeight", "core.reflectionDirection", "core.objectScale", "core.objectOrigin", "core.objectRandom", "core.distanceToPoint", "core.sphereMask", "core.boxVolumeMask", "core.capsuleMask", "core.stripes3D", "core.snowMask", "core.wetnessColor", "core.anisotropicHighlight" };
    public static void Run(Action<bool,string> assert)
    {
        foreach (var op in Features)
        {
            var node = NodeCatalog.Create(op); assert(node != null && NodeCatalog.IsKnown(op) && NodeCatalog.Ports(op,true).Length > 0 && !string.IsNullOrWhiteSpace(NodeCatalog.Description(op)), op + " catalog metadata");
            var graph = Build(op); var saved = GraphJson.Parse(GraphJson.Serialize(graph)); var result = ShaderEmitter.Emit(saved);
            assert(GraphValidator.Validate(saved).IsValid && result.Succeeded, op + " round trips and emits");
        }
    }
    static ShaderGraph Build(string op)
    {
        var graph = new ShaderGraph { GraphId = "feature-" + op }; var node = NodeCatalog.Create(op); node.Id = "effect"; graph.Nodes.Add(node);
        if (op == "core.fur") { var baseNode = NodeCatalog.Create("core.unlitSurface"); baseNode.Id = "base"; graph.Nodes.Add(baseNode); Edge(graph,"base","surface","effect","base"); }
        if (op == "core.parallaxOcclusion" || op == "core.chromaticTexture") { node.Properties["resourceId"] = "texture"; graph.Resources.Add(new GraphResource { Id="texture", Kind="texture2D", Uri="builtin://white" }); }
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        var port = NodeCatalog.Ports(op,true)[0]; var type = NodeCatalog.PortType(node,port);
        if (type == "surface") Edge(graph,"effect",port,"output","surface");
        else if (type == "vector2" || type == "vector3") { var preview = NodeCatalog.Create("core.previewVector"); preview.Id="preview"; graph.Nodes.Add(preview); Edge(graph,"effect",port,"preview",type == "vector2" ? "uv" : "normal"); var surface=NodeCatalog.Create("core.unlitSurface"); surface.Id="surface"; graph.Nodes.Add(surface); Edge(graph,"preview","color","surface","albedo"); Edge(graph,"surface","surface","output","surface"); }
        else { var surface = NodeCatalog.Create(type == "color" ? "core.unlitSurface" : "core.pbrSurface"); surface.Id="surface"; graph.Nodes.Add(surface); Edge(graph,"effect",port,"surface",type == "color" ? "albedo" : "opacity"); if(type != "color") { var color=NodeCatalog.Create("core.constant"); color.Id="albedo"; graph.Nodes.Add(color); Edge(graph,"albedo","value","surface","albedo"); } Edge(graph,"surface","surface","output","surface"); }
        graph.Nodes.Add(output); return graph;
    }
    static void Edge(ShaderGraph g,string from,string port,string to,string input) { g.Connections.Add(new GraphConnection { Id=from+"-"+to+"-"+input, From=new GraphPortRef { NodeId=from,PortId=port }, To=new GraphPortRef { NodeId=to,PortId=input } }); }
}

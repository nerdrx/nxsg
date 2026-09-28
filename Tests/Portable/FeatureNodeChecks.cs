using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class FeatureNodeChecks
{
    static readonly string[] Features = { "core.fur", "core.parallaxUV", "core.parallaxOcclusion", "core.furMask", "core.flowMapUV", "core.ditherMask", "core.truchet", "core.weave", "core.scales", "core.dots", "core.scratches", "core.cracks", "core.woodRings", "core.marble", "core.clouds", "core.sparkleMask", "core.scanlines", "core.glitchUV", "core.pixelateUV", "core.kaleidoscopeUV", "core.swapUV", "core.spherizeUV", "core.pinchUV", "core.barrelUV", "core.chromaticTexture", "core.normalBlend", "core.normalStrength", "core.normalFromHeight", "core.reflectionDirection", "core.objectScale", "core.objectOrigin", "core.objectRandom", "core.distanceToPoint", "core.sphereMask", "core.boxVolumeMask", "core.capsuleMask", "core.stripes3D", "core.snowMask", "core.wetnessColor", "core.anisotropicHighlight", "core.depthBulge" };
    public static void Run(Action<bool,string> assert)
    {
        CheckDepthBulge(assert);
        foreach (var op in Features)
        {
            var node = NodeCatalog.Create(op); assert(node != null && NodeCatalog.IsKnown(op) && NodeCatalog.Ports(op,true).Length > 0 && !string.IsNullOrWhiteSpace(NodeCatalog.Description(op)), op + " catalog metadata");
            var graph = Build(op); var saved = GraphJson.Parse(GraphJson.Serialize(graph)); var result = ShaderEmitter.Emit(saved);
            assert(GraphValidator.Validate(saved).IsValid && result.Succeeded, op + " round trips and emits");
        }
    }
    static void CheckDepthBulge(Action<bool,string> assert)
    {
        var node = NodeCatalog.Create("core.depthBulge");
        assert(node != null && NodeCatalog.Category("core.depthBulge") == "Surface" &&
            NodeCatalog.Ports("core.depthBulge", false).SequenceEqual(new[] { "height", "distance", "falloff", "bias", "mask" }) &&
            NodeCatalog.Ports("core.depthBulge", true).SequenceEqual(new[] { "displacement", "touch" }) &&
            NodeCatalog.PortType(node, "displacement") == "float" && NodeCatalog.PortType(node, "touch") == "float",
            "Depth Bulge socket contract");
        assert((double)node.Properties["height"] == -.03 && (double)node.Properties["distance"] == .1 &&
            (double)node.Properties["falloff"] == 1 && (double)node.Properties["bias"] == .002 && (double)node.Properties["mask"] == 1,
            "Depth Bulge defaults");
        assert(new[] { "touch", "dent", "press", "squish", "proximity" }.All(alias => NodeCatalog.Aliases("core.depthBulge").Contains(alias)),
            "Depth Bulge search aliases");
        var graph = new ShaderGraph { GraphId = "feature-core.depthBulge" };
        node.Id = "effect"; graph.Nodes.Add(node);
        var time = NodeCatalog.Create("core.time"); time.Id = "time"; graph.Nodes.Add(time);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Edge(graph, "time", "value", "effect", "height");
        Edge(graph, "effect", "displacement", "surface", "displacement");
        Edge(graph, "surface", "surface", "output", "surface");
        var saved = GraphJson.Parse(GraphJson.Serialize(graph));
        assert(GraphValidator.Validate(saved).IsValid && NodeCatalog.Ports("core.depthBulge", true).All(port => NodeCatalog.PortType(node, port) == "float"),
            "Depth Bulge graph validates and round-trips");
        var emitted = ShaderEmitter.Emit(saved);
        assert(emitted.Succeeded && emitted.ShaderSource.Contains("NX_DepthBulgeTouch") && emitted.ShaderSource.Contains("NXSG_Time()"),
            "Depth Bulge emits depth helper and connected dynamic height");
        assert(emitted.ShaderSource.Contains("UNITY_PASS_SHADOWCASTER") && emitted.ShaderSource.Contains("return 0;") &&
            emitted.ShaderSource.Contains("\"Queue\"=\"Geometry\"") && emitted.ShaderSource.Contains("\"RenderType\"=\"Opaque\"") &&
            !emitted.ShaderSource.Contains("Blend SrcAlpha") && !emitted.ShaderSource.Contains("Blend One OneMinusSrcAlpha"),
            "Depth Bulge keeps shadow fallback and opaque queue/blend behavior");
        CheckDepthBulgeCompatibility(assert);
    }
    static void CheckDepthBulgeCompatibility(Action<bool,string> assert)
    {
        var tessellation = New("depth-bulge-tessellation", "core.depthBulge", "effect");
        New(tessellation, "core.unlitSurface", "base");
        New(tessellation, "core.tessellation", "tess");
        New(tessellation, "core.output", "output");
        Edge(tessellation, "effect", "displacement", "base", "displacement");
        Edge(tessellation, "base", "surface", "tess", "base");
        Edge(tessellation, "tess", "surface", "output", "surface");
        assert(ShaderEmitter.Emit(tessellation).Succeeded, "Depth Bulge is supported through Tessellation Base");

        var particles = New("depth-bulge-particle-base", "core.depthBulge", "effect");
        New(particles, "core.unlitSurface", "base"); New(particles, "core.surfaceParticles", "particles"); New(particles, "core.output", "output");
        Edge(particles, "effect", "displacement", "base", "displacement");
        Edge(particles, "base", "surface", "particles", "base");
        Edge(particles, "particles", "surface", "output", "surface");
        assert(ShaderEmitter.Emit(particles).Succeeded, "Depth Bulge is supported on Surface Particles Base");

        var particleInputs = New("depth-bulge-particle-input", "core.depthBulge", "effect");
        New(particleInputs, "core.unlitSurface", "base"); New(particleInputs, "core.surfaceParticles", "particles"); New(particleInputs, "core.output", "output");
        Edge(particleInputs, "base", "surface", "particles", "base");
        Edge(particleInputs, "effect", "touch", "particles", "mask");
        Edge(particleInputs, "particles", "surface", "output", "surface");
        assert(!ShaderEmitter.Emit(particleInputs).Succeeded, "Depth Bulge is rejected in generated particle inputs");

        var particleSurface = New("depth-bulge-particle-surface", "core.depthBulge", "effect");
        New(particleSurface, "core.particleSurface", "particle");
        New(particleSurface, "core.output", "output");
        Edge(particleSurface, "effect", "touch", "particle", "opacity");
        Edge(particleSurface, "particle", "surface", "output", "surface");
        assert(!ShaderEmitter.Emit(particleSurface).Succeeded, "Depth Bulge is rejected on Particle Surface");

        var volumeGraph = New("depth-bulge-volume", "core.depthBulge", "effect");
        New(volumeGraph, "core.volumeSurface", "volume"); New(volumeGraph, "core.output", "output");
        Edge(volumeGraph, "effect", "touch", "volume", "density");
        Edge(volumeGraph, "volume", "surface", "output", "surface");
        assert(!ShaderEmitter.Emit(volumeGraph).Succeeded, "Depth Bulge is rejected on Volume Surface");

        var furGraph = New("depth-bulge-fur", "core.depthBulge", "effect");
        New(furGraph, "core.unlitSurface", "base"); New(furGraph, "core.fur", "fur"); New(furGraph, "core.output", "output");
        Edge(furGraph, "effect", "displacement", "base", "displacement");
        Edge(furGraph, "base", "surface", "fur", "base");
        Edge(furGraph, "fur", "surface", "output", "surface");
        assert(!ShaderEmitter.Emit(furGraph).Succeeded, "Depth Bulge is rejected on Fur");
    }
    static ShaderGraph New(string graphId, string operation, string id)
    {
        var graph = new ShaderGraph { GraphId = graphId };
        New(graph, operation, id);
        return graph;
    }
    static GraphNode New(ShaderGraph graph, string operation, string id)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node); return node;
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
        else { var surface = NodeCatalog.Create(type == "color" || op == "core.depthBulge" ? "core.unlitSurface" : "core.pbrSurface"); surface.Id="surface"; graph.Nodes.Add(surface); Edge(graph,"effect",port,"surface",type == "color" ? "albedo" : op == "core.depthBulge" ? "displacement" : "opacity"); if(type != "color" && op != "core.depthBulge") { var color=NodeCatalog.Create("core.constant"); color.Id="albedo"; graph.Nodes.Add(color); Edge(graph,"albedo","value","surface","albedo"); } Edge(graph,"surface","surface","output","surface"); }
        graph.Nodes.Add(output); return graph;
    }
    static void Edge(ShaderGraph g,string from,string port,string to,string input) { g.Connections.Add(new GraphConnection { Id=from+"-"+to+"-"+input, From=new GraphPortRef { NodeId=from,PortId=port }, To=new GraphPortRef { NodeId=to,PortId=input } }); }
}

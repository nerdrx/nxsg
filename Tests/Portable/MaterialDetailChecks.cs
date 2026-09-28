using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class MaterialDetailChecks
{
    public static void Run(Action<bool, string> assert)
    {
        foreach (var operation in new[] { "core.sdfFaceShadow", "core.depthRim", "core.gem" })
        {
            var node = NodeCatalog.Create(operation);
            assert(node != null && NodeCatalog.IsKnown(operation) && NodeCatalog.Ports(operation, true).Length == 1,
                operation + " registration");
            var graph = MakeGraph(operation);
            var saved = GraphJson.Parse(GraphJson.Serialize(graph));
            var emitted = ShaderEmitter.Emit(saved);
            assert(GraphValidator.Validate(saved).IsValid && emitted.Succeeded, operation + " validates, saves and emits");
        }

        var face = ShaderEmitter.Emit(MakeGraph("core.sdfFaceShadow"));
        assert(face.ShaderSource.Contains("smoothstep(") && face.ShaderSource.Contains("atan2(abs(") &&
            face.ShaderSource.Contains("step(0,dot(") && face.ShaderSource.Contains("unity_ObjectToWorld"),
            "SDF Face Shadow emits scalar mirrored selection in object basis");
        var customBasis = MakeGraph("core.sdfFaceShadow");
        customBasis.Nodes.Single(n => n.Operation == "core.sdfFaceShadow").Properties["basis"] = 1;
        var customSource = ShaderEmitter.Emit(customBasis).ShaderSource;
        assert(customSource.Contains("NX_DetailSafeNormal(float3(1,0,0))"),
            "SDF Face Shadow supports supplied head basis");
        assert(!ShaderEmitter.Emit(MakeGraph("core.sdfFaceShadow", displacement: true)).Succeeded,
            "SDF Face Shadow rejects vertex-stage use");

        var depth = ShaderEmitter.Emit(MakeGraph("core.depthRim"));
        assert(depth.ShaderSource.Contains("NX_DepthRim") && depth.ShaderSource.Contains("NX_ScreenDepthAvailable") &&
            depth.ShaderSource.Contains("unity_CameraProjection") && depth.ShaderSource.Contains("expectedDepth") &&
            depth.ShaderSource.Contains("scene-expectedDepth") && depth.Diagnostics.Any(d => d.Code == "depth.screenSpace"),
            "Depth Rim predicts tilted planes and documents missing depth and mirror limits");
        var zeroWidth = MakeGraph("core.depthRim"); zeroWidth.Nodes.Single(n => n.Operation == "core.depthRim").Properties["width"] = 0;
        assert(ShaderEmitter.Emit(zeroWidth).Succeeded && depth.ShaderSource.Contains("width<=0"), "Depth Rim width zero is neutral");
        var vertexDepth = MakeGraph("core.depthRim", displacement: true);
        assert(!ShaderEmitter.Emit(vertexDepth).Succeeded, "Depth Rim rejects vertex-stage use");

        var gem = ShaderEmitter.Emit(MakeGraph("core.gem"));
        assert(gem.ShaderSource.Contains("GrabPass { \"_NXSG_GrabTexture\" }") &&
            gem.ShaderSource.Contains("UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0") &&
            gem.ShaderSource.Contains("clamp(uv+offset") && gem.ShaderSource.Contains("float f0=ratio*ratio") &&
            gem.Diagnostics.Any(d => d.Code == "cost.refraction" && d.Message.Contains("first-probe reflection approximation")),
            "Gem emits screen refraction and probe approximation limits");
        var gemVolume = MakeGraph("core.gem", volume: true);
        assert(!ShaderEmitter.Emit(gemVolume).Succeeded, "Gem rejects ray-marched volume use");
        assert(!ShaderEmitter.Emit(MakeGraph("core.gem", displacement: true)).Succeeded,
            "Gem rejects vertex-stage use");

        var extendedWidth = MakeGraph("core.depthRim"); extendedWidth.Nodes.Single(n => n.Operation == "core.depthRim").Properties["width"] = 24;
        assert(ShaderEmitter.Emit(extendedWidth).Succeeded,"Typed width beyond the slider range remains supported");
        var invalidFace = NodeCatalog.Create("core.sdfFaceShadow"); invalidFace.Properties["basis"] = 2;
        var invalidDepth = NodeCatalog.Create("core.depthRim"); invalidDepth.Properties["width"] = "bad";
        var invalidGem = NodeCatalog.Create("core.gem"); invalidGem.Properties["ior"] = "bad";
        assert(!GraphValidator.Validate(NodeGraph(invalidFace)).IsValid &&
            !GraphValidator.Validate(NodeGraph(invalidDepth)).IsValid &&
            !GraphValidator.Validate(NodeGraph(invalidGem)).IsValid, "detail controls reject malformed values and invalid enums");
    }

    static ShaderGraph MakeGraph(string operation, bool displacement = false, bool volume = false)
    {
        var graph = new ShaderGraph { GraphId = "material-detail-" + operation };
        var effect = Add(graph, operation, "effect");
        if (operation == "core.sdfFaceShadow")
        {
            var left = Add(graph, "core.value", "left"); left.Properties["value"] = .2;
            var right = Add(graph, "core.value", "right"); right.Properties["value"] = .8;
            Edge(graph, left, "value", effect, "sdfLeft"); Edge(graph, right, "value", effect, "sdfRight");
        }
        if (volume)
        {
            var surface = Add(graph, "core.volumeSurface", "surface");
            var output = Add(graph, "core.output", "output");
            Edge(graph, effect, operation == "core.gem" ? "color" : "mask", surface, operation == "core.gem" ? "color" : "density");
            Edge(graph, surface, "surface", output, "surface");
            return graph;
        }
        var material = Add(graph, "core.pbrSurface", "material");
        var final = Add(graph, "core.output", "output");
        var from = operation == "core.gem" ? "color" : "mask";
        var to = displacement ? "displacement" : operation == "core.gem" ? "albedo" : "shadow";
        Edge(graph, effect, from, material, to);
        Edge(graph, material, "surface", final, "surface");
        return graph;
    }

    static ShaderGraph NodeGraph(GraphNode node)
    {
        var graph = new ShaderGraph { GraphId = "invalid-" + node.Operation };
        graph.Nodes.Add(node); graph.Nodes.Add(NodeCatalog.Create("core.output"));
        return graph;
    }

    static GraphNode Add(ShaderGraph graph, string operation, string id)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node); return node;
    }

    static void Edge(ShaderGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = from.Id + "-" + to.Id + "-" + toPort,
            From = new GraphPortRef { NodeId = from.Id, PortId = fromPort },
            To = new GraphPortRef { NodeId = to.Id, PortId = toPort }
        });
    }
}

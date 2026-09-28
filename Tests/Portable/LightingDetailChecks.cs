using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class LightingDetailChecks
{
    public static void Run(Action<bool,string> check)
    {
        var layeredNode = NodeCatalog.Create("core.layeredPbrSurface");
        check(NodeCatalog.PortType(layeredNode, "specularAa") == "float" && (double)layeredNode.Properties["specularAa"] == 0,
            "Layered PBR has neutral specular AA socket");
        var aniso = NodeCatalog.Create("core.anisotropicHighlight");
        check(aniso != null && NodeCatalog.PortType(aniso, "dualLobe") == "float" &&
            NodeCatalog.PortType(aniso, "secondaryTint") == "color" &&
            NodeCatalog.PortType(aniso, "secondaryShift") == "float" &&
            (double)aniso.Properties["dualLobe"] == 0 && (double)aniso.Properties["tangentStrength"] == 1,
            "Anisotropic defaults retain one original tangent lobe and expose independent secondary shift");
        var subsurface = NodeCatalog.Create("core.subsurface");
        check(NodeCatalog.PortType(subsurface, "strength") == "float" &&
            NodeCatalog.PortType(subsurface, "viewResponse") == "float" &&
            NodeCatalog.PortType(subsurface, "attenuation") == "float",
            "Subsurface exposes strength, view and attenuation inputs");

        var plain = LayeredSurfaceChecks.Graph("core.pbrSurface");
        var plainSource = Emit(plain, check, "Neutral PBR");
        check(!plainSource.Contains("ddx(n)") && !plainSource.Contains("specAaDx"),
            "Neutral PBR omits fragment derivative AA");

        var aa = LayeredSurfaceChecks.Graph("core.pbrSurface");
        LayeredSurfaceChecks.Surface(aa).Properties["specularAa"] = .75;
        var aaSource = Emit(aa, check, "PBR with specular AA");
        check(aaSource.Contains("ddx(n)") && aaSource.Contains("ddy(n)"), "Specular AA uses normal derivatives");
        check(!VertexCode(aaSource).Contains("ddx(") && !VertexCode(aaSource).Contains("ddy("),
            "Specular derivatives stay out of vertex functions");

        var wiredAa = LayeredSurfaceChecks.Graph("core.pbrSurface");
        LayeredSurfaceChecks.AddValue(wiredAa, "specularAa", 0);
        check(Emit(wiredAa, check, "Connected neutral specular AA").Contains("ddx(n)"),
            "Connected zero AA remains dynamic and emits derivatives");

        var neutralLayered = Emit(LayeredSurfaceChecks.Graph(), check, "Neutral layered PBR");
        check(Fragment(plainSource, "frag") == Fragment(neutralLayered, "frag"),
            "Neutral layered PBR preserves base PBR fragment exactly");
        check(!neutralLayered.Contains("ddx(n)"), "Neutral layered PBR prunes derivative code");

        var unused = LayeredSurfaceChecks.Graph("core.pbrSurface");
        unused.Nodes.Add(NodeCatalog.Create("core.anisotropicHighlight"));
        unused.Nodes[unused.Nodes.Count - 1].Id = "unused-anisotropic";
        check(!Emit(unused, check, "Unused anisotropic node").Contains("NX_Aniso("),
            "Disconnected anisotropic feature prunes helper and expression");

        var anisoGraph = FeatureGraph("core.anisotropicHighlight");
        Value(anisoGraph, "dualLobe", .8);
        var anisoSource = Emit(anisoGraph, check, "Dual anisotropic lobe");
        check(anisoSource.Contains("NX_Aniso(") && anisoSource.Contains(".8") && anisoSource.Contains("secondaryT="),
            "Anisotropic second lobe input reaches emitted shader");
        var tintedAniso = FeatureGraph("core.anisotropicHighlight");
        tintedAniso.Nodes.Single(node => node.Id == "feature").Properties["secondaryTint"] = new JArray(1, .1, .05, 1);
        check(Emit(tintedAniso, check, "Tinted anisotropic lobe").Contains("float4(1,0.1,0.05,1)"),
            "Anisotropic tint property reaches shader fallback");

        var defaultScatter = Emit(FeatureGraph("core.subsurface"), check, "Neutral subsurface");
        check(defaultScatter.Contains("wrap*.65+back*.35") && !defaultScatter.Contains("float attenuation=exp("),
            "Neutral subsurface keeps legacy view-independent code");
        var scatter = FeatureGraph("core.subsurface");
        Value(scatter, "strength", 1.4);
        Value(scatter, "viewResponse", 1);
        Value(scatter, "attenuation", 1);
        var scatterSource = Emit(scatter, check, "View-dependent attenuated subsurface");
        check(scatterSource.Contains("saturate(1-dot(n,v))") && scatterSource.Contains("exp(-max(0,") && scatterSource.Contains("1.4"),
            "Subsurface options and connected strength reach emitted shader");
        scatter.Nodes.Single(node => node.Id == "feature").Properties["tint"] = new JArray(.2, .3, .4, 1);
        check(Emit(scatter, check, "Tinted subsurface").Contains("float4(0.2,0.3,0.4,1)"),
            "Subsurface tint property reaches shader fallback");
    }

    static ShaderGraph FeatureGraph(string operation)
    {
        var graph = LayeredSurfaceChecks.Graph("core.pbrSurface");
        graph.Connections.RemoveAll(edge => edge.To.NodeId == "surface" && edge.To.PortId == "albedo");
        graph.Nodes.RemoveAll(node => node.Id == "input-albedo");
        var feature = NodeCatalog.Create(operation); feature.Id = "feature"; graph.Nodes.Add(feature);
        Edge(graph, "feature", "color", "surface", "albedo");
        return graph;
    }

    static void Value(ShaderGraph graph, string port, double value)
    {
        var source = NodeCatalog.Create("core.value"); source.Id = "input-" + port; source.Properties["value"] = value; graph.Nodes.Add(source);
        Edge(graph, source.Id, "value", "feature", port);
    }

    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static string Emit(ShaderGraph graph, Action<bool,string> check, string label)
    {
        var result = ShaderEmitter.Emit(graph);
        check(result.Succeeded, label + " emits: " + string.Join(";", result.Diagnostics.Select(d => d.Message)));
        return result.ShaderSource ?? "";
    }

    static string Fragment(string source, string function)
    {
        var start = source.IndexOf("float4 " + function + "(", StringComparison.Ordinal);
        if (start < 0) return "missing " + function;
        var stop = source.IndexOf("ENDCG", start, StringComparison.Ordinal);
        return source.Substring(start, stop - start);
    }

    static string VertexCode(string source)
    {
        var result = "";
        var at = 0;
        while ((at = source.IndexOf("NXInput vert", at, StringComparison.Ordinal)) >= 0)
        {
            var stop = source.IndexOf("float4 frag", at, StringComparison.Ordinal);
            if (stop < 0) break;
            result += source.Substring(at, stop - at);
            at = stop;
        }
        return result;
    }
}

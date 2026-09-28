using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class IndirectLightingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var baseline = Graph();
        var plain = Emit(baseline, check);
        var neutral = Graph();
        var neutralSurface = Surface(neutral);
        neutralSurface.Properties["bentStrength"] = 1;
        neutralSurface.Properties["lightDirection"] = new JArray(1, 2, 3);
        neutralSurface.Properties["lightDirectionStrength"] = 0;
        neutralSurface.Properties["lightDirectionSpace"] = 1;
        var explicitDefaults = Emit(neutral, check);
        check(plain == explicitDefaults, "Disconnected bent normal and neutral light override are pruned");

        var bent = Graph();
        bent.Nodes.Add(Vector("bent", .25, .75, .6));
        Link(bent, "bent", "value", "surface", "bentNormal");
        Surface(bent).Properties["bentStrength"] = .8;
        var bentShader = Emit(bent, check);
        check(bentShader.Contains("NX_BentNormal(input,n,") && bentShader.Contains("0.25,0.75,0.6"), "Connected tangent-space bent normal is decoded through mesh tangent basis");
        check(bentShader.Contains("normalize(input.tangent)*tangentBent.x") && bentShader.Contains("NX_BentVisibility("), "Bent normal affects ambient direction and rough reflection visibility");

        foreach (var ao in new[] { 0.0, 1.0 })
        foreach (var roughness in new[] { 0.0, 1.0 })
        {
            var endpoint = Graph(); endpoint.Nodes.Add(Vector("bent", 0, 1, 0));
            Link(endpoint, "bent", "value", "surface", "bentNormal");
            Surface(endpoint).Properties["bentStrength"] = 1;
            Surface(endpoint).Properties["occlusion"] = ao;
            Surface(endpoint).Properties["roughness"] = roughness;
            var shader = Emit(endpoint, check);
            check(shader.Contains("NX_BentVisibility("), "AO / roughness endpoint retains bent specular visibility");
            check(!shader.Contains("NaN") && !shader.Contains("Infinity"), "AO / roughness endpoint emits finite HLSL tokens");
        }
        var invalid = Graph(); Surface(invalid).Properties["occlusion"] = double.NaN;
        check(!ShaderEmitter.Emit(invalid).Succeeded, "Non-finite AO control is rejected");
        invalid = Graph(); Surface(invalid).Properties["roughness"] = double.PositiveInfinity;
        check(!ShaderEmitter.Emit(invalid).Succeeded, "Non-finite roughness control is rejected");

        var overrideGraph = Graph(); var overrideSurface = Surface(overrideGraph);
        overrideSurface.Properties["lightDirection"] = new JArray(0, 0, -1);
        overrideSurface.Properties["lightDirectionStrength"] = 1;
        var overrideResult = ShaderEmitter.Emit(overrideGraph);
        check(overrideResult.Succeeded, "Direct-light direction override emits");
        var overrideShader = overrideResult.ShaderSource ?? "";
        var attenuation = overrideShader.IndexOf("UNITY_LIGHT_ATTENUATION(atten,input,input.ws);", StringComparison.Ordinal);
        var overrideCall = overrideShader.IndexOf("NX_OverrideLightDirection(lightDir", StringComparison.Ordinal);
        check(attenuation >= 0 && overrideCall > attenuation, "Direction override preserves Unity distance, cookie and shadow attenuation");

        overrideSurface.Properties["lightDirectionSpace"] = 0;
        var world = ShaderEmitter.Emit(overrideGraph).ShaderSource ?? "";
        overrideSurface.Properties["lightDirectionSpace"] = 1;
        var local = ShaderEmitter.Emit(overrideGraph).ShaderSource ?? "";
        check(world.Contains("NX_OverrideLightDirection(lightDir,float3(0,0,-1),1)") &&
            local.Contains("NX_OverrideLightDirection(lightDir,UnityObjectToWorldDir(float3(0,0,-1)),1)"),
            "World direction stays fixed while object direction is transformed into world space");

        var emissionBase = Graph(); Surface(emissionBase).Properties["metallic"] = 1;
        Surface(emissionBase).Properties["roughness"] = .8;
        emissionBase.Nodes.Add(Color("emission", .2, .4, .8, 1));
        Link(emissionBase, "emission", "value", "surface", "emission");
        var emissionOverride = GraphJson.Parse(GraphJson.Serialize(emissionBase));
        Surface(emissionOverride).Properties["lightDirection"] = new JArray(0, 0, -1);
        Surface(emissionOverride).Properties["lightDirectionStrength"] = 1;
        check(ReturnLine(Emit(emissionBase, check)) == ReturnLine(Emit(emissionOverride, check)), "Light direction control does not alter the PBR emission return");
    }

    static string Emit(ShaderGraph graph, Action<bool, string> check)
    {
        var result = ShaderEmitter.Emit(graph);
        check(result.Succeeded, "Indirect lighting graph emits");
        return result.ShaderSource ?? "";
    }
    static string ReturnLine(string shader)
    {
        var start = shader.IndexOf("float4 frag(", StringComparison.Ordinal);
        var end = shader.IndexOf("ENDCG", start, StringComparison.Ordinal);
        if (start < 0 || end < 0) return "";
        var fragment = shader.Substring(start, end - start);
        var result = fragment.Split('\n').LastOrDefault(line => line.Contains("return float4("));
        return result?.Trim() ?? "";
    }
    static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "indirect-lighting-checks" };
        var surface = NodeCatalog.Create("core.pbrSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "surface", "surface", "output", "surface");
        return graph;
    }
    static GraphNode Surface(ShaderGraph graph) => graph.Nodes.Single(node => node.Id == "surface");
    static GraphNode Vector(string id, double x, double y, double z) => new GraphNode
    {
        Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(x, y, z) }
    };
    static GraphNode Color(string id, double r, double g, double b, double a) => new GraphNode
    {
        Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(r, g, b, a) }
    };
    static void Link(ShaderGraph graph, string from, string output, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
}

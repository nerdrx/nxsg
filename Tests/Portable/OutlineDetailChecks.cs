using System;
using NXSG.Backend;
using NXSG.Core;

public static class OutlineDetailChecks
{
    public static void Run(Action<bool, string> check)
    {
        var graph = RenderingOptionsChecks.Graph("core.pbrSurface");
        var outline = NodeCatalog.Create("core.outline"); outline.Id = "outline"; graph.Nodes.Add(outline);
        graph.Connections[0].To = new GraphPortRef { NodeId = "outline", PortId = "base" };
        RenderingOptionsChecks.Edge(graph, "outline", "surface", "output", "surface");

        var world = ShaderEmitter.Emit(graph);
        var worldPass = Pass(world.ShaderSource);
        check(world.Succeeded && worldPass.Contains("world+=outlineDirection*max(0,0.003)*saturate(1);") &&
            worldPass.Contains("if(clipPos.z>=UNITY_NEAR_CLIP_VALUE*clipPos.w) { clipPos.z+=bias*(1-UNITY_NEAR_CLIP_VALUE)*clipPos.w") &&
            worldPass.Contains("if(clipPos.z<=clipPos.w) { clipPos.z-=bias*clipPos.w") && !worldPass.Contains("#pragma multi_compile_fwdbase"),
            "Default outline keeps world width and omits lighting variants");

        outline.Properties["widthMode"] = 1;
        outline.Properties["pixelWidth"] = 8.5;
        outline.Properties["mask"] = .25;
        var direction = NodeCatalog.Create("core.normalDirection"); direction.Id = "direction"; graph.Nodes.Add(direction);
        RenderingOptionsChecks.Edge(graph, "direction", "normal", "outline", "direction");
        var pixels = ShaderEmitter.Emit(graph);
        var pixelPass = Pass(pixels.ShaderSource);
        check(pixels.Succeeded && pixelPass.Contains("UNITY_MATRIX_VP") && pixelPass.Contains("_ScreenParams.xy") &&
            pixelPass.Contains("unity_StereoScaleOffset[unity_StereoEyeIndex].xy") &&
            pixelPass.Contains("defined(UNITY_SINGLE_PASS_STEREO) && !defined(UNITY_STEREO_INSTANCING_ENABLED) && !defined(UNITY_STEREO_MULTIVIEW_ENABLED)") &&
            pixelPass.Contains("max(0,8.5)*saturate(0.25)") && pixelPass.Contains("customLength>0.000001"),
            "Pixel outline uses projected world direction, per-eye viewport and safe direction fallback");

        outline.Properties["lighting"] = 1;
        outline.Properties["emission"] = new Newtonsoft.Json.Linq.JArray(1, .25, 0, 1);
        var lit = ShaderEmitter.Emit(graph);
        var litPass = Pass(lit.ShaderSource);
        check(lit.Succeeded && litPass.Contains("\"LightMode\"=\"ForwardBase\"") &&
            litPass.Contains("#pragma multi_compile_fwdbase") && litPass.Contains("UNITY_LIGHT_ATTENUATION") &&
            litPass.Contains("ShadeSH9") && litPass.Contains("color.rgb=lerp(color.rgb,lit,saturate(1))") &&
            litPass.Contains("float3 emission=(float4(1,0.25,0,1)).rgb") && !litPass.Contains("ForwardAdd"),
            "Optional outline lighting uses ForwardBase, keeps emission independent and adds no pass");

        outline.Properties["lighting"] = 0;
        var dynamicLighting = NodeCatalog.Create("core.cameraDistance"); dynamicLighting.Id = "lightAmount"; graph.Nodes.Add(dynamicLighting);
        RenderingOptionsChecks.Edge(graph, "lightAmount", "value", "outline", "lighting");
        var connected = ShaderEmitter.Emit(graph);
        check(connected.Succeeded && Pass(connected.ShaderSource).Contains("#pragma multi_compile_fwdbase"),
            "Connected lighting input retains runtime lighting variants at default zero");
    }

    private static string Pass(string shader)
    {
        var start = shader.IndexOf("Name \"Outline\"", StringComparison.Ordinal);
        var end = shader.IndexOf("ENDCG", start, StringComparison.Ordinal);
        return start >= 0 && end > start ? shader.Substring(start, end - start) : string.Empty;
    }
}

using System;
using NXSG.Backend;
using NXSG.Core;

public static class SpatialChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var deform = SpatialShader.TransformFunction("NX_Deform", 1, 2);
        assert(deform.Contains("unity_ObjectToWorld") && deform.Contains("unity_WorldToObject") &&
            deform.Contains("cross(jy,jz)") && deform.Contains("fullT") && deform.Contains("Linear(float3(1,0,0)") &&
            deform.Contains("warp>1e-6 || snap>1e-6") && deform.Contains("snap>1e-6") && deform.Contains("nearDistance>0"),
            "deformation emits world conversion, inverse-transpose normals, tangents and snapping");
        var neutral = SpatialShader.TransformCall("NX_Deform", "v.vertex.xyz", "v.normal", "v.tangent",
            "float3(0,0,0)", "float3(0,0,0)", "float3(1,1,1)", "float3(0,0,0)", "1", "0", "0");
        assert(neutral.Contains("float3(1,1,1)") && neutral.Contains("v.tangent"), "neutral transform call preserves identity inputs");
        var near = NodeCatalog.Create("core.vertexDeform");
        assert((double)near.Properties["nearDistance"] == 0 && (double)near.Properties["nearStrength"] == 1 && NodeCatalog.PortType(near,"nearDistance") == "float", "near camera deformation defaults off with scalar inputs");
        var nearCall = SpatialShader.TransformCall("NX_Deform", "p", "n", "t", "tr", "rot", "scale", "pivot", "mask", "snap", "warp", "1", ".5");
        assert(nearCall.EndsWith(",1,.5)") && deform.Contains("worldPos+=forward*max(0,nearDistance-cameraDepth)"), "near camera call generates bounded camera-depth push");
        var layered = SpatialShader.InfinityParallaxBody("_Interior", "input.uv", "input.view", "0.7", ".4", ".08",
            "float4(.8,.9,1,1)", ".6", "1", 12, 0);
        assert(layered.Contains("nxI<12") && layered.Contains("nxLayer") && layered.Contains("nxTint") && layered.Contains("nxViewLengthSq") &&
            layered.Contains("nxSample.a") && layered.Contains("return float4(lerp(nxFront.rgb") && layered.Contains("lerp(nxFront.a,nxInside.a,nxMask)"),
            "bounded composite parallax includes depth, tint, fade and alpha masking");
        var additive = SpatialShader.InfinityParallaxBody("_Interior", "uv", "view", ".5", ".3", ".05", "tint", ".7", "mask", 4, 1);
        var maximum = SpatialShader.InfinityParallaxBody("_Interior", "uv", "view", ".5", ".3", ".05", "tint", ".7", "mask", 4, 2);
        assert(additive.Contains("nxAccum.rgb+=") && maximum.Contains("nxAccum=max("), "fixed blend choices generate additive and maximum layer rules");
        AssertInvalid(assert, () => SpatialShader.TransformFunction("NX_Deform", 2, 0), "invalid deformation enum rejected");
        AssertInvalid(assert, () => SpatialShader.InfinityParallaxBody("_Interior", "uv", "view", ".5", ".3", ".05", "tint", ".7", "mask", 33), "iteration cap rejected");
        AssertInvalid(assert, () => SpatialShader.InfinityParallaxBody("_Interior", "uv", "view", ".5", ".3", ".05", "tint", ".7", "mask", 4, 3), "invalid blend enum rejected");
    }

    static void AssertInvalid(Action<bool, string> assert, Action action, string name)
    {
        try { action(); assert(false, name); }
        catch (ArgumentException) { assert(true, name); }
    }
}

using System;
using NXSG.Core;

namespace NXSG.Backend
{
    internal static class DetailShader
    {
        internal static string FaceShadow(bool vertex,
            Func<string, string, string, string> input, Func<string, double, string> scalar,
            int basis, string rightFallback, string forwardFallback)
        {
            if (vertex) throw new InvalidOperationException("SDF Face Shadow is a fragment lighting mask; connect it to a surface color or shadow input.");
            var transform = "(float3x3)unity_ObjectToWorld";
            var right = basis == 0 ? "NX_DetailSafeNormal(mul(" + transform + ",float3(1,0,0)))"
                : "NX_DetailSafeNormal(" + input("headRight", rightFallback, "vector3") + ")";
            var forward = basis == 0 ? "NX_DetailSafeNormal(mul(" + transform + ",float3(0,0,1)))"
                : "NX_DetailSafeNormal(" + input("headForward", forwardFallback, "vector3") + ")";
            var light = "NX_DetailSafeNormal(UnityWorldSpaceLightDir(input.ws))";
            var side = "dot(" + light + "," + right + ")";
            var angular = "saturate(atan2(abs(" + side + "),dot(" + light + "," + forward + "))*.31830989)";
            var selected = "lerp(" + scalar("sdfLeft", .5) + "," + scalar("sdfRight", .5) + ",step(0," + side + "))";
            var strength = scalar("strength", 1);
            var threshold = "saturate(" + scalar("threshold", .5) + "+" + scalar("offset", 0) + "+" + scalar("angleStrength", .3) + "*" + angular + ")";
            var softness = "max(" + scalar("softness", .04) + ",.0001)";
            return "(1-saturate(" + strength + "-" + strength + "*smoothstep(" + threshold + "-" + softness + "," + threshold + "+" + softness + "," + selected + ")))";
        }

        internal static string DepthRim(bool vertex, Func<string, double, string> scalar)
        {
            if (vertex) throw new InvalidOperationException("Depth Rim needs fragment camera depth; connect it to a surface color or opacity input.");
            return "NX_DepthRim(input," + scalar("width", 2) + "," + scalar("softness", .02) + "," + scalar("bias", .01) + ")*saturate(" + scalar("strength", 1) + ")";
        }

        internal static string Gem(bool vertex, Func<string, string, string, string> input,
            Func<string, double, string> scalar, string colorFallback)
        {
            if (vertex) throw new InvalidOperationException("Gem refraction and probe reflection are fragment-only; connect Gem to a surface color input.");
            var color = input("color", colorFallback, "color");
            var normal = "NX_DetailSafeNormal(" + input("normal", "input.n", "vector3") + ")";
            return "NX_Gem(input," + color + "," + normal + "," + scalar("ior", 1.5) + "," + scalar("refraction", .06) + "," + scalar("reflection", .8) + "," + scalar("dispersion", .015) + "," + scalar("roughness", .15) + ")";
        }

        internal const string CommonHelpers = @"
float3 NX_DetailSafeNormal(float3 value){float lengthSquared=dot(value,value);return lengthSquared>0.000001?value*rsqrt(lengthSquared):float3(1,0,0);}
";

        internal const string DepthRimHelpers = @"
float NX_DepthRim(NXInput input,float width,float softness,float bias)
{
#if defined(UNITY_PASS_SHADOWCASTER)
    return 0;
#else
    if(width<=0||!NX_ScreenDepthAvailable())return 0;
    float center=-mul(UNITY_MATRIX_V,float4(input.ws,1)).z;
    if(center<=_ProjectionParams.y||center>=_ProjectionParams.z)return 0;
    float4 clipPosition=UnityWorldToClipPos(input.ws);
    if(clipPosition.w<=0.00001)return 0;
    float4 screenPosition=ComputeScreenPos(clipPosition);
    float2 uv=screenPosition.xy/max(screenPosition.w,0.00001);
    float2 delta=_CameraDepthTexture_TexelSize.xy*max(width,0);
    float2 offsets[4]={float2(delta.x,0),float2(-delta.x,0),float2(0,delta.y),float2(0,-delta.y)};
    float2 uvDx=ddx(uv),uvDy=ddy(uv);
    float determinant=uvDx.x*uvDy.y-uvDx.y*uvDy.x;
    if(abs(determinant)<0.00000001)return 0;
    float depthDx=ddx(center),depthDy=ddy(center);
    float rim=0;
    [unroll]for(int i=0;i<4;i++)
    {
        float2 sampleUv=uv+offsets[i];
        if(!NX_ScreenUvInEye(sampleUv))continue;
        float scene;
        if(!NX_SampleScreenDepth(sampleUv,scene)){rim=1;continue;}
        float2 pixelOffset=float2(offsets[i].x*uvDy.y-offsets[i].y*uvDy.x,uvDx.x*offsets[i].y-uvDx.y*offsets[i].x)/determinant;
        float expectedDepth=center+depthDx*pixelOffset.x+depthDy*pixelOffset.y;
        float fartherGap=scene-expectedDepth;
        rim=max(rim,smoothstep(max(0,bias),max(0,bias)+max(softness,.001),fartherGap));
    }
    return saturate(rim);
#endif
}
";

        internal const string GemHelpers = @"
float4 NX_Gem(NXInput input,float4 tint,float3 worldNormal,float ior,float refraction,float reflection,float dispersion,float roughness)
{
    float3 worldView=NX_DetailSafeNormal(_WorldSpaceCameraPos-input.ws);
    float3 normalView=NX_DetailSafeNormal(mul((float3x3)UNITY_MATRIX_V,worldNormal));
    float3 viewView=NX_DetailSafeNormal(mul((float3x3)UNITY_MATRIX_V,worldView));
    float3 incident=-viewView;
    float3 refracted=refract(incident,normalView,1/max(ior,1));
    float2 oldRay=incident.xy/max(abs(incident.z),0.00001);
    float2 newRay=refracted.xy/max(abs(refracted.z),0.00001);
    float2 uv=input.screenPos.xy/max(input.screenPos.w,0.00001);
    float2 halfTexel=_NXSG_GrabTexture_TexelSize.xy*.5;
    float2 eyeMin=halfTexel,eyeMax=1-halfTexel;
#if defined(UNITY_SINGLE_PASS_STEREO)
    float4 eye=unity_StereoScaleOffset[unity_StereoEyeIndex];
    eyeMin=eye.zw+halfTexel;
    eyeMax=eye.zw+eye.xy-halfTexel;
#endif
    float2 offset=(newRay-oldRay)*max(0,refraction);
    float fringe=saturate(dispersion);
    float3 scene=float3(tex2D(_NXSG_GrabTexture,clamp(uv+offset*(1+fringe),eyeMin,eyeMax)).r,
        tex2D(_NXSG_GrabTexture,clamp(uv+offset,eyeMin,eyeMax)).g,
        tex2D(_NXSG_GrabTexture,clamp(uv+offset*(1-fringe),eyeMin,eyeMax)).b);
    float3 reflected=reflect(-worldView,worldNormal);
    float4 probeData=UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflected,saturate(roughness)*6);
    float3 probe=DecodeHDR(probeData,unity_SpecCube0_HDR);
    float ratio=(max(ior,1)-1)/(max(ior,1)+1);
    float f0=ratio*ratio;
    float fresnel=f0+(1-f0)*pow(1-saturate(dot(worldNormal,worldView)),5);
    float3 result=lerp(scene,probe,saturate(reflection)*fresnel)*tint.rgb;
    return float4(result,tint.a);
}
";
    }
}

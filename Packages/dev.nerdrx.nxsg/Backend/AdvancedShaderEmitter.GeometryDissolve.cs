using System.Text;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        GraphNode geometryDissolve;
        bool geometryDissolveActive;

        string BreakupGeometry(bool shadow)
        {
            var b = new StringBuilder(@"
float3 NX_BreakSafeNormal(float3 n) { return n*rsqrt(max(dot(n,n),1e-12)); }
float3 NX_BreakRotate(float3 v,float3 axis,float angle) { float s,c; sincos(angle,s,c); return v*c+cross(axis,v)*s+axis*dot(axis,v)*(1-c); }
");
            var type = shadow ? "NXShadow" : "NXInput";
            b.AppendLine("[maxvertexcount(3)] void " + (shadow ? "geomBreakupShadow" : "geomWire") + "(triangle " + type + " tri[3], inout TriangleStream<" + type + "> stream, uint primitive:SV_PrimitiveID) {");
            b.AppendLine("UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(tri[0]); float3 center=(tri[0].ws+tri[1].ws+tri[2].ws)/3.0; NXInput input=(NXInput)0; input.ws=center; input.pos=UnityWorldToClipPos(center); input.screenPos=ComputeGrabScreenPos(input.pos);");
            var fields = new[] { "local", "uv", "uv1", "uv2", "uv3", "color", "originalWs", "originalLocal", "tangent", "bitangent", "sourceUV" };
            foreach (var field in fields)
                b.AppendLine("input." + field + "=(tri[0]." + field + "+tri[1]." + field + "+tri[2]." + field + ")/3.0;");
            var normalField = shadow ? "normal" : "n";
            b.AppendLine("input.n=NX_BreakSafeNormal((tri[0]." + normalField + "+tri[1]." + normalField + "+tri[2]." + normalField + ")/3.0); input.tangent=NX_BreakSafeNormal(input.tangent); input.bitangent=NX_BreakSafeNormal(input.bitangent);");
            b.AppendLine("float progress=saturate(" + Scalar(geometryDissolve, "amount", 0, true) + ")*saturate(" + Scalar(geometryDissolve, "mask", 1, true) + ");");
            b.AppendLine("float3 direction=" + Input(geometryDissolve,"direction","input.n","vector3",true) + "; direction*=rsqrt(max(dot(direction,direction),1e-12)); float3 shift=direction*" + Scalar(geometryDissolve,"distance",.3,true) + "*progress;");
            b.AppendLine("float seed=frac(sin((primitive+1)*12.9898)*43758.5453); float3 axis=frac(float3(seed,seed+.371,seed+.719)*13.13)*2-1; axis*=rsqrt(max(dot(axis,axis),1e-12)); float angle=" + Scalar(geometryDissolve,"rotation",1,true) + "*6.2831853*progress*(seed*2-1); float scale=max(0,1-progress*saturate(" + Scalar(geometryDissolve,"shrink",1,true) + ")); if(progress>=.999999) scale=0;");
            b.AppendLine("[unroll] for(int k=0;k<3;k++) { " + type + " o=tri[k]; o.ws=center+NX_BreakRotate((o.ws-center)*scale,axis,angle)+shift; o.local=mul(unity_WorldToObject,float4(o.ws,1)).xyz;");
            if (shadow)
                b.AppendLine(@"o.wireBary=k==0?float3(1,0,0):k==1?float3(0,1,0):float3(0,0,1); o.normal=NX_BreakRotate(o.normal,axis,angle);
float3 localNormal=mul(o.normal,(float3x3)unity_ObjectToWorld);
o.pos=UnityApplyLinearShadowBias(UnityClipSpaceShadowCasterPos(float4(o.local,1),localNormal));
#if defined(SHADOWS_CUBE) && !defined(SHADOWS_CUBE_IN_DEPTH_TEX)
o.vec=o.ws-_LightPositionRange.xyz;
#endif");
            else
                b.AppendLine("o.n=NX_BreakRotate(o.n,axis,angle); o.tangent=NX_BreakRotate(o.tangent,axis,angle); o.bitangent=NX_BreakRotate(o.bitangent,axis,angle); o.pos=UnityWorldToClipPos(o.ws); o.screenPos=ComputeGrabScreenPos(o.pos); o.wireBary=k==0?float3(1,0,0):k==1?float3(0,1,0):float3(0,0,1); TRANSFER_SHADOW_WPOS(o,o.ws);");
            b.AppendLine("stream.Append(o); } stream.RestartStrip(); }");
            return b.ToString();
        }
    }
}

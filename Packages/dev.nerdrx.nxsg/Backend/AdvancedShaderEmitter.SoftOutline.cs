using System.Text;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        string SoftOutlinePass(GraphNode outline, SurfacePass basePass, GraphNode tessellation)
        {
            var surface = basePass.Surface;
            var pixel = IntProp(outline, "widthMode", 0, 0, 1) == 1;
            var width = Scalar(outline, pixel ? "pixelWidth" : "width", pixel ? 12 : .04, true);
            var mask = Scalar(outline, "mask", 1, true);
            var color = Input(outline, "color", ColorProp(outline, "color", .47, .05, 1, 1), "color");
            var opacity = Scalar(outline, "opacity", 1);
            var falloff = Scalar(outline, "falloff", 1);
            var alpha = IntProp(surface, "useAlbedoAlpha", 1, 0, 1) == 1 ? "(" + Input(surface, "albedo", "float4(1,1,1,1)", "color") + ").a*_Color.a" : "_Color.a";
            var displacement = Scalar(surface, "displacement", 0, true) + "+" + basePass.Offset;
            if (tessellation != null) displacement += "+(" + Scalar(tessellation, "height", .5, true) + "-" + Prop(tessellation, "reference", .5) + ")*" + Prop(tessellation, "strength", .1);
            var b = new StringBuilder("Pass {\nName \"SoftOutline\"\nTags { \"LightMode\"=\"Always\" }\nCull Off\nZWrite Off\nZTest ")
                .Append(renderState.DepthTest).Append("\nBlend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha\nCGPROGRAM\n#pragma target ")
                .Append(tessellation == null ? "4.5" : "4.6").Append("\n#pragma vertex ")
                .Append(tessellation == null ? "vertSoftOutline" : "vertTessSoftOutline")
                .AppendLine("\n#pragma geometry geomSoftOutline\n#pragma fragment fragSoftOutline\n#pragma multi_compile_instancing");
            if (tessellation != null) b.AppendLine("#pragma hull hullTessSoftOutline\n#pragma domain domainTessSoftOutline");
            b.AppendLine("struct NXSoftInput { NXInput data; float edge:TEXCOORD18; };");
            b.AppendLine("NXInput vertSoftOutline(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*(" + displacement + "); NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); return o; }");
            b.AppendLine(@"NXInput NX_SoftLerp(NXInput a,NXInput b,float t) {
NXInput o=a; o.pos=lerp(a.pos,b.pos,t); o.ws=lerp(a.ws,b.ws,t); o.n=lerp(a.n,b.n,t); o.local=lerp(a.local,b.local,t);
o.originalWs=lerp(a.originalWs,b.originalWs,t); o.originalLocal=lerp(a.originalLocal,b.originalLocal,t);
o.uv=lerp(a.uv,b.uv,t); o.uv1=lerp(a.uv1,b.uv1,t); o.uv2=lerp(a.uv2,b.uv2,t); o.uv3=lerp(a.uv3,b.uv3,t);
o.sourceUV=lerp(a.sourceUV,b.sourceUV,t); o.wireBary=lerp(a.wireBary,b.wireBary,t); o.color=lerp(a.color,b.color,t); o.tangent=lerp(a.tangent,b.tangent,t); o.bitangent=lerp(a.bitangent,b.bitangent,t); return o;
}");
            b.AppendLine("NXSoftInput NX_SoftVertex(NXInput input,float edge) { NXSoftInput o; o.data=input; o.edge=edge; float3 n=input.n*rsqrt(max(dot(input.n,input.n),1e-12)); float width=max(0," + width + ")*saturate(" + mask + ")*edge;");
            if (pixel)
                b.AppendLine(@"float4 direction=mul(UNITY_MATRIX_VP,float4(n,0)); float4 p=input.pos; float2 viewport=_ScreenParams.xy;
#if defined(UNITY_SINGLE_PASS_STEREO) && !defined(UNITY_STEREO_INSTANCING_ENABLED) && !defined(UNITY_STEREO_MULTIVIEW_ENABLED)
viewport*=unity_StereoScaleOffset[unity_StereoEyeIndex].xy;
#endif
float2 d=(direction.xy-p.xy*(direction.w/max(p.w,.00001)))*viewport; float q=dot(d,d); if(q>1e-12 && p.w>0) o.data.pos.xy+=d*rsqrt(q)*2*width/max(viewport,float2(1,1))*p.w;");
            else b.AppendLine("o.data.ws=input.ws+n*width; o.data.local=mul(unity_WorldToObject,float4(o.data.ws,1)).xyz; o.data.pos=UnityWorldToClipPos(o.data.ws);");
            b.AppendLine("o.data.screenPos=ComputeGrabScreenPos(o.data.pos); return o; }");
            b.AppendLine(@"[maxvertexcount(4)] void geomSoftOutline(triangle NXInput tri[3],inout TriangleStream<NXSoftInput> stream) {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(tri[0]);
tri[0].wireBary=float3(1,0,0); tri[1].wireBary=float3(0,1,0); tri[2].wireBary=float3(0,0,1);
float d[3]; [unroll] for(int j=0;j<3;j++) { float3 view=unity_OrthoParams.w>.5?-UNITY_MATRIX_V[2].xyz:(_WorldSpaceCameraPos-tri[j].ws); d[j]=dot(tri[j].n,view); }
NXInput endA=tri[0]; NXInput endB=tri[1]; int count=0;
[unroll] for(int k=0;k<3;k++) { int next=(k+1)%3; if((d[k]>0)!=(d[next]>0)) { float t=saturate(d[k]/(d[k]-d[next])); if(count==0) endA=NX_SoftLerp(tri[k],tri[next],t); else endB=NX_SoftLerp(tri[k],tri[next],t); count++; } }
if(count!=2 || endA.pos.w<=.00001 || endB.pos.w<=.00001) return;
stream.Append(NX_SoftVertex(endA,0)); stream.Append(NX_SoftVertex(endB,0)); stream.Append(NX_SoftVertex(endA,1)); stream.Append(NX_SoftVertex(endB,1)); stream.RestartStrip(); }");
            if (tessellation != null)
                b.AppendLine(TessellationShader.Forward(Prop(tessellation, "factor", 8), Prop(tessellation, "minFactor", 1), Prop(tessellation, "nearDistance", 2), Prop(tessellation, "farDistance", 15), Prop(tessellation, "smoothing", 0))
                    .Replace("vertTess", "vertTessSoftOutline").Replace("hullTess", "hullTessSoftOutline").Replace("domainTess", "domainTessSoftOutline").Replace("return vert(a)", "return vertSoftOutline(a)"));
            b.AppendLine("float4 fragSoftOutline(NXSoftInput fin):SV_Target { NXInput input=fin.data; UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float sourceAlpha=" + (renderState.ForceOpaque ? "1" : "saturate(" + alpha + "*" + Scalar(surface, "opacity", 1) + ")") + ";");
            if (!renderState.ForceOpaque) b.AppendLine("clip(sourceAlpha-" + Prop(surface, "cutoff", .001) + ");");
            b.AppendLine("float4 c=" + color + "; float edge=pow(saturate(1-fin.edge),max(.01," + falloff + ")); c.a=saturate(c.a*" + opacity + "*sourceAlpha*edge); return c; }\nENDCG\n}");
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.softOutline", outline.Id, "Soft Outline adds a PC geometry pass with one feathered fin per smooth-normal silhouette triangle. Split/hard normals and coarse meshes can leave gaps; expand renderer bounds. Generated fur/particles are not outlined. Stereo and VRChat require client checks."));
            return b.ToString();
        }
    }
}

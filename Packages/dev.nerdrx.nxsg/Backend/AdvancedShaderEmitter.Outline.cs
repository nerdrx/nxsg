using System.Text;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        string OutlinePass(GraphNode outline, SurfacePass basePass, GraphNode tessellation)
        {
            var surface = basePass.Surface;
            var pixelMode = IntProp(outline, "widthMode", 0, 0, 1) == 1;
            var width = pixelMode ? null : Scalar(outline, "width", .003, true);
            var pixelWidth = pixelMode ? Scalar(outline, "pixelWidth", 2, true) : null;
            var mask = Scalar(outline, "mask", 1, true);
            var directionStrength = Scalar(outline, "directionStrength", 1, true);
            var direction = Input(outline, "direction", "input.n", "vector3", true);
            var depthBias = Scalar(outline, "depthBias", 0, true);
            var lightingToken = outline.Properties["lighting"];
            var lighting = edges.ContainsKey(Key(outline.Id, "lighting")) ||
                (lightingToken != null && (lightingToken.Type != JTokenType.Integer && lightingToken.Type != JTokenType.Float || (double)lightingToken != 0));
            var lightingStrength = Scalar(outline, "lighting", 0);
            var color = Input(outline, "color", ColorProp(outline, "color", 0, 0, 0, 1), "color");
            var emission = Input(outline, "emission", ColorProp(outline, "emission", 0, 0, 0, 1), "color");
            var opacity = Scalar(surface, "opacity", 1);
            var alpha = renderState.ForceOpaque ? "1" : IntProp(surface, "useAlbedoAlpha", 1, 0, 1) == 1
                ? "(" + Input(surface, "albedo", "float4(1,1,1,1)", "color") + ").a*_Color.a" : "_Color.a";
            var displacement = Scalar(surface, "displacement", 0, true) + "+" + basePass.Offset;
            if (tessellation != null)
                displacement += "+(" + Scalar(tessellation, "height", .5, true) + "-" + Prop(tessellation, "reference", .5) + ")*" + Prop(tessellation, "strength", .1);

            var b = new StringBuilder("Pass {\nName \"Outline\"\nTags { \"LightMode\"=\"")
                .Append(lighting ? "ForwardBase" : "Always")
                .Append("\" }\nCull Front\nZWrite Off\nZTest ").Append(renderState.DepthTest)
                .Append("\nBlend SrcAlpha OneMinusSrcAlpha\nCGPROGRAM\n#pragma target ")
                .Append(tessellation == null ? "3.5" : "4.6")
                .Append("\n#pragma vertex ").Append(tessellation == null ? "vertOutline" : "vertTessOutline")
                .Append("\n#pragma fragment fragOutline\n");
            if (lighting) b.AppendLine("#pragma multi_compile_fwdbase");
            b.AppendLine("#pragma multi_compile_instancing");
            if (tessellation != null) b.AppendLine("#pragma hull hullTessOutline\n#pragma domain domainTessOutline");

            b.AppendLine("NXInput vertOutline(NXApp v) {");
            b.AppendLine("    UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v);");
            b.AppendLine("    v.vertex.xyz+=v.normal*(" + displacement + ");");
            b.AppendLine("    float3 geometric=input.n; float geometricLength=dot(geometric,geometric);");
            b.AppendLine("    geometric=geometricLength>0.000001?geometric*rsqrt(geometricLength):float3(0,0,1);");
            b.AppendLine("    float3 customDirection=" + direction + "; float customLength=dot(customDirection,customDirection);");
            b.AppendLine("    customDirection=customLength>0.000001?customDirection*rsqrt(customLength):geometric;");
            b.AppendLine("    float3 outlineDirection=lerp(geometric,customDirection,saturate(" + directionStrength + ")); float directionLength=dot(outlineDirection,outlineDirection);");
            b.AppendLine("    outlineDirection=directionLength>0.000001?outlineDirection*rsqrt(directionLength):geometric;");
            b.AppendLine("    float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;");
            if (!pixelMode)
            {
                // Preserve legacy world width and mask behavior when mode remains at its default.
                b.AppendLine("    world+=outlineDirection*max(0," + width + ")*saturate(" + mask + ");");
                b.AppendLine("    v.vertex=mul(unity_WorldToObject,float4(world,1));");
            }
            b.AppendLine("    NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs;");
            b.AppendLine("    float4 clipPos=o.pos; float bias=clamp(" + depthBias + ",-1,1); if(clipPos.w>0.00001) {");
            if (pixelMode)
            {
                b.AppendLine("        float4 directionClip=mul(UNITY_MATRIX_VP,float4(outlineDirection,0)); float2 viewportPixels=_ScreenParams.xy;");
                b.AppendLine("#if defined(UNITY_SINGLE_PASS_STEREO) && !defined(UNITY_STEREO_INSTANCING_ENABLED) && !defined(UNITY_STEREO_MULTIVIEW_ENABLED)");
                b.AppendLine("        viewportPixels*=unity_StereoScaleOffset[unity_StereoEyeIndex].xy;");
                b.AppendLine("#endif");
                b.AppendLine("        float invW=rcp(clipPos.w); float2 pixelDirection=(directionClip.xy-clipPos.xy*(directionClip.w*invW))*viewportPixels;");
                b.AppendLine("        float pixelLength=dot(pixelDirection,pixelDirection); if(pixelLength>0.000001) clipPos.xy+=(pixelDirection*rsqrt(pixelLength))*((2*max(0," + pixelWidth + ")*saturate(" + mask + "))/max(viewportPixels,float2(1,1)))*clipPos.w;");
            }
            b.AppendLine("#if defined(UNITY_REVERSED_Z)");
            b.AppendLine("        if(clipPos.z<=clipPos.w) { clipPos.z-=bias*clipPos.w; clipPos.z=min(clipPos.z,clipPos.w); }");
            b.AppendLine("#else");
            b.AppendLine("        if(clipPos.z>=UNITY_NEAR_CLIP_VALUE*clipPos.w) { clipPos.z+=bias*(1-UNITY_NEAR_CLIP_VALUE)*clipPos.w; clipPos.z=max(clipPos.z,UNITY_NEAR_CLIP_VALUE*clipPos.w); }");
            b.AppendLine("#endif");
            b.AppendLine("    } o.pos=clipPos; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_VERTEX_TO_FRAGMENT(o); return o;");
            b.AppendLine("}");
            if (tessellation != null)
                b.AppendLine(TessellationShader.Forward(Prop(tessellation, "factor", 8), Prop(tessellation, "minFactor", 1), Prop(tessellation, "nearDistance", 2), Prop(tessellation, "farDistance", 15), Prop(tessellation, "smoothing", 0))
                    .Replace("vertTess", "vertTessOutline").Replace("hullTess", "hullTessOutline").Replace("domainTess", "domainTessOutline").Replace("return vert(a)", "return vertOutline(a)"));

            b.Append("float4 fragOutline(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float alpha=")
                .Append(renderState.ForceOpaque ? "1" : "saturate(" + alpha + "*" + opacity + ")").Append("; ");
            if (!renderState.ForceOpaque) b.Append("clip(alpha-").Append(Prop(surface, "cutoff", .001)).Append("); ");
            b.Append("float4 color=").Append(color).Append("; float3 emission=(").Append(emission).Append(").rgb; ");
            if (lighting)
                b.Append("float3 n=normalize(input.n); UNITY_LIGHT_ATTENUATION(atten,input,input.ws); float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws)); float3 lit=color.rgb*(max(0,ShadeSH9(float4(n,1)))+_LightColor0.rgb*atten*saturate(dot(n,lightDir))); color.rgb=lerp(color.rgb,lit,saturate(")
                    .Append(lightingStrength).Append(")); ");
            b.AppendLine("return float4(color.rgb+emission,color.a*alpha); }\nENDCG\n}");
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "cost.outline", outline.Id, "Outline adds one expanded hull pass. Hard normals can separate the hull; expand renderer bounds for its world-space width. It does not outline generated fur or particle geometry."));
            return b.ToString();
        }
    }
}

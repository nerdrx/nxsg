using System.Text;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        const string LightVolumeGraphHelper = @"
float4 NX_LightVolumeGraph(NXInput input,float4 albedo,float3 normal,float roughness,float metallic,float strength,int outputMode) {
#if defined(UNITY_PASS_FORWARDADD) || defined(UNITY_PASS_SHADOWCASTER)
return float4(0,0,0,1);
#else
float3 diffuse,specular;
NX_LightVolumes(input.ws,normal,normalize(_WorldSpaceCameraPos-input.ws),albedo.rgb,roughness,metallic,diffuse,specular);
return float4((outputMode==1?diffuse:outputMode==2?specular:diffuse+specular)*max(0,strength),1);
#endif
}";
        string ToonLighting(GraphNode surface, int passIndex, bool additional)
        {
            var b = new StringBuilder();
            var mode = IntProp(surface,"lightingMode",0,0,2);
            var strength = ToonSetting(surface,"shadowStrength",passIndex);
            var threshold = ToonSetting(surface,"threshold",passIndex);
            var softness = ToonSetting(surface,"softness",passIndex);
            b.AppendLine("float lightCoordinate=saturate(dot(n,lightDir)*.5+.5+"+Scalar(surface,"shadeMap",.5)+"-.5);");
            if (mode == 2)
                b.AppendLine("float3 toonResponse=lerp(float3(1,1,1),tex2D("+textureNames[(string)surface.Properties["resourceId"]]+",float2(lightCoordinate,saturate("+Prop(surface,"rampRow",.5)+"))).rgb,saturate("+strength+"));");
            else
            {
                if (mode == 1)
                    b.AppendLine("float bands="+IntProp(surface,"bands",3,2,8)+"-1; float scaled=lightCoordinate*bands; float lit=saturate((floor(scaled)+smoothstep(.5-max(.001,"+softness+"),.5+max(.001,"+softness+"),frac(scaled)))/bands);");
                else b.AppendLine("float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),lightCoordinate);");
                b.AppendLine("float3 toonResponse=lerp(lerp(float3(1,1,1),("+Input(surface,"shadeColor",ColorProp(surface,"shadeColor",0,0,0,1),"color")+").rgb,saturate("+strength+")),float3(1,1,1),lit);");
            }
            b.AppendLine("float3 direct=_LightColor0.rgb*atten*toonResponse;");
            if (HasLightingControls(surface)) b.AppendLine("direct=NX_LightingContribution(direct,"+Prop(surface,"lightingSaturation",1)+","+Prop(surface,"lightingMax",0)+");");
            if (additional) b.AppendLine("return float4(c.rgb*direct,alpha);");
            else
            {
                b.AppendLine("float3 ambient=max(0,ShadeSH9(float4(n,1)))*saturate("+Scalar(surface,"occlusion",1)+");");
                if (HasLightingControls(surface))
                    b.AppendLine("ambient=NX_LightingContribution(ambient,"+Prop(surface,"lightingSaturation",1)+","+Prop(surface,"lightingMax",0)+"); return float4(c.rgb*NX_LightingBase(ambient+direct,"+Prop(surface,"lightingMin",0)+","+Prop(surface,"lightingMax",0)+")+emission,alpha);");
                else b.AppendLine("return float4(c.rgb*(ambient+direct)+emission,alpha);");
            }
            return b.ToString();
        }

        string OutlinePass(GraphNode outline, SurfacePass basePass, GraphNode tessellation)
        {
            var surface = basePass.Surface;
            var width = Scalar(outline,"width",.003,true);
            var mask = Scalar(outline,"mask",1,true);
            var color = Input(outline,"color",ColorProp(outline,"color",0,0,0,1),"color");
            var opacity = Scalar(surface,"opacity",1);
            var alpha = renderState.ForceOpaque ? "1" : IntProp(surface,"useAlbedoAlpha",1,0,1)==1 ? "("+Input(surface,"albedo","float4(1,1,1,1)","color")+").a*_Color.a" : "_Color.a";
            var displacement = Scalar(surface,"displacement",0,true)+"+"+basePass.Offset;
            if (tessellation != null) displacement += "+("+Scalar(tessellation,"height",.5,true)+"-"+Prop(tessellation,"reference",.5)+")*"+Prop(tessellation,"strength",.1);
            var b = new StringBuilder("Pass {\nName \"Outline\"\nTags { \"LightMode\"=\"Always\" }\nCull Front\nZWrite Off\nZTest "+renderState.DepthTest+"\nBlend SrcAlpha OneMinusSrcAlpha\nCGPROGRAM\n#pragma target "+(tessellation==null?"3.5":"4.6")+"\n#pragma vertex "+(tessellation==null?"vertOutline":"vertTessOutline")+"\n#pragma fragment fragOutline\n#pragma multi_compile_instancing\n");
            if (tessellation != null) b.AppendLine("#pragma hull hullTessOutline\n#pragma domain domainTessOutline");
            b.AppendLine("NXInput vertOutline(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput input=NX_Make(v); v.vertex.xyz+=v.normal*("+displacement+"); float3 world=mul(unity_ObjectToWorld,v.vertex).xyz+normalize(UnityObjectToWorldNormal(v.normal))*max(0,"+width+")*saturate("+mask+"); v.vertex=mul(unity_WorldToObject,float4(world,1)); NXInput o=NX_Make(v); o.originalLocal=input.originalLocal; o.originalWs=input.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_VERTEX_TO_FRAGMENT(o); return o; }");
            if (tessellation != null) b.AppendLine(TessellationShader.Forward(Prop(tessellation,"factor",8),Prop(tessellation,"minFactor",1),Prop(tessellation,"nearDistance",2),Prop(tessellation,"farDistance",15),Prop(tessellation,"smoothing",0)).Replace("vertTess","vertTessOutline").Replace("hullTess","hullTessOutline").Replace("domainTess","domainTessOutline").Replace("return vert(a)","return vertOutline(a)"));
            b.AppendLine("float4 fragOutline(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float alpha="+(renderState.ForceOpaque ? "1" : "saturate("+alpha+"*"+opacity+")")+"; "+(renderState.ForceOpaque ? "" : "clip(alpha-"+Prop(surface,"cutoff",.001)+"); ")+"float4 color="+color+"; return float4(color.rgb,color.a*alpha); }\nENDCG\n}");
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,"cost.outline",outline.Id,"Outline adds one expanded hull pass. Hard normals can separate the hull; expand renderer bounds for its world-space width. It does not outline generated fur or particle geometry."));
            return b.ToString();
        }
    }
}

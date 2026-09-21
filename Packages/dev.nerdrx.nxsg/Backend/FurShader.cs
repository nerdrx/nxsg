using System;
using System.Globalization;
using System.Text;

namespace NXSG.Backend
{
    internal static class FurShader
    {
        static string Num(double value) { return AdvancedShaderEmitter.NumExpr(value); }

        public static string Pass(string rootColor, string tipColor, string length, string density, string thickness, string mask, string groom, string time, int layers, double taper, double gravity, double windStrength, double windSpeed, double windScale, double rimStrength, double lodNear, double lodFar, int minLayers, bool receiveShadows = true)
        {
            minLayers = Math.Min(layers, minLayers);
            var b = new StringBuilder();
            for (var layer = 0; layer < layers; layer++)
            {
                var h = Num((layer + .5) / Math.Max(1, layers));
                b.Append("Pass {\nName \"Fur").Append(layer + 1).Append("\"\nTags { \"LightMode\"=\"ForwardBase\" }\nCull Back\nZWrite Off\nBlend SrcAlpha OneMinusSrcAlpha\nCGPROGRAM\n#pragma target 3.5\n#pragma vertex vertFur").Append(layer + 1).Append("\n#pragma fragment fragFur").Append(layer + 1).Append("\n#pragma multi_compile_instancing\n#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight\n");
                b.Append("float FurHeight").Append(layer + 1).Append(" = ").Append(h).Append(";\n");
                b.Append("NXInput vertFur").Append(layer + 1).Append("(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput baseInput=NX_Make(v); NXInput input=baseInput; float furDistance=distance(_WorldSpaceCameraPos,baseInput.ws); float activeLayers=lerp(").Append(Num(layers)).Append(",").Append(Num(minLayers)).Append(",saturate((furDistance-").Append(Num(lodNear)).Append(")/max(.0001,").Append(Num(lodFar - lodNear)).Append("))); float h=saturate((").Append(Num(layer+.5)).Append(")/max(1,floor(activeLayers))); float3 n=normalize(v.normal); float3 groomOffset=").Append(groom).Append("; float3 wind=float3(sin(").Append(time).Append("*" ).Append(Num(windSpeed)).Append("+v.vertex.x*" ).Append(Num(windScale)).Append("+h*6.2831853),0,cos(").Append(time).Append("*" ).Append(Num(windSpeed)).Append("+v.vertex.z*" ).Append(Num(windScale)).Append("+h*6.2831853))*" ).Append(Num(windStrength)).Append("*h; v.vertex.xyz+=n*(" ).Append(length).Append("*h)+groomOffset*(" ).Append(length).Append("*h*h)+wind*" ).Append(length).Append("*h+float3(0,-" ).Append(Num(gravity)).Append("*" ).Append(length).Append("*h*h,0); NXInput o=NX_Make(v); o.originalLocal=baseInput.originalLocal; o.originalWs=baseInput.originalWs; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); TRANSFER_VERTEX_TO_FRAGMENT(o); return o; }\n");
                b.Append("float4 fragFur").Append(layer + 1).Append("(NXInput input):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); float furDistance=distance(_WorldSpaceCameraPos,input.ws); float activeLayers=lerp(").Append(Num(layers)).Append(",").Append(Num(minLayers)).Append(",saturate((furDistance-").Append(Num(lodNear)).Append(")/max(.0001,").Append(Num(lodFar - lodNear)).Append("))); float h=saturate((").Append(Num(layer+.5)).Append(")/max(1,floor(activeLayers))); clip(floor(activeLayers)-").Append(Num(layer+.5)).Append("); float cells=max(1,abs(" ).Append(density).Append(")); float2 cell=floor(input.uv*cells); float2 local=frac(input.uv*cells)-.5; float2 center=float2(NX_Hash(cell+float2(3.1,7.2)),NX_Hash(cell+float2(11.4,2.8)))-.5; float strand=1-smoothstep(max(.0001," ).Append(thickness).Append("*.5*pow(saturate(1-h),max(.001," ).Append(Num(taper)).Append("))),max(.0001," ).Append(thickness).Append("*.5*pow(saturate(1-h),max(.001," ).Append(Num(taper)).Append(")))+fwidth(length(local-center)),length(local-center)); float particleMask=saturate(" ).Append(mask).Append("); clip(strand*particleMask-.001); float4 root=(" ).Append(rootColor).Append("); float4 tip=(" ).Append(tipColor).Append("); float4 c=lerp(root,tip,h); float3 n=normalize(input.n); float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws)); float3 gu,gv; NX_FurUvGradients(ddx(input.originalWs),ddy(input.originalWs),ddx(input.uv),ddy(input.uv),n,gu,gv); float selfShadow=NX_FurSelfShadow(input,h,cells," ).Append(thickness).Append(",").Append(Num(taper)).Append(",").Append(length).Append(",particleMask,lightDir,gu,gv); UNITY_LIGHT_ATTENUATION(sceneShadow,input,input.ws); float3 lit=max(0,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,lightDir))*selfShadow*").Append(receiveShadows?"sceneShadow":"1").Append("; float rim=pow(saturate(1-dot(n,normalize(_WorldSpaceCameraPos-input.ws))),2)*" ).Append(Num(rimStrength)).Append("; return float4(c.rgb*(lit+rim),c.a*particleMask*strand); }\nENDCG\n}\n");
            }
            return b.ToString();
        }
    }
}

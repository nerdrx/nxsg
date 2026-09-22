using System.Globalization;

namespace NXSG.Backend
{
    internal static class VolumeShader
    {
        // Local-space signed distances; negative values are inside the shape.
        public const string Helpers = @"
float NX_SdfBox(float3 p,float3 size) { float3 q=abs(p)-max(abs(size),.00001); return length(max(q,0))+min(max(q.x,max(q.y,q.z)),0); }
float NX_SdfTorus(float3 p,float radius,float thickness) { return length(float2(length(p.xz)-radius,p.y))-thickness; }
float NX_SdfMin(float a,float b,float k) { if(k<.00001)return min(a,b); float h=saturate(.5+.5*(b-a)/k); return lerp(b,a,h)-k*h*(1-h); }
float NX_SdfBlend(float a,float b,float k,int mode) { if(mode==1)return -NX_SdfMin(-a,b,k); if(mode==2)return -NX_SdfMin(-a,-b,k); return NX_SdfMin(a,b,k); }
";

        public static string Pass(string density, string color, string emission, string distance, string bounds, int steps, string maxDistance, bool depthClip, bool solid)
        {
            var shader = @"Pass {
Name ""Volume""
Tags { ""LightMode""=""" + (solid ? "ForwardBase" : "Always") + @""" }
Cull Front
ZWrite Off
ZTest Always
Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
CGPROGRAM
#pragma target 4.5
#pragma vertex vertVolume
#pragma fragment fragVolume
#pragma multi_compile_instancing
" + (solid ? "#pragma multi_compile_fwdbase\n" : "") + (depthClip ? "UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture);\n" : "") + @"
float NX_VolumeDistance(NXInput input,float3 p) { input.local=p; input.ws=mul(unity_ObjectToWorld,float4(p,1)).xyz; input.originalLocal=p; input.originalWs=input.ws; return " + distance + @"; }
NXInput vertVolume(NXApp v) { UNITY_SETUP_INSTANCE_ID(v); NXInput o=NX_Make(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.screenPos=ComputeScreenPos(o.pos); return o; }
float4 fragVolume(NXInput input):SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
float3 forward=-UNITY_MATRIX_V[2].xyz;
float3 originWs=lerp(_WorldSpaceCameraPos,input.ws-forward*dot(input.ws-_WorldSpaceCameraPos,forward),unity_OrthoParams.w);
float3 ro=mul(unity_WorldToObject,float4(originWs,1)).xyz;
float3 rd=normalize(lerp(input.local-ro,mul((float3x3)unity_WorldToObject,forward),unity_OrthoParams.w));
float3 safeDir=lerp(-1.0,1.0,step(0,rd))*max(abs(rd),.000001);
float3 lo=(-" + bounds + @"-ro)/safeDir, hi=(" + bounds + @"-ro)/safeDir;
float3 entry=min(lo,hi), leave=max(lo,hi);
float begin=max(0,max(entry.x,max(entry.y,entry.z)));
float end=min(leave.x,min(leave.y,leave.z));
float originEye=-mul(UNITY_MATRIX_V,float4(originWs,1)).z;
float rayEye=-mul((float3x3)UNITY_MATRIX_V,mul((float3x3)unity_ObjectToWorld,rd)).z;
begin=max(begin,(_ProjectionParams.y-originEye)/max(rayEye,.000001));
end=min(end,begin+" + maxDistance + @");
" + (depthClip ? @"
float rawDepth=UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture,input.screenPos.xy/input.screenPos.w).r;
float eyeDepth=LinearEyeDepth(rawDepth);
#if defined(UNITY_REVERSED_Z)
rawDepth=1-rawDepth;
#endif
eyeDepth=lerp(eyeDepth,lerp(_ProjectionParams.y,_ProjectionParams.z,rawDepth),unity_OrthoParams.w);
end=min(end,(eyeDepth-originEye)/max(rayEye,.000001));
" : "") + @"
if(end<=begin)return 0;
float dt=(end-begin)/" + steps.ToString(CultureInfo.InvariantCulture) + @";
float4 result=0;
[loop] for(int stepIndex=0; stepIndex<" + steps.ToString(CultureInfo.InvariantCulture) + @"; ++stepIndex) {
 input.local=ro+rd*(begin+(stepIndex+.5)*dt);
 input.ws=mul(unity_ObjectToWorld,float4(input.local,1)).xyz;
 input.originalLocal=input.local; input.originalWs=input.ws;
 float sdf=" + distance + @";
 float inside=1-smoothstep(-dt*.5,dt*.5,sdf);
 float sigma=clamp(" + density + @",0,100)*inside;
 float4 c=(" + color + @")*_Color;
 float alpha=(1-exp(-sigma*dt))*saturate(c.a);
 float3 light=max(0,c.rgb+ (" + emission + @").rgb);
 result.rgb+=(1-result.a)*alpha*light;
 result.a+=(1-result.a)*alpha;
 if(result.a>=.995)break;
}
return result;
}
ENDCG
}
";
            if (!solid) return shader;
            // Replace only the integration block; both modes share ray/box/depth setup.
            int start = shader.IndexOf("float dt=(end-begin)", System.StringComparison.Ordinal);
            int stop = shader.IndexOf("return result;", start, System.StringComparison.Ordinal) + "return result;".Length;
            string trace = @"
float t=begin;
[loop] for(int stepIndex=0;stepIndex<" + steps.ToString(CultureInfo.InvariantCulture) + @"; ++stepIndex) {
 float3 p=ro+rd*t;
 float d=NX_VolumeDistance(input,p);
 if(abs(d)<.0008) {
  float h=.001;
  float3 nLocal=normalize(float3(NX_VolumeDistance(input,p+float3(h,0,0))-NX_VolumeDistance(input,p-float3(h,0,0)),NX_VolumeDistance(input,p+float3(0,h,0))-NX_VolumeDistance(input,p-float3(0,h,0)),NX_VolumeDistance(input,p+float3(0,0,h))-NX_VolumeDistance(input,p-float3(0,0,h))));
  input.local=p; input.ws=mul(unity_ObjectToWorld,float4(p,1)).xyz; input.originalLocal=p; input.originalWs=input.ws;
  float3 n=UnityObjectToWorldNormal(nLocal);
  float3 lightDir=normalize(UnityWorldSpaceLightDir(input.ws));
  float3 view=normalize(_WorldSpaceCameraPos-input.ws);
  float diffuse=saturate(dot(n,lightDir));
  float spec=pow(saturate(dot(n,normalize(lightDir+view))),48)*.35;
  float3 ambient=max(float3(.035,.035,.035),ShadeSH9(float4(n,1)));
  float4 c=(" + color + @")*_Color;
  float3 directColor=_LightColor0.rgb*(1-_WorldSpaceLightPos0.w);
  float3 rgb=c.rgb*(ambient+directColor*diffuse)+directColor*spec+(" + emission + @").rgb;
  float alpha=saturate(c.a); return float4(rgb*alpha,alpha);
 }
 t+=max(abs(d)*.8,.0004);
 if(t>end)break;
}
return 0;
";
            return shader.Substring(0,start)+trace+shader.Substring(stop);
        }
    }
}

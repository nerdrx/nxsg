using System.Globalization;

namespace NXSG.Backend
{
    internal static class FurLighting
    {
        // Local strand-volume approximation. No shadow texture or persistent simulation is allocated.
        public static string Helpers(int quality, double strength, double bias)
        {
            var steps = quality == 1 ? 4 : quality == 2 ? 8 : quality == 3 ? 16 : 0;
            var common = @"void NX_FurUvGradients(float3 a,float3 b,float2 ua,float2 ub,float3 n,out float3 gu,out float3 gv) {
    float3 u=cross(b,n),v=cross(n,a); float determinant=dot(a,u);
    if(abs(determinant)<1e-10) {gu=0;gv=0;return;}
    gu=(ua.x*u+ub.x*v)/determinant;gv=(ua.y*u+ub.y*v)/determinant;
}
float NX_FurSelfShadow(NXInput input,float h,float density,float thickness,float taper,float furLength,float mask,float3 lightDir,float3 gu,float3 gv) {
";
            if(steps==0 || strength<=0)return common+"return 1; }\n";
            return common+@"
    float3 n=normalize(input.n);
    float ndl=dot(n,lightDir);
    if(ndl<=0 || furLength<=0 || mask<=0 || dot(gu,gu)+dot(gv,gv)<1e-10)return 1;
    float3 objectNormal=normalize(mul(n,(float3x3)unity_ObjectToWorld));
    float worldLength=abs(furLength)*length(mul((float3x3)unity_ObjectToWorld,objectNormal));
    float start=saturate(h+BIAS),remaining=1-start;
    float stepHeight=remaining/STEPS;
    float2 uvDirection=float2(dot(gu,lightDir),dot(gv,lightDir));
    float cells=max(1,abs(density));float opticalDepth=0;
    [unroll] for(int index=0;index<STEPS;index++) {
        float sampleHeight=start+(index+.5)*stepHeight;
        float travel=(sampleHeight-h)*worldLength/max(.05,ndl);
        float2 uv=input.uv+uvDirection*travel;
        float2 cell=floor(uv*cells),local=frac(uv*cells)-.5;
        float2 center=float2(NX_Hash(cell+float2(3.1,7.2)),NX_Hash(cell+float2(11.4,2.8)))-.5;
        float radius=max(.0001,thickness*.5*pow(saturate(1-sampleHeight),max(.001,taper)));
        float occupied=1-smoothstep(radius,radius+.015,length(local-center));
        opticalDepth+=occupied*stepHeight/max(.1,ndl);
    }
    return exp(-opticalDepth*8*STRENGTH*saturate(mask));
}
".Replace("STEPS",steps.ToString(CultureInfo.InvariantCulture)).Replace("STRENGTH",((float)strength).ToString("R",CultureInfo.InvariantCulture)).Replace("BIAS",((float)bias).ToString("R",CultureInfo.InvariantCulture));
        }
    }
}

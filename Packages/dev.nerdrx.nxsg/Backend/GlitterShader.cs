namespace NXSG.Backend
{
    // Independent procedural implementation: fixed UV cells with randomized microfacet directions.
    internal static class GlitterShader
    {
        public const string Hlsl = @"
float NX_Glitter(NXInput input,float2 uv,float scale,float density,float size,float sharpness,float viewStrength,float time,float speed,float twinkle,float seed,float mask) {
    float2 p=uv*max(scale,.001),id=floor(p),f=frac(p);
    float footprint=max(length(ddx(p)),length(ddy(p)));
    float aa=max(.001,footprint*.5),nearest=100;float2 winner=id;
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) {
        float2 offset=float2(x,y),cell=id+offset;
        float2 jitter=float2(NX_Hash(cell+seed+13.1),NX_Hash(cell+seed+79.7));
        float d=length(offset+jitter-f);
        if(d<nearest){nearest=d;winner=cell;}
    }
    if(size<=0 || density<=0 || mask<=0)return 0;
    float radius=saturate(size)*.5;
    float shape=1-smoothstep(radius-aa,radius+aa,nearest);
    float present=step(NX_Hash(winner+seed+151.3),saturate(density));
    float3 n=NX_SafeNormal(input.n),t=input.tangent-n*dot(input.tangent,n);
    if(dot(t,t)<.00001)t=cross(n,abs(n.y)<.99?float3(0,1,0):float3(1,0,0));
    t=NX_SafeNormal(t);
    float3 b=NX_SafeNormal(cross(n,t));
    float2 tilt=(float2(NX_Hash(winner+seed+211.1),NX_Hash(winner+seed+307.9))*2-1)*1.4;
    float3 facet=NX_SafeNormal(n+t*tilt.x+b*tilt.y);
    float3 view=NX_SafeNormal(_WorldSpaceCameraPos-input.ws);
    float glint=pow(saturate(dot(facet,view)),max(1,sharpness));
    float pulse=pow(.5+.5*sin(time*speed+NX_Hash(winner+seed+401.3)*6.2831853),4);
    float distantFade=1-smoothstep(1,4,footprint);
    return saturate(shape*present*lerp(1,glint,saturate(viewStrength))*lerp(1,pulse,saturate(twinkle))*saturate(mask)*distantFade);
}
";
    }
}

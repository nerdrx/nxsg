namespace NXSG.Backend
{
    // Original implementation of published GGX/Schlick and Charlie/Ashikhmin equations.
    // References (retrieved 2026-09-24):
    // https://google.github.io/filament/main/filament.html (material models)
    // https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_sheen
    // No upstream shader source or lookup tables are bundled. This is an artistic
    // layering approximation, not a complete glTF BSDF or energy-conservation claim.
    internal static class LayeredPbrShader
    {
        internal const string CoatHelpers = @"
float3 NX_CoatLayer(float3 baseLighting, float3 coatNormal, float3 view, float3 lightDir,
    float3 lightColor, float3 coatEnvironment, float coat, float roughness)
{
    float weight=saturate(coat);
    if(weight<=0)return baseLighting;
    float normalLength=dot(coatNormal,coatNormal);
    float3 n=normalLength>.00000001?coatNormal*rsqrt(normalLength):float3(0,0,1);
    float3 v=view*rsqrt(max(dot(view,view),.00000001));
    float3 l=lightDir*rsqrt(max(dot(lightDir,lightDir),.00000001));
    float3 sum=v+l;
    float3 h=sum*rsqrt(max(dot(sum,sum),.00000001));
    float nv=max(saturate(dot(n,v)),.0001), nl=saturate(dot(n,l));
    float nh=saturate(dot(n,h)), vh=saturate(dot(v,h));
    float r=clamp(roughness,.045,1), a=r*r, a2=a*a;
    float d=nh*nh*(a2-1)+1;
    float distribution=a2/max(3.14159265359*d*d,.0000000001);
    float visibility=.5/max(nl*sqrt(nv*nv*(1-a2)+a2)+nv*sqrt(nl*nl*(1-a2)+a2),.000001);
    float fresnelView=.04+.96*pow(1-nv,5);
    float fresnelHalf=.04+.96*pow(1-vh,5);
    float3 direct=max(0,lightColor)*(distribution*visibility*fresnelHalf*nl);
    // The caller supplies the coat-normal, roughness-filtered probe in ForwardBase and shell overlays.
    // Outgoing Fresnel attenuates the aggregate base lighting; emission stays outside.
    float3 environment=max(0,coatEnvironment)*(fresnelView/(1+r*r));
    return baseLighting*(1-weight*fresnelView)+weight*(direct+environment);
}
";

        internal const string SheenHelpers = @"
float3 NX_SheenLayer(float3 baseLighting, float3 normal, float3 view, float3 lightDir,
    float3 lightColor, float3 diffuseEnvironment, float sheen, float3 sheenColor, float roughness)
{
    float weight=saturate(sheen);
    if(weight<=0)return baseLighting;
    float normalLength=dot(normal,normal);
    float3 n=normalLength>.00000001?normal*rsqrt(normalLength):float3(0,0,1);
    float3 v=view*rsqrt(max(dot(view,view),.00000001));
    float3 l=lightDir*rsqrt(max(dot(lightDir,lightDir),.00000001));
    float3 sum=v+l;
    float3 h=sum*rsqrt(max(dot(sum,sum),.00000001));
    float nv=max(saturate(dot(n,v)),.0001), nl=saturate(dot(n,l));
    float nh=saturate(dot(n,h)), r=clamp(roughness,.07,1);
    float inverseAlpha=1/(r*r);
    float distribution=(2+inverseAlpha)*pow(max(0,1-nh*nh),.5*inverseAlpha)/6.28318530718;
    float visibility=1/max(4*(nl+nv-nl*nv),.0001);
    float3 tint=saturate(sheenColor)*weight;
    float3 direct=max(0,lightColor)*(distribution*visibility*nl);
    // Cheap SH grazing response and base attenuation approximate the missing cloth
    // directional-albedo LUT. They are not a preintegrated, energy-conserving sheen IBL.
    float grazing=.5*pow(1-nv,lerp(8,1,r));
    float keep=1-saturate(max(tint.r,max(tint.g,tint.b)))*grazing;
    return baseLighting*keep+tint*(direct+max(0,diffuseEnvironment)*grazing);
}
";
    }
}

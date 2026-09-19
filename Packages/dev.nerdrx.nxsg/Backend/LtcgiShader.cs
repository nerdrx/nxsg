namespace NXSG.Backend
{
    // Calls the installed LTCGI compatibility API; no upstream implementation is bundled.
    internal static class LtcgiShader
    {
        internal const string Hlsl = @"
#if !defined(UNITY_PASS_FORWARDADD) && !defined(UNITY_PASS_SHADOWCASTER)
#define LTCGI_AVATAR_MODE
#include ""Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc""
#endif
float4 NX_Ltcgi(NXInput input, float4 albedo, float3 tangentNormal, float roughness, float metallic, float strength)
{
#if defined(UNITY_PASS_FORWARDADD) || defined(UNITY_PASS_SHADOWCASTER)
    return 0;
#else
    float3 normalWS=normalize(normalize(input.tangent)*tangentNormal.x+normalize(input.bitangent)*tangentNormal.y+normalize(input.n)*tangentNormal.z);
    half3 diffuse=0, specular=0;
    LTCGI_Contribution(input.ws,normalWS,normalize(_WorldSpaceCameraPos-input.ws),saturate(roughness),input.uv1,diffuse,specular);
    float metal=saturate(metallic);
    float3 tint=max(0,albedo.rgb);
    return float4((diffuse*tint*(1-metal)+specular*lerp(float3(.04,.04,.04),tint,metal))*max(0,strength),1);
#endif
}
";
    }
}

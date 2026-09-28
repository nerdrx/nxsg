namespace NXSG.Backend
{
    // Calls the installed VRC Light Volumes v3 shader API; upstream code is not bundled.
    internal static class LightVolumesShader
    {
        internal const string Hlsl = @"
#include ""Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc""

void NX_LightVolumes(float3 worldPos, float3 normalWS, float3 viewDir,
    float3 albedo, float roughness, float metallic,
    out float3 diffuse, out float3 specular)
{
    normalWS = normalize(normalWS);
    viewDir = normalize(viewDir);
    float3 L0, L1r, L1g, L1b;
    LightVolumeSHSpecular(worldPos, L0, L1r, L1g, L1b, specular,
        max(albedo, 0), saturate(1.0 - roughness), saturate(metallic),
        normalWS, viewDir, 0, 3.0);
    float3 irradiance = max(LightVolumeEvaluate(normalWS, L0, L1r, L1g, L1b), 0);
    diffuse = irradiance * max(albedo, 0) * (1.0 - saturate(metallic));
}
";
    }
}

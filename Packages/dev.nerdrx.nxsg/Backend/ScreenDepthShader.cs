namespace NXSG.Backend
{
    internal static class ScreenDepthShader
    {
        // Shared Built-In pipeline depth sampling for fragment-only AO/contact nodes.
        internal const string Hlsl = @"
#ifndef NXSG_CAMERA_DEPTH_DECLARED
#define NXSG_CAMERA_DEPTH_DECLARED
UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture);
float4 _CameraDepthTexture_TexelSize;
#endif
#define NX_SCREEN_DEPTH_MAX_SAMPLES 32

bool NX_ScreenDepthAvailable()
{
#if defined(UNITY_PASS_SHADOWCASTER)
    return false;
#else
    return _CameraDepthTexture_TexelSize.z > 16 && _CameraDepthTexture_TexelSize.w > 16 &&
        abs(unity_CameraProjection._m20) + abs(unity_CameraProjection._m21) <= 0.00001;
#endif
}

float NX_ScreenEyeDepth(float rawDepth)
{
    float forwardDepth = rawDepth;
#if defined(UNITY_REVERSED_Z)
    forwardDepth = 1 - rawDepth;
#endif
    return lerp(LinearEyeDepth(rawDepth), lerp(_ProjectionParams.y, _ProjectionParams.z, forwardDepth), unity_OrthoParams.w);
}

bool NX_ScreenUvInEye(float2 uv)
{
#if defined(UNITY_SINGLE_PASS_STEREO)
    float4 eyeScaleOffset = unity_StereoScaleOffset[unity_StereoEyeIndex];
    float2 eyeMin = eyeScaleOffset.zw;
    float2 eyeMax = eyeMin + eyeScaleOffset.xy;
    return all(uv >= eyeMin) && all(uv <= eyeMax);
#else
    return all(uv >= 0) && all(uv <= 1);
#endif
}

bool NX_SampleScreenDepth(float2 uv, out float eyeDepth)
{
    eyeDepth = 0;
    if (!NX_ScreenDepthAvailable() || !NX_ScreenUvInEye(uv)) return false;
    float rawDepth = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, uv).r;
    float forwardDepth = rawDepth;
#if defined(UNITY_REVERSED_Z)
    forwardDepth = 1 - rawDepth;
#endif
    if (forwardDepth <= 0 || forwardDepth >= 0.999999) return false;
    eyeDepth = NX_ScreenEyeDepth(rawDepth);
    return eyeDepth > _ProjectionParams.y && eyeDepth < _ProjectionParams.z;
}

int NX_ScreenSampleCount(int samples)
{
    return samples >= 32 ? 32 : samples >= 16 ? 16 : samples >= 8 ? 8 : 4;
}

float2 NX_SSAOPattern(int i, int samples)
{
    const float goldenAngle = 2.39996323;
    float radius = sqrt((i + 0.5) / samples);
    float angle = i * goldenAngle;
    return float2(cos(angle), sin(angle)) * radius;
}

float NX_ScreenSpaceOcclusion(NXInput input, float radius, float strength, float thickness, float bias, int samples)
{
#if defined(UNITY_PASS_SHADOWCASTER)
    return 1;
#else
    if (!NX_ScreenDepthAvailable()) return 1;
    float4 clipPosition = UnityWorldToClipPos(input.ws);
    if (clipPosition.w <= 0.00001) return 1;
    float4 screenPosition = ComputeScreenPos(clipPosition);
    float2 uv = screenPosition.xy / screenPosition.w;
    float centerEyeDepth = -mul(UNITY_MATRIX_V, float4(input.ws, 1)).z;
    if (centerEyeDepth <= _ProjectionParams.y || centerEyeDepth >= _ProjectionParams.z) return 1;
    float2 uvDx = ddx(uv), uvDy = ddy(uv);
    float uvDeterminant = uvDx.x * uvDy.y - uvDx.y * uvDy.x;
    if (abs(uvDeterminant) < 1e-10) return 1;
    float depthDx = ddx(centerEyeDepth), depthDy = ddy(centerEyeDepth);
    float2 projectedRadius = max(0, radius) * 0.5 * float2(abs(UNITY_MATRIX_P._m00), abs(UNITY_MATRIX_P._m11));
    projectedRadius *= lerp(1 / max(centerEyeDepth, 0.0001), 1, unity_OrthoParams.w);
#if defined(UNITY_SINGLE_PASS_STEREO)
    projectedRadius *= unity_StereoScaleOffset[unity_StereoEyeIndex].xy;
#endif
    float depthBias = max(0, bias);
    float depthThickness = max(max(0, thickness), depthBias + 0.0001);
    float transition = max(0.0001, depthThickness * 0.1);
    int sampleCount = NX_ScreenSampleCount(samples);
    float occlusion = 0;
    [unroll]
    for (int i = 0; i < NX_SCREEN_DEPTH_MAX_SAMPLES; i++)
    {
        if (i < sampleCount)
        {
            float sampleEyeDepth;
            float2 sampleOffset = NX_SSAOPattern(i, sampleCount) * projectedRadius;
            float2 pixelOffset = float2(sampleOffset.x * uvDy.y - sampleOffset.y * uvDy.x,
                uvDx.x * sampleOffset.y - uvDx.y * sampleOffset.x) / uvDeterminant;
            float expectedPlaneDepth = centerEyeDepth + depthDx * pixelOffset.x + depthDy * pixelOffset.y;
            if (NX_SampleScreenDepth(uv + sampleOffset, sampleEyeDepth))
            {
                // Plane extrapolation avoids treating a sloped receiver as its own occluder.
                float gap = expectedPlaneDepth - sampleEyeDepth;
                float nearFade = smoothstep(depthBias, depthBias + transition, gap);
                float thicknessFade = 1 - smoothstep(depthThickness, depthThickness + transition, gap);
                occlusion += nearFade * thicknessFade;
            }
        }
    }
    return 1 - saturate(strength) * saturate(occlusion / sampleCount);
#endif
}

float NX_ScreenContactShadow(NXInput input, float3 directionToLight, float distance, float strength, float thickness, float bias, int samples)
{
#if defined(UNITY_PASS_SHADOWCASTER)
    return 1;
#else
    if (!NX_ScreenDepthAvailable() || distance <= 0) return 1;
    float directionLength = dot(directionToLight, directionToLight);
    float normalLength = dot(input.n, input.n);
    if (directionLength < 0.000001 || normalLength < 0.000001) return 1;
    float3 lightDirection = directionToLight * rsqrt(directionLength);
    float3 normal = input.n * rsqrt(normalLength);
    float depthBias = max(0, bias);
    float depthThickness = max(max(0, thickness), depthBias + 0.0001);
    float transition = max(0.0001, depthThickness * 0.1);
    int sampleCount = NX_ScreenSampleCount(samples);
    float3 rayOrigin = input.ws + normal * depthBias;
    float occlusion = 0;
    [unroll]
    for (int i = 0; i < NX_SCREEN_DEPTH_MAX_SAMPLES; i++)
    {
        if (i < sampleCount)
        {
            float travel = distance * ((i + 1.0) / sampleCount);
            float3 rayPosition = rayOrigin + lightDirection * travel;
            float4 clipPosition = UnityWorldToClipPos(rayPosition);
            if (clipPosition.w > 0.00001)
            {
                float4 screenPosition = ComputeScreenPos(clipPosition);
                float2 uv = screenPosition.xy / screenPosition.w;
                float sceneEyeDepth;
                if (NX_SampleScreenDepth(uv, sceneEyeDepth))
                {
                    float rayEyeDepth = -mul(UNITY_MATRIX_V, float4(rayPosition, 1)).z;
                    float gap = rayEyeDepth - sceneEyeDepth;
                    float hit = smoothstep(depthBias, depthBias + transition, gap) * (1 - smoothstep(depthThickness, depthThickness + transition, gap));
                    occlusion = max(occlusion, hit);
                }
            }
        }
    }
    return 1 - saturate(strength) * saturate(occlusion);
#endif
}
";
    }
}

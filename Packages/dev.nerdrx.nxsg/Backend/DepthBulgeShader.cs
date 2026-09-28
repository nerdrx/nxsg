namespace NXSG.Backend
{
    internal static class DepthBulgeShader
    {
        // Scene-depth proximity, not collision detection. Keep camera-dependent
        // deformation out of shadow/depth rendering to avoid a feedback loop.
        internal const string Hlsl = @"
#ifndef NXSG_CAMERA_DEPTH_DECLARED
#define NXSG_CAMERA_DEPTH_DECLARED
UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture);
float4 _CameraDepthTexture_TexelSize;
#endif
float NX_DepthBulgeTouch(float3 worldPosition, float distance, float falloff, float bias)
{
#if defined(UNITY_PASS_SHADOWCASTER)
    return 0;
#else
    // Missing depth is normally an unbound/tiny fallback texture. Oblique
    // projections (including mirrors) don't share the usual depth conversion.
    if (_CameraDepthTexture_TexelSize.z <= 16 || _CameraDepthTexture_TexelSize.w <= 16 ||
        abs(unity_CameraProjection._m20) + abs(unity_CameraProjection._m21) > 0.00001)
        return 0;
    float range = max(0, distance);
    float epsilon = max(0.00001, bias);
    if (range <= epsilon) return 0;
    float4 clipPosition = UnityWorldToClipPos(worldPosition);
    float eyeDepth = -mul(UNITY_MATRIX_V, float4(worldPosition, 1)).z;
    if (clipPosition.w <= 0.00001 || eyeDepth <= _ProjectionParams.y || eyeDepth >= _ProjectionParams.z) return 0;
    float4 screenPosition = ComputeScreenPos(clipPosition);
    float2 uv = screenPosition.xy / screenPosition.w;
    if (any(uv < 0) || any(uv > 1)) return 0;
    float rawDepth;
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    rawDepth = UNITY_SAMPLE_TEX2DARRAY_LOD(_CameraDepthTexture, float3(uv, (float)unity_StereoEyeIndex), 0).r;
#else
    rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0));
#endif
    float forwardDepth = rawDepth;
#if defined(UNITY_REVERSED_Z)
    forwardDepth = 1 - rawDepth;
#endif
    // Far-plane/cleared depth must not look like a nearby touching surface.
    if (forwardDepth >= 0.999999 || forwardDepth <= 0) return 0;
    float sceneDepth = lerp(LinearEyeDepth(rawDepth),
        lerp(_ProjectionParams.y, _ProjectionParams.z, forwardDepth), unity_OrthoParams.w);
    float gap = abs(sceneDepth - eyeDepth);
    float selfFade = smoothstep(epsilon, epsilon + max(0.00001, range * 0.05), gap);
    float proximity = 1 - smoothstep(epsilon, range, gap);
    return selfFade * pow(saturate(proximity), max(0.001, falloff));
#endif
}

void NX_DepthBulgeNormals(inout NXInput input)
{
    // Preserve the smooth mesh normal, adding the change in geometric normal
    // caused by displacement. Undeformed areas retain their original shading.
    float3 before = cross(ddx(input.originalWs), ddy(input.originalWs));
    float3 after = cross(ddx(input.ws), ddy(input.ws));
    float beforeLength = dot(before, before), afterLength = dot(after, after);
    if (beforeLength < 1e-16 || afterLength < 1e-16) return;
    before *= rsqrt(beforeLength); after *= rsqrt(afterLength);
    float orientation = dot(before, input.n) < 0 ? -1 : 1;
    float3 normal = normalize(input.n) + (after - before) * orientation;
    if (dot(normal, normal) < 1e-8) return;
    float handedness = dot(cross(input.n, input.tangent), input.bitangent) < 0 ? -1 : 1;
    input.n = normalize(normal);
    float3 tangent = input.tangent - input.n * dot(input.tangent, input.n);
    if (dot(tangent, tangent) > 1e-8)
    {
        input.tangent = normalize(tangent);
        input.bitangent = cross(input.n, input.tangent) * handedness;
    }
}
";
    }
}

using System;
using NXSG.Backend;

public static class ScreenDepthShaderChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var hlsl = ScreenDepthShader.Hlsl;
        assert(hlsl.Contains("NX_ScreenSpaceOcclusion(NXInput input, float radius, float strength, float thickness, float bias, int samples)"), "SSAO helper contract");
        assert(hlsl.Contains("NX_ScreenContactShadow(NXInput input, float3 directionToLight, float distance, float strength, float thickness, float bias, int samples)"), "contact-shadow helper contract");
        assert(hlsl.Contains("#define NX_SCREEN_DEPTH_MAX_SAMPLES 32") && hlsl.Contains("return samples >= 32 ? 32 : samples >= 16 ? 16 : samples >= 8 ? 8 : 4;") &&
            hlsl.Contains("for (int i = 0; i < NX_SCREEN_DEPTH_MAX_SAMPLES; i++)") && hlsl.Contains("if (i < sampleCount)"),
            "screen-depth quality is clamped to fixed 4/8/16/32 sample budgets");
        assert(hlsl.Contains("UNITY_DECLARE_SCREENSPACE_TEXTURE(_CameraDepthTexture)") && hlsl.Contains("UNITY_SAMPLE_SCREENSPACE_TEXTURE(_CameraDepthTexture, uv)"),
            "screen-depth access uses Unity stereo-aware texture macros");
        assert(hlsl.Contains("#if defined(UNITY_SINGLE_PASS_STEREO)") &&
            hlsl.Contains("unity_StereoScaleOffset[unity_StereoEyeIndex]") &&
            hlsl.Contains("return all(uv >= eyeMin) && all(uv <= eyeMax);") &&
            hlsl.Contains("projectedRadius *= unity_StereoScaleOffset[unity_StereoEyeIndex].xy;"),
            "double-wide stereo samples and AO radius stay inside the active eye viewport");
        assert(hlsl.Contains("UNITY_REVERSED_Z") && hlsl.Contains("unity_OrthoParams.w") && hlsl.Contains("UNITY_MATRIX_P._m00") && hlsl.Contains("UNITY_MATRIX_P._m11"),
            "reversed-Z and perspective/orthographic projection paths are represented");
        assert(hlsl.Contains("_CameraDepthTexture_TexelSize.z > 16") && hlsl.Contains("abs(unity_CameraProjection._m20) + abs(unity_CameraProjection._m21) <= 0.00001") &&
            hlsl.Contains("#if defined(UNITY_PASS_SHADOWCASTER)\n    return 1;"), "unavailable, oblique and shadow-caster paths are neutral");
        assert(hlsl.Contains("expectedPlaneDepth = centerEyeDepth + depthDx * pixelOffset.x + depthDy * pixelOffset.y") &&
            hlsl.Contains("float gap = expectedPlaneDepth - sampleEyeDepth"), "AO uses a tangent-plane depth estimate");
        assert(hlsl.Contains("return 1 - saturate(strength) * saturate(occlusion / sampleCount)") &&
            hlsl.Contains("return 1 - saturate(strength) * saturate(occlusion);"), "helpers return neutral-one visibility masks");
        assert(!hlsl.Contains("while (") && !hlsl.Contains("[loop]"), "screen-depth helpers contain no unbounded or runtime-selected loops");
    }
}

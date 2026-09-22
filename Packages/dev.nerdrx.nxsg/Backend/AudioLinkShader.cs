using System;

namespace NXSG.Backend
{
    /// <summary>
    /// Standalone Built-In shader snippet for AudioLink 128x64 textures.
    /// The generated shader declares _AudioTexture and the preview uniforms
    /// before inserting <see cref="Hlsl"/>.
    /// </summary>
    public static class AudioLinkShader
    {
        /// <summary>CG/HLSL declarations and float NXSG_Audio(...) implementation.</summary>
        public const string Hlsl = @"
Texture2D<float4> _AudioTexture;
float4 _AudioTexture_TexelSize;
float _NXSG_AudioLinkPreview;
float _NXSG_AudioLinkValue;

// AudioLink layout: current 4-band values at (0,0..3), filtered values at
// (smoothing level 0..15, 28..31). Missing/default textures return fallback.
float NXSG_Audio(float band, float gain, float smoothing, float fallback, int rangeEnabled, float minimum, float maximum)
{
    if (_NXSG_AudioLinkPreview > 0.5)
    {
        float preview = _NXSG_AudioLinkValue * gain;
        return rangeEnabled != 0 ? lerp(minimum, maximum, saturate(preview)) : preview;
    }

    // AudioLink's official availability test is width > 16. Filtered rows
    // require the 128x64 layout.
    int width, height;
#if defined(SHADER_API_GLCORE)
    width = (int)_AudioTexture_TexelSize.z;
    height = (int)_AudioTexture_TexelSize.w;
#else
    _AudioTexture.GetDimensions(width, height);
#endif
    if (width <= 16 || height < 32)
        return fallback;

    float b = clamp(floor(band + 0.5), 0.0, 3.0);
    // UI supplies normalized smoothing: 0 = least smoothed (raw), 1 = most.
    // AudioLink stores least smoothed at x=15 and most smoothed at x=0.
    float s = clamp(floor((1.0 - saturate(smoothing)) * 15.0 + 0.5), 0.0, 15.0);
    int2 pixel = smoothing <= 0.0 ? int2(0, (int)b) : int2((int)s, 28 + (int)b);
    float value = _AudioTexture.Load(int3(pixel, 0)).r;
    value *= gain;
    return rangeEnabled != 0 ? lerp(minimum, maximum, saturate(value)) : value;
}
";

        /// <summary>Returns a stable insertion marker for emitters that append shader text.</summary>
        public const string FunctionName = "NXSG_Audio";
    }
}

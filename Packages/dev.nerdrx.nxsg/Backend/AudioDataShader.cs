using System;

namespace NXSG.Backend
{
    /// <summary>Built-In HLSL helpers for additional AudioLink data blocks.</summary>
    public static class AudioDataShader
    {
        /// <summary>ShaderLab declarations for predictable editor preview inputs.</summary>
        public const string PreviewProperties = "[HideInInspector] _NXSG_AudioDataPreview (\"Preview AudioLink data\", Float) = 0\n" +
            "[HideInInspector] _NXSG_AudioSpectrumPreview (\"Spectrum preview\", Float) = 0\n" +
            "[HideInInspector] _NXSG_AudioChronotensityPreview (\"Chronotensity preview\", Float) = 0\n" +
            "[HideInInspector] _NXSG_AudioThemePreview (\"Theme color preview\", Color) = (1,1,1,1)";

        /// <summary>HLSL helpers. Requires `_AudioTexture` and `_AudioTexture_TexelSize` declarations.</summary>
        public const string Hlsl = @"
float _NXSG_AudioDataPreview;
float _NXSG_AudioSpectrumPreview;
float _NXSG_AudioChronotensityPreview;
float4 _NXSG_AudioThemePreview;

bool NXSG_AudioDataAvailable(int requiredWidth, int requiredHeight)
{
    int width, height;
#if defined(SHADER_API_GLCORE)
    width = (int)_AudioTexture_TexelSize.z;
    height = (int)_AudioTexture_TexelSize.w;
#else
    _AudioTexture.GetDimensions(width, height);
#endif
    return width >= requiredWidth && height >= requiredHeight;
}

float4 NXSG_AudioSpectrumBinData(float bin)
{
    // DFT begins at (0,4), with 240 semitone-quarter bins laid out multiline.
    float b = clamp(bin, 0.0, 239.0);
    int lo = (int)floor(b);
    int hi = min(lo + 1, 239);
    float4 a = _AudioTexture.Load(int3(lo % 128, 4 + lo / 128, 0));
    float4 z = _AudioTexture.Load(int3(hi % 128, 4 + hi / 128, 0));
    return lerp(a, z, frac(b));
}

float NXSG_AudioSpectrumBin(float bin, int channel, float gain, float fallbackValue)
{
    if (_NXSG_AudioDataPreview > 0.5) return _NXSG_AudioSpectrumPreview * gain;
    if (!NXSG_AudioDataAvailable(128, 6)) return fallbackValue;
    float4 value = NXSG_AudioSpectrumBinData(bin);
    float magnitude = channel == 1 ? value.g : channel == 2 ? value.b : value.r;
    return magnitude * gain;
}

float NXSG_AudioSpectrumFrequency(float frequency, int channel, float gain, float fallbackValue)
{
    float hz = clamp(frequency, 13.75, 14080.0);
    float bin = 24.0 * log2(hz / 13.75);
    return NXSG_AudioSpectrumBin(bin, channel, gain, fallbackValue);
}

float NXSG_AudioVisualizer(float2 uv,int bars,int radial,float lowHz,float highHz,float gain,float gap)
{
    float x=uv.x, y=uv.y;
    if(radial!=0) { float2 p=(uv-.5)*2; x=frac(atan2(p.y,p.x)/6.283185307+.5); y=length(p); }
    if(x<0 || x>1 || y<0 || y>1) return 0;
    float scaled=min(x,.999999)*bars;
    float band=(floor(scaled)+.5)/bars;
    float frequency=exp2(lerp(log2(max(13.75,lowHz)),log2(max(13.75,highHz)),band));
    float height=saturate(NXSG_AudioSpectrumFrequency(frequency,0,gain,0));
    return step(y,height)*step(.000001,height)*step(saturate(gap)*.5,frac(scaled))*step(frac(scaled),1-saturate(gap)*.5);
}

uint NXSG_AudioChronotensityData(int index, int band)
{
    float4 raw = _AudioTexture.Load(int3(16 + clamp(index, 0, 7), 28 + clamp(band, 0, 3), 0));
    uint4 channels = (uint4)max(0.0, floor(raw + 0.5));
    return channels.x + channels.y * 1024u + channels.z * 1048576u + channels.w * 1073741824u;
}

float NXSG_AudioChronotensity(int index, int band, float speed, int normalized, float fallbackValue)
{
    if (_NXSG_AudioDataPreview > 0.5)
    {
        float preview = _NXSG_AudioChronotensityPreview * speed;
        return normalized != 0 ? frac(preview) : preview;
    }
    if (!NXSG_AudioDataAvailable(24, 32)) return fallbackValue;
    float timeValue = (float)NXSG_AudioChronotensityData(index, band) / 100000.0 * speed;
    return normalized != 0 ? frac(timeValue) : timeValue;
}

float4 NXSG_AudioThemeColor(int index, float4 fallbackColor)
{
    if (_NXSG_AudioDataPreview > 0.5) return _NXSG_AudioThemePreview;
    if (!NXSG_AudioDataAvailable(4, 24)) return fallbackColor;
    return _AudioTexture.Load(int3(clamp(index, 0, 3), 23, 0));
}
";

        public static string SpectrumFrequency(string frequency, string channel, string gain, string fallbackValue)
        {
            return "NXSG_AudioSpectrumFrequency(" + frequency + "," + channel + "," + gain + "," + fallbackValue + ")";
        }

        public static string SpectrumBin(string bin, string channel, string gain, string fallbackValue)
        {
            return "NXSG_AudioSpectrumBin(" + bin + "," + channel + "," + gain + "," + fallbackValue + ")";
        }

        public static string Chronotensity(string index, string band, string speed, string normalized, string fallbackValue)
        {
            return "NXSG_AudioChronotensity(" + index + "," + band + "," + speed + "," + normalized + "," + fallbackValue + ")";
        }

        public static string ThemeColor(string index, string fallbackColor)
        {
            return "NXSG_AudioThemeColor(" + index + "," + fallbackColor + ")";
        }
    }
}

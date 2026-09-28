using System;

namespace NXSG.Backend
{
    /// <summary>HLSL contracts for clock, MSDF decal, and numeric SDF text nodes.</summary>
    public static class UtilityShader
    {
        public const string PreviewProperties = "[HideInInspector] _NXSG_PreviewClock (\"Preview clock\", Float) = 0\n[HideInInspector] _NXSG_PreviewTime (\"Preview seconds\", Float) = 0";

        public const string ClockHlsl = @"
uint _VRChatTimeNetworkMs;
float NXSG_UtilityUnityTime() { return _NXSG_PreviewClock > .5 ? _NXSG_PreviewTime : _Time.y; }
uint NXSG_ClockPeriodMs(float periodSeconds) { return (uint)clamp(floor(max(periodSeconds, .001) * 1000.0 + .5), 1.0, 86400000.0); }
uint NXSG_ClockShiftedMs(float offsetSeconds) { return _VRChatTimeNetworkMs + (uint)(int)round(clamp(offsetSeconds, -2147483.0, 2147483.0) * 1000.0); }
float NXSG_NetworkClockSeconds(float offsetSeconds) { return (float)NXSG_ClockShiftedMs(offsetSeconds) * .001; }
float NXSG_NetworkClockPhase(float periodSeconds, float offsetSeconds) { uint p = NXSG_ClockPeriodMs(periodSeconds); return (float)(NXSG_ClockShiftedMs(offsetSeconds) % p) / (float)p; }
float NXSG_NetworkClockCycle(float periodSeconds, float offsetSeconds) { return (float)(NXSG_ClockShiftedMs(offsetSeconds) / NXSG_ClockPeriodMs(periodSeconds)); }
float NXSG_UtilityClockSeconds(float source, float offsetSeconds) { return _NXSG_PreviewClock > .5 ? _NXSG_PreviewTime + offsetSeconds : (source > .5 ? NXSG_NetworkClockSeconds(offsetSeconds) : _Time.y + offsetSeconds); }
float NXSG_UtilityClockPhase(float source, float periodSeconds, float offsetSeconds) { return _NXSG_PreviewClock > .5 ? frac((_NXSG_PreviewTime + offsetSeconds) / max(periodSeconds, .001)) : (source > .5 ? NXSG_NetworkClockPhase(periodSeconds, offsetSeconds) : frac((_Time.y + offsetSeconds) / max(periodSeconds, .001))); }
float NXSG_UtilityClockCycle(float source, float periodSeconds, float offsetSeconds) { return _NXSG_PreviewClock > .5 ? floor((_NXSG_PreviewTime + offsetSeconds) / max(periodSeconds, .001)) : (source > .5 ? NXSG_NetworkClockCycle(periodSeconds, offsetSeconds) : floor((_Time.y + offsetSeconds) / max(periodSeconds, .001))); }
";

        public const string MsdfHlsl = @"
float NXSG_Median3(float3 x) { return max(min(x.r, x.g), min(max(x.r, x.g), x.b)); }
float NXSG_MSDFCoverage(float3 sampleRgb, float2 uv, float4 texelSize, float distanceRange, float softness)
{
    float sd = NXSG_Median3(sampleRgb);
    float2 texelsPerPixel = max(abs(ddx(uv)) + abs(ddy(uv)), float2(1e-6,1e-6));
    distanceRange = clamp(distanceRange, .25, 256.0);
    float screenRange = max(distanceRange / max(dot(texelsPerPixel, texelSize.zw) * .5, 1e-4), 1.0);
    float d = (sd - .5) * screenRange;
    float feather = max(fwidth(d), max(softness, 1e-4));
    return smoothstep(-feather, feather, d);
}
float4 NXSG_MsdfDecal(float3 sampleRgb, float2 uv, float4 texelSize, float4 tint, float4 outline, float outlineWidth, float softness, float distanceRange)
{
    float sd = NXSG_Median3(sampleRgb);
    float2 texelsPerPixel = max(abs(ddx(uv)) + abs(ddy(uv)), float2(1e-6,1e-6));
    distanceRange = clamp(distanceRange, .25, 256.0);
    outlineWidth = clamp(outlineWidth, 0.0, 32.0);
    softness = clamp(softness, 0.0, 16.0);
    float screenRange = max(distanceRange / max(dot(texelsPerPixel, texelSize.zw) * .5, 1e-4), 1.0);
    float d = (sd - .5) * screenRange;
    float feather = max(fwidth(d), max(softness, 1e-4));
    float outer = smoothstep(-feather, feather, d + max(outlineWidth, 0.0));
    float fill = smoothstep(-feather, feather, d);
    float3 rgb = lerp(outline.rgb, tint.rgb, fill);
    return float4(rgb, outer * lerp(outline.a, tint.a, fill));
}
";

        public const string NumericTextHlsl = @"
float NXSG_NumberGlyph(float value, float2 uv, sampler2D atlas, float scale, float spacing, int digits, int decimals)
{
    float2 p = uv / max(min(scale, 64.0), .001);
    if (p.x < 0.0 || p.x >= 1.0 || p.y < 0.0 || p.y >= 1.0) return 0.0;
    int signSlot = digits + decimals + (decimals > 0 ? 1 : 0);
    int slots = signSlot + 1;
    int column = min((int)floor(p.x * slots), slots - 1);
    int place = slots - 1 - column;
    float decimalScale = pow(10.0, decimals);
    float maxScaled = pow(10.0, digits + decimals) - 1.0;
    float scaled = min(floor(abs(value) * decimalScale + .5), maxScaled);
    float whole = floor(scaled / decimalScale);
    float fractional = fmod(scaled, decimalScale);
    int glyph = -1;
    if (decimals > 0 && place == decimals) glyph = 11;
    else if (place == signSlot) glyph = value < 0.0 && scaled > 0.0 ? 10 : -1;
    else if (place < decimals) { float d = fmod(floor(fractional / pow(10.0, place)), 10.0); glyph = (int)d; }
    else
    {
        int wholePlace = place - decimals - (decimals > 0 ? 1 : 0);
        float divisor = pow(10.0, wholePlace);
        float d = fmod(floor(whole / divisor), 10.0);
        bool leading = wholePlace > 0 && floor(whole / divisor) < 1.0;
        glyph = leading ? -1 : (int)d;
    }
    if (glyph < 0) return 0.0;
    float2 cell = float2(glyph % 4, glyph / 4);
    float2 local = float2((frac(p.x * slots) - .5) / (1.0 - clamp(spacing, 0.0, .8)) + .5, p.y);
    if (local.x < 0.0 || local.x > 1.0) return 0.0;
    float2 atlasUv = (cell + local) * float2(.25, 1.0 / 3.0);
    float sd = tex2D(atlas, atlasUv).r;
    float width = max(fwidth(sd), .001);
    return smoothstep(.5 - width, .5 + width, sd);
}
";

        /// <summary>Clock output expression factory. source=0 is legacy Unity/preview; source=1 is VRChat.</summary>
        public static string ClockExpression(string output, string source, string period, string offset)
        {
            if (output == "seconds") return "NXSG_UtilityClockSeconds(" + source + "," + offset + ")";
            if (output == "phase") return "NXSG_UtilityClockPhase(" + source + "," + period + "," + offset + ")";
            if (output == "cycle") return "NXSG_UtilityClockCycle(" + source + "," + period + "," + offset + ")";
            throw new ArgumentException("Unknown clock output: " + output, nameof(output));
        }

        /// <summary>Wraps an RGB MSDF sample. distanceRange is the atlas encoding range in texels.</summary>
        public static string MsdfExpression(string uv, string sampler, string texelSize, string tint, string outline, string width, string softness, string distanceRange)
        {
            return "NXSG_MsdfDecal(tex2D(" + sampler + "," + uv + ").rgb," + uv + "," + texelSize + "," + tint + "," + outline + "," + width + "," + softness + "," + distanceRange + ")";
        }

        public static string MsdfAlphaExpression(string uv, string sampler, string texelSize, string tint, string outline, string width, string softness, string distanceRange)
        { return "(" + MsdfExpression(uv, sampler, texelSize, tint, outline, width, softness, distanceRange) + ").a"; }

        public static string NumberAlphaExpression(string uv, string value, string sampler, string scale, string spacing, int digits, int decimals)
        {
            return "NXSG_NumberGlyph(" + value + "," + uv + "," + sampler + "," + scale + "," + spacing + "," + Math.Max(1, Math.Min(8, digits)) + "," + Math.Max(0, Math.Min(4, decimals)) + ")";
        }

        public static string NumberColorExpression(string uv, string value, string sampler, string tint, string scale, string spacing, int digits, int decimals)
        {
            return "float4(" + tint + ".rgb," + tint + ".a*" + NumberAlphaExpression(uv, value, sampler, scale, spacing, digits, decimals) + ")";
        }

        public const string ViewerStatsHlsl = @"
float NXSG_ViewerRenderFps() { return 1.0 / max(unity_DeltaTime.x, 1e-4); }
float NXSG_ViewerDeltaSeconds() { return unity_DeltaTime.x; }
float NXSG_ViewerCameraDistance(float3 worldPosition) { return distance(_WorldSpaceCameraPos, worldPosition); }
";

        public static string ViewerStatsExpression(string output)
        {
            switch (output)
            {
                case "renderFps": return "NXSG_ViewerRenderFps()";
                case "deltaSeconds": return "NXSG_ViewerDeltaSeconds()";
                case "worldPosition": return "input.ws";
                case "cameraDistance": return "NXSG_ViewerCameraDistance(input.ws)";
                case "unitySeconds": return "NXSG_UtilityUnityTime()";
                case "networkSeconds": return "NXSG_UtilityClockSeconds(1.0,0.0)";
                default: throw new ArgumentException("Unknown viewer stats output: " + output, nameof(output));
            }
        }
    }
}

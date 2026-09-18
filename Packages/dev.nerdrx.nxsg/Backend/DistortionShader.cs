namespace NXSG.Backend
{
    // UV distortion and remapping helpers. Integration decides where this snippet is emitted.
    internal static class DistortionShader
    {
        public const string Hlsl = @"
float NX_DistortionFbm(float2 p, int detail)
{
    int count = clamp(detail, 1, 6);
    float sum = 0.0;
    float weight = 0.0;
    float amplitude = 1.0;
    for (int octave = 0; octave < 6; octave++)
    {
        if (octave >= count) break;
        sum += NX_Noise(p) * amplitude;
        weight += amplitude;
        p *= 2.0;
        amplitude *= 0.5;
    }
    return weight > 0.0 ? sum / weight : 0.0;
}

float2 NX_Warp(float2 uv, int mode, float amount, float scale, float time,
    float2 center, float2 direction, float2 axes, float radius, float falloff,
    int detail, float2 flow)
{
    if (amount == 0.0) return uv;

    float2 delta = uv - center;
    float radialDistance = length(delta);
    float safeRadius = max(abs(radius), 1e-5);
    float edge = saturate(1.0 - radialDistance / safeRadius);
    float mask = smoothstep(0.0, 1.0, pow(edge, max(falloff, 1e-5)));
    float2 offset = 0.0;

    if (mode == 0)
    {
        float2 samplePoint = uv * scale + direction * time;
        float2 noise = detail <= 1
            ? float2(NX_Noise(samplePoint), NX_Noise(samplePoint + 17.2))
            : float2(NX_DistortionFbm(samplePoint, detail),
                NX_DistortionFbm(samplePoint + 17.2, detail));
        offset = (noise - 0.5) * amount * axes;
    }
    else if (mode == 1)
    {
        float2 phaseDirection = direction;
        float phaseLength = max(length(phaseDirection), 1e-5);
        phaseDirection /= phaseLength;
        float phase = dot(delta, phaseDirection) * scale + time;
        offset = float2(sin(phase), sin(phase + 1.57079632679)) * amount * axes;
    }
    else if (mode == 2)
    {
        float angle = amount * mask;
        float sine = sin(angle), cosine = cos(angle);
        float2 rotated = float2(delta.x * cosine - delta.y * sine,
            delta.x * sine + delta.y * cosine);
        offset = (rotated - delta) * axes;
    }
    else if (mode == 3)
    {
        float2 unitDelta = delta / max(radialDistance, 1e-5);
        float wave = sin(radialDistance * scale - time) * amount * mask;
        offset = unitDelta * wave * axes;
    }
    else if (mode == 4)
    {
        offset = (flow * 2.0 - 1.0) * amount * axes;
    }
    else if (mode == 5)
    {
        float cells = max(abs(scale), 1.0);
        float2 snapped = (floor(uv * cells) + 0.5) / cells;
        offset = (snapped - uv) * axes * saturate(abs(amount));
    }
    else if (mode == 6)
    {
        float lens = amount * mask * (1.0 - saturate(radialDistance / safeRadius)) * 0.25;
        offset = delta * lens * axes;
    }

    return uv + offset;
}

float NX_Gradient(float2 uv, int mode, float2 center, float angle, float radius)
{
    float2 delta = uv - center;
    if (mode == 0)
    {
        float angleRadians = angle * 0.0174532925199433;
        return saturate(dot(delta, float2(cos(angleRadians), sin(angleRadians))) + 0.5);
    }
    if (mode == 1)
        return saturate(length(delta) / max(abs(radius), 1e-5));
    if (mode == 2)
    {
        if (length(delta) < 1e-5) return 0.0;
        float angleRadians = angle * 0.0174532925199433;
        return frac(atan2(delta.y, delta.x) / 6.283185307179586 + 0.5 + angleRadians / 6.283185307179586);
    }
    return 0.0;
}

float2 NX_TileUV(float2 uv, int mode, float2 tiling, float2 offset)
{
    float2 coordinate = uv * tiling + offset;
    if (mode == 0) return frac(coordinate);
    if (mode == 1) return 1.0 - abs(frac(coordinate * 0.5) * 2.0 - 1.0);
    return saturate(coordinate);
}

float NX_Posterize(float value, float levels)
{
    float count = clamp(floor(levels + 0.5), 2.0, 256.0);
    return floor(saturate(value) * (count - 1.0) + 0.5) / (count - 1.0);
}
";
    }
}

namespace NXSG.Backend
{
    // Portable procedural helpers. Integration decides where this snippet is emitted.
    internal static class ProceduralShader
    {
        public const string Hlsl = @"
float NX_ProcHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float NX_ProcHash1(float p)
{
    return frac(sin(p * 127.1) * 43758.5453);
}

float NX_ProcHash3(float3 p)
{
    return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

float NX_ProcHash4(float4 p)
{
    return frac(sin(dot(p, float4(127.1, 311.7, 74.7, 181.3))) * 43758.5453);
}

float NX_Noise1(float p)
{
    float i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(NX_ProcHash1(i), NX_ProcHash1(i + 1.0), f);
}

float NX_Noise3(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = NX_ProcHash3(i);
    float n100 = NX_ProcHash3(i + float3(1, 0, 0));
    float n010 = NX_ProcHash3(i + float3(0, 1, 0));
    float n110 = NX_ProcHash3(i + float3(1, 1, 0));
    float n001 = NX_ProcHash3(i + float3(0, 0, 1));
    float n101 = NX_ProcHash3(i + float3(1, 0, 1));
    float n011 = NX_ProcHash3(i + float3(0, 1, 1));
    float n111 = NX_ProcHash3(i + 1);
    return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
}

float NX_Noise4(float4 p)
{
    float4 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n0000 = NX_ProcHash4(i);
    float n1000 = NX_ProcHash4(i + float4(1, 0, 0, 0));
    float n0100 = NX_ProcHash4(i + float4(0, 1, 0, 0));
    float n1100 = NX_ProcHash4(i + float4(1, 1, 0, 0));
    float n0010 = NX_ProcHash4(i + float4(0, 0, 1, 0));
    float n1010 = NX_ProcHash4(i + float4(1, 0, 1, 0));
    float n0110 = NX_ProcHash4(i + float4(0, 1, 1, 0));
    float n1110 = NX_ProcHash4(i + float4(1, 1, 1, 0));
    float n0001 = NX_ProcHash4(i + float4(0, 0, 0, 1));
    float n1001 = NX_ProcHash4(i + float4(1, 0, 0, 1));
    float n0101 = NX_ProcHash4(i + float4(0, 1, 0, 1));
    float n1101 = NX_ProcHash4(i + float4(1, 1, 0, 1));
    float n0011 = NX_ProcHash4(i + float4(0, 0, 1, 1));
    float n1011 = NX_ProcHash4(i + float4(1, 0, 1, 1));
    float n0111 = NX_ProcHash4(i + float4(0, 1, 1, 1));
    float n1111 = NX_ProcHash4(i + 1);
    float z0 = lerp(lerp(lerp(n0000, n1000, f.x), lerp(n0100, n1100, f.x), f.y),
        lerp(lerp(n0010, n1010, f.x), lerp(n0110, n1110, f.x), f.y), f.z);
    float z1 = lerp(lerp(lerp(n0001, n1001, f.x), lerp(n0101, n1101, f.x), f.y),
        lerp(lerp(n0011, n1011, f.x), lerp(n0111, n1111, f.x), f.y), f.z);
    return lerp(z0, z1, f.w);
}

float NX_Musgrave(float3 p, int octaves, float lacunarity, float gain, int mode)
{
    int count = clamp(octaves, 1, 8);
    float frequency = 1.0, amplitude = 1.0, total = 0.0, weight = 0.0;
    for (int octave = 0; octave < 8; octave++)
    {
        if (octave >= count) break;
        float n = NX_Noise3(p * frequency);
        float signal = mode == 1 ? 1.0 - abs(n * 2.0 - 1.0) : mode == 2 ? abs(n * 2.0 - 1.0) : n;
        total += signal * amplitude;
        weight += amplitude;
        frequency *= max(lacunarity, 0.0001);
        amplitude *= saturate(gain);
    }
    return weight > 0.0 ? saturate(total / weight) : 0.0;
}

float NX_Voronoi(float3 p, float randomness, int dimensions)
{
    int dims = dimensions == 2 ? 2 : 3;
    float3 cell = floor(p), local = frac(p);
    float nearest = 1e9;
    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (dims == 2 && z != 0) continue;
                float3 offset = float3(x, y, z);
                float jitter = saturate(randomness);
                float3 feature = 0.5 + jitter * (float3(
                    NX_ProcHash3(cell + offset + 17.0),
                    NX_ProcHash3(cell + offset + 43.0),
                    NX_ProcHash3(cell + offset + 71.0)) - 0.5);
                float3 delta = offset + feature - local;
                if (dims == 2) delta.z = 0.0;
                nearest = min(nearest, dot(delta, delta));
            }
        }
    }
    return saturate(sqrt(nearest) / (0.5 * sqrt((float)dims)));
}

float NX_Checker(float3 p, int dimensions)
{
    float sum = floor(p.x) + floor(p.y) + (dimensions == 3 ? floor(p.z) : 0.0);
    return frac(sum * 0.5) * 2.0;
}

float NX_Wave(float3 p, int mode, int axis, float phase)
{
    float coordinate = axis == 1 ? p.y : axis == 2 ? p.z : p.x;
    float distance = mode == 1 ? length(p) : coordinate;
    return 0.5 + 0.5 * sin(distance + phase);
}
";
    }
}

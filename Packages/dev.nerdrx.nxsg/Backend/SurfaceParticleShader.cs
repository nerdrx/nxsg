using System;
using System.Globalization;

namespace NXSG.Backend
{
    // Geometry emission keeps particles attached to the mesh that owns the material.
    internal static class SurfaceParticleShader
    {
        public static string Pass(string mask, string color, string emission, string opacity, string time, string density, string emissionRate, string size, string lifetime, string speed, string gravity, string spread, int blendMode, bool sourceUV)
        {
            var blend = blendMode == 1 ? "One" : "OneMinusSrcAlpha";
            double.TryParse(emissionRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var requestedRate);
            double.TryParse(lifetime, NumberStyles.Float, CultureInfo.InvariantCulture, out var requestedLife);
            var level = (int)Math.Min(64, Math.Max(1, Math.Ceiling(Math.Sqrt(Math.Max(0, requestedRate) * Math.Max(.0001, requestedLife) / 4))));
            // Triangle tessellators use concentric rings. Count is an estimate across APIs;
            // the requested rate is distributed across those microtriangles.
            var subdivisions = level == 1 ? 1 : (3 * level * level - level % 2) / 2;
            var tessellated = level > 1;
            return @"
Pass {
Name ""SurfaceParticles""
Tags { ""LightMode""=""Always"" }
Cull Off
ZWrite Off
Blend SrcAlpha " + blend + @"
CGPROGRAM
#pragma target " + (tessellated ? "4.6" : "4.0") + @"
" + (tessellated ? "#pragma hull hullEmit\n#pragma domain domainEmit" : "") + @"
#pragma vertex vertEmit
#pragma geometry geomEmit
#pragma fragment fragEmit
#pragma multi_compile_instancing

float NX_SurfaceParticleHash(float x)
{
    return frac(sin(x * 12.9898 + 78.233) * 43758.5453);
}

NXInput vertEmit(NXApp v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    NXInput input = NX_Make(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(input);
    return input;
}

" + (tessellated ? Tessellation(level) : "") + @"
[maxvertexcount(16)]
void geomEmit(triangle NXInput tri[3], inout TriangleStream<NXInput> stream, uint primitiveId : SV_PrimitiveID)
{
    float sourceId = " + (tessellated ? "tri[0].sourceUV.x" : "(float)primitiveId") + @";
    float seed = (float)primitiveId + dot(tri[0].uv + tri[1].uv + tri[2].uv,float2(17.3,41.7));
    float h0 = NX_SurfaceParticleHash(sourceId + 1.17);
    float densityGate = h0 < saturate(" + density + @") ? 1.0 : 0.0;
    float life = max(" + lifetime + @", 0.0001);
    float effectiveRate = min(max(" + emissionRate + @", 0.0) / " + subdivisions + @".0, 4.0 / life);
    float rateVisible = step(0.000001, effectiveRate);
    for (int slot = 0; slot < 4; slot++)
    {
        float h1 = NX_SurfaceParticleHash(seed + 11.31 + slot * 71.13);
        float h2 = NX_SurfaceParticleHash(seed + 23.71 + slot * 83.17);
        if (h1 + h2 > 1.0) { h1 = 1.0 - h1; h2 = 1.0 - h2; }
        float3 bary = float3(1.0 - h1 - h2, h1, h2);
        NXInput input = tri[0];
        input.uv = tri[0].uv * bary.x + tri[1].uv * bary.y + tri[2].uv * bary.z;
        input.uv1 = tri[0].uv1 * bary.x + tri[1].uv1 * bary.y + tri[2].uv1 * bary.z;
        input.uv2 = tri[0].uv2 * bary.x + tri[1].uv2 * bary.y + tri[2].uv2 * bary.z;
        input.uv3 = tri[0].uv3 * bary.x + tri[1].uv3 * bary.y + tri[2].uv3 * bary.z;
        input.ws = tri[0].ws * bary.x + tri[1].ws * bary.y + tri[2].ws * bary.z;
        input.n = normalize(tri[0].n * bary.x + tri[1].n * bary.y + tri[2].n * bary.z);
        input.local = tri[0].local * bary.x + tri[1].local * bary.y + tri[2].local * bary.z;
        input.originalWs = tri[0].originalWs * bary.x + tri[1].originalWs * bary.y + tri[2].originalWs * bary.z;
        input.originalLocal = tri[0].originalLocal * bary.x + tri[1].originalLocal * bary.y + tri[2].originalLocal * bary.z;
        input.tangent = tri[0].tangent * bary.x + tri[1].tangent * bary.y + tri[2].tangent * bary.z;
        input.bitangent = tri[0].bitangent * bary.x + tri[1].bitangent * bary.y + tri[2].bitangent * bary.z;
        input.color = tri[0].color * bary.x + tri[1].color * bary.y + tri[2].color * bary.z;
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float particleTime = " + time + @";
        float particleMask = saturate(" + mask + @");
        float basePhase = NX_SurfaceParticleHash(seed + 37.19);
        float ageSeconds = frac(particleTime * effectiveRate / 4.0 + basePhase - slot / 4.0) * 4.0 / max(effectiveRate, 0.000001);
        float active = densityGate * rateVisible * step(0.000001, particleMask) * (ageSeconds < life ? 1.0 : 0.0);
        float t = min(ageSeconds, life);
        float normalizedAge = saturate(ageSeconds / life);
        float3 localNormal = normalize(mul(input.n, (float3x3)unity_ObjectToWorld));
        float3 randomDirection = normalize(float3(
            NX_SurfaceParticleHash(seed + 41.7 + slot * 61.3) * 2.0 - 1.0,
            NX_SurfaceParticleHash(seed + 53.9 + slot * 67.1) * 2.0 - 1.0,
            NX_SurfaceParticleHash(seed + 67.2 + slot * 73.7) * 2.0 - 1.0));
        float3 localMotion = localNormal * (" + speed + @" * t) + float3(0.0, 0.5 * " + gravity + @" * t * t, 0.0) + randomDirection * (" + spread + @" * t);
        float3 worldCenter = mul(unity_ObjectToWorld, float4(input.local + localMotion, 1.0)).xyz;
        float objectScale = max(length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)), max(length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)), length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22))));
        float halfSize = 0.5 * max(0.0, " + size + @") * objectScale * active;
        float3 right = normalize(float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20));
        float3 up = normalize(float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21));
        float3 corners[4] = { float3(-1,-1,0), float3(1,-1,0), float3(-1,1,0), float3(1,1,0) };
        float2 spriteUV[4] = { float2(0,0), float2(1,0), float2(0,1), float2(1,1) };
        float fade = smoothstep(0.0, 0.1, normalizedAge) * (1.0 - smoothstep(0.9, 1.0, normalizedAge));
        for (int i = 0; i < 4; i++)
        {
            NXInput corner = input;
            float3 worldPosition = worldCenter + right * corners[i].x * halfSize + up * corners[i].y * halfSize;
            corner.pos = UnityWorldToClipPos(worldPosition);
            corner.ws = worldPosition;
            corner.local = mul(unity_WorldToObject, float4(worldPosition, 1.0)).xyz;
            corner.sourceUV = input.uv;
            corner.uv = spriteUV[i];
            corner.color.a = input.color.a * fade * particleMask * active;
            corner.n = normalize(_WorldSpaceCameraPos - worldPosition);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(corner);
            stream.Append(corner);
        }
        stream.RestartStrip();
    }
}

float4 fragEmit(NXInput input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 circlePosition = input.uv * 2.0 - 1.0;
    float circle = saturate(1.0 - dot(circlePosition, circlePosition));
    circle *= circle;
    clip(circle - 0.001);
    float particleOpacity = " + opacity + @";
    " + (sourceUV ? "input.uv = input.sourceUV;" : "") + @"
    float4 c = (" + color + @") * _Color * input.color;
    float3 e = (" + emission + @").rgb * input.color.rgb;
    float alpha = saturate(c.a * particleOpacity * circle);
    return float4(c.rgb + e, alpha);
}
ENDCG
}
";
        }

        static string Tessellation(int level)
        {
            var code = @"
struct NXParticleTess { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; float sourceId : TEXCOORD0; };
NXParticleTess particleFactors(InputPatch<NXInput,3> patch, uint patchId : SV_PrimitiveID)
{
    NXParticleTess o; o.edge[0]=o.edge[1]=o.edge[2]=o.inside=" + level + @".0; o.sourceId=(float)patchId; return o;
}
[domain(""tri"")]
[partitioning(""integer"")]
[outputtopology(""triangle_cw"")]
[outputcontrolpoints(3)]
[patchconstantfunc(""particleFactors"")]
NXInput hullEmit(InputPatch<NXInput,3> patch,uint id : SV_OutputControlPointID) { return patch[id]; }
[domain(""tri"")]
NXInput domainEmit(NXParticleTess factors,const OutputPatch<NXInput,3> patch,float3 bary : SV_DomainLocation)
{
    NXInput o=patch[0];
";
            foreach(var field in new[]{"uv","uv1","uv2","uv3","ws","n","local","originalWs","originalLocal","tangent","bitangent","color"})
                code += "o." + field + "=patch[0]." + field + "*bary.x+patch[1]." + field + "*bary.y+patch[2]." + field + "*bary.z;\n";
            return code + "o.pos=UnityWorldToClipPos(o.ws); o.sourceUV=float2(factors.sourceId,0); return o; }\n";
        }
    }
}

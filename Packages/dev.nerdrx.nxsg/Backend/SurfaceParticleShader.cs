namespace NXSG.Backend
{
    // Geometry emission keeps particles attached to the mesh that owns the material.
    internal static class SurfaceParticleShader
    {
        public static string Pass(string mask, string color, string emission, string opacity, string time, string density, string size, string lifetime, string speed, string gravity, string spread, int blendMode, bool sourceUV)
        {
            var blend = blendMode == 1 ? "One" : "OneMinusSrcAlpha";
            return @"
Pass {
Name ""SurfaceParticles""
Tags { ""LightMode""=""Always"" }
Cull Off
ZWrite Off
Blend SrcAlpha " + blend + @"
CGPROGRAM
#pragma target 4.0
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

[maxvertexcount(4)]
void geomEmit(triangle NXInput tri[3], inout TriangleStream<NXInput> stream, uint primitiveId : SV_PrimitiveID)
{
    float h0 = NX_SurfaceParticleHash((float)primitiveId + 1.17);
    float densityGate = h0 < saturate(" + density + @") ? 1.0 : 0.0;

    float h1 = NX_SurfaceParticleHash((float)primitiveId + 11.31);
    float h2 = NX_SurfaceParticleHash((float)primitiveId + 23.71);
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

    float particleMask = saturate(" + mask + @");
    // Keep a degenerate output for invisible particles: Unity OpenGL can lose the
    // geometry output declaration when constant-zero masks remove every Append.
    float visible = densityGate * step(0.000001, particleMask);
    float particleTime = " + time + @";
    float life = max(" + lifetime + @", 0.0001);
    float age = frac(particleTime / life + NX_SurfaceParticleHash((float)primitiveId + 37.19));
    float t = age * life;
    float3 localNormal = normalize(mul(input.n, (float3x3)unity_ObjectToWorld));
    float3 randomDirection = normalize(float3(
        NX_SurfaceParticleHash((float)primitiveId + 41.7) * 2.0 - 1.0,
        NX_SurfaceParticleHash((float)primitiveId + 53.9) * 2.0 - 1.0,
        NX_SurfaceParticleHash((float)primitiveId + 67.2) * 2.0 - 1.0));
    float3 localMotion = localNormal * (" + speed + @" * t) + float3(0.0, 0.5 * " + gravity + @" * t * t, 0.0) + randomDirection * (" + spread + @" * t);
    float3 worldCenter = mul(unity_ObjectToWorld, float4(input.local + localMotion, 1.0)).xyz;
    float objectScale = max(length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)), max(length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)), length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22))));
    float halfSize = 0.5 * max(0.0, " + size + @") * objectScale * visible;
    float3 right = normalize(float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20));
    float3 up = normalize(float3(UNITY_MATRIX_I_V._m01, UNITY_MATRIX_I_V._m11, UNITY_MATRIX_I_V._m21));
    float3 corners[4] = { float3(-1,-1,0), float3(1,-1,0), float3(-1,1,0), float3(1,1,0) };
    float2 spriteUV[4] = { float2(0,0), float2(1,0), float2(0,1), float2(1,1) };
    float fade = smoothstep(0.0, 0.1, age) * (1.0 - smoothstep(0.9, 1.0, age));
    for (int i = 0; i < 4; i++)
    {
        NXInput output = input;
        float3 worldPosition = worldCenter + right * corners[i].x * halfSize + up * corners[i].y * halfSize;
        output.pos = UnityWorldToClipPos(worldPosition);
        output.ws = worldPosition;
        output.local = mul(unity_WorldToObject, float4(worldPosition, 1.0)).xyz;
        output.originalWs = input.originalWs;
        output.originalLocal = input.originalLocal;
        output.sourceUV = input.uv;
        output.uv = spriteUV[i];
        output.color.a = input.color.a * fade * particleMask;
        output.n = normalize(_WorldSpaceCameraPos - worldPosition);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        stream.Append(output);
    }
    stream.RestartStrip();
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
    }
}

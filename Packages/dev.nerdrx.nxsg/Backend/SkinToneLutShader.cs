namespace NXSG.Backend
{
    public static class SkinToneLutShader
    {
        public const string Hlsl = @"
float4 NXSG_SkinToneLut(float4 baseColor,float pigment,float mask,float strength,sampler2D lut,float4 texelSize) {
    float luminance=dot(max(baseColor.rgb,0),float3(.2126,.7152,.0722));
    float2 uv=saturate(float2(luminance,pigment));
    uv=lerp(texelSize.xy*.5,1-texelSize.xy*.5,uv);
    float3 tone=tex2D(lut,uv).rgb;
    float blend=saturate(mask)*saturate(strength);
    return float4(lerp(baseColor.rgb,tone,blend),baseColor.a);
}
";

        public static string Expression(string baseColor, string pigment, string mask, string strength, string texture)
        {
            return "NXSG_SkinToneLut(" + baseColor + "," + pigment + "," + mask + "," + strength + "," + texture + "," + texture + "_TexelSize)";
        }
    }
}

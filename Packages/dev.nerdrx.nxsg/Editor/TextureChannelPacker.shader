Shader "Hidden/NXSG/PackChannels"
{
    Properties
    {
        _Source0 ("Red source", 2D) = "white" {}
        _Source1 ("Green source", 2D) = "white" {}
        _Source2 ("Blue source", 2D) = "white" {}
        _Source3 ("Alpha source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Source0, _Source1, _Source2, _Source3;
            float _Channel0, _Channel1, _Channel2, _Channel3;
            float Pick(float4 sample, float channel)
            {
                return channel < .5 ? sample.r : channel < 1.5 ? sample.g : channel < 2.5 ? sample.b : sample.a;
            }
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                return float4(Pick(tex2D(_Source0, uv), _Channel0),
                              Pick(tex2D(_Source1, uv), _Channel1),
                              Pick(tex2D(_Source2, uv), _Channel2),
                              Pick(tex2D(_Source3, uv), _Channel3));
            }
            ENDCG
        }
    }
}

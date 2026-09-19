Shader "Hidden/NXSG/TextureChannelPreview"
{
    Properties { _MainTex ("Texture",2D)="white" {} _Channel ("Channel",Float)=0 _DecodeNormal ("Decode",Float)=0 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Channel;
            float _DecodeNormal;
            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                if (_DecodeNormal > 0.5 && _Channel < 0.5) c.rgb = UnpackNormal(c) * .5 + .5;
                if (_Channel < 0.5) return c;
                float v = _Channel < 1.5 ? c.r : (_Channel < 2.5 ? c.g : (_Channel < 3.5 ? c.b : c.a));
                return fixed4(v, v, v, 1);
            }
            ENDCG
        }
    }
}

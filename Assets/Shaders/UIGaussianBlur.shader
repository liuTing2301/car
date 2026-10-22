Shader "DigitalTwin/UIGaussianBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurSize ("Blur Size (texels)", Float) = 12
        _Direction ("Direction", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlurSize;
            float4 _Direction;

            fixed4 frag (v2f_img i) : SV_Target
            {
                // 9 采样线性高斯（横 / 竖各一遍）
                float2 dir = _Direction.xy * _MainTex_TexelSize.xy * _BlurSize;
                float2 uv = i.uv;

                fixed4 col = tex2D(_MainTex, uv) * 0.2270270270;
                col += tex2D(_MainTex, uv + dir * 1.3846153846) * 0.3162162162;
                col += tex2D(_MainTex, uv - dir * 1.3846153846) * 0.3162162162;
                col += tex2D(_MainTex, uv + dir * 3.2307692308) * 0.0702702703;
                col += tex2D(_MainTex, uv - dir * 3.2307692308) * 0.0702702703;

                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}

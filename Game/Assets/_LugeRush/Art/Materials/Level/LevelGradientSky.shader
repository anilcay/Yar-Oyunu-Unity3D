Shader "LugeRush/Level Gradient Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.3, 0.55, 0.85, 1)
        _HorizonColor ("Horizon", Color) = (0.8, 0.9, 0.95, 1)
        _GroundColor ("Below horizon", Color) = (0.7, 0.75, 0.75, 1)
        _Exponent ("Gradient exponent", Float) = 0.6
        _SunDirection ("Sun direction", Vector) = (0, 0.5, 0.8, 0)
        _SunColor ("Sun colour", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Sun disc size", Float) = 0.9992
        _GlowPower ("Sun glow power", Float) = 24
        _GlowStrength ("Sun glow strength", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _TopColor, _HorizonColor, _GroundColor, _SunColor, _SunDirection;
            float _Exponent, _SunSize, _GlowPower, _GlowStrength;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.dir = vertex.xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 col = d.y >= 0.0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(d.y), _Exponent))
                    : lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-d.y * 6.0));
                float s = saturate(dot(d, normalize(_SunDirection.xyz)));
                col += _SunColor.rgb * pow(s, _GlowPower) * _GlowStrength;
                col = lerp(col, _SunColor.rgb * 1.6, smoothstep(_SunSize, _SunSize + 0.0004, s));
                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

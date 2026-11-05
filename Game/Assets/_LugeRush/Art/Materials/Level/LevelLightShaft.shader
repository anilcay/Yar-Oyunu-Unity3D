Shader "LugeRush/Level Light Shaft"
{
    // Additive sun beam card. UV.x across the beam, UV.y along it (0 = top). Fades near the camera and far away.
    Properties
    {
        _Color ("Colour", Color) = (1, 0.95, 0.75, 0.2)
        _NearFade ("Near fade distance", Float) = 8
        _FarFade ("Far fade distance", Float) = 150
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _NearFade, _FarFade;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; };

            V vert(A a)
            {
                V o;
                float3 w = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(w);
                o.uv = a.uv;
                o.dist = distance(w, _WorldSpaceCameraPos);
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);
                across *= across;
                float along = smoothstep(0.0, 0.2, i.uv.y) * (1.0 - smoothstep(0.55, 1.0, i.uv.y));
                float fade = saturate((i.dist - _NearFade) / _NearFade) * (1.0 - saturate(i.dist / _FarFade));
                return float4(_Color.rgb * _Color.a * across * along * fade, 0.0);
            }
            ENDHLSL
        }
    }
}

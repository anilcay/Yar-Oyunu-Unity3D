// Stylised semi-transparent water for Luge Rush (URP). No UVs required: the flow pattern is procedural
// value noise in world space (XZ for streams/pools, Y-dominant on steep faces so waterfalls "fall").
// Hand-written HLSL equivalent of the planned Shader Graph (kept text based so it can be generated/reviewed).
Shader "LugeRush/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.40, 0.72, 0.85, 0.55)
        _DeepColor ("Deep Color", Color) = (0.12, 0.38, 0.55, 0.85)
        _FoamColor ("Foam Color", Color) = (0.93, 0.97, 1.0, 1.0)
        _NoiseScale ("Noise Scale (1/m)", Float) = 0.6
        _FlowSpeed ("Flow Speed (m/s)", Float) = 0.8
        _FallSpeed ("Waterfall Speed (m/s)", Float) = 3.0
        _FlowDirection ("Flow Direction (world XZ)", Vector) = (0, 0, 1, 0)
        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.72
        _FoamSoftness ("Foam Softness", Range(0.001, 0.5)) = 0.08
        _Smoothness ("Smoothness", Range(0, 1)) = 0.85
        _FresnelPower ("Fresnel Power", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float _NoiseScale;
                float _FlowSpeed;
                float _FallSpeed;
                float4 _FlowDirection;
                half _FoamThreshold;
                half _FoamSoftness;
                half _Smoothness;
                half _FresnelPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float a = 0.5, s = 0.0;
                for (int o = 0; o < 3; o++) { s += a * ValueNoise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return s / 0.875;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float t = _Time.y;
                float steep = saturate((1.0 - abs(n.y)) * 1.6); // 0 = flat pool, 1 = vertical fall

                float2 dir = normalize(_FlowDirection.xz + 1e-5);
                float3 flatP = float3(i.positionWS.x - dir.x * _FlowSpeed * t, 0.0, i.positionWS.z - dir.y * _FlowSpeed * t) * _NoiseScale;
                flatP.y = t * 0.15;
                float3 fallP = float3(i.positionWS.x + i.positionWS.z, (i.positionWS.y + _FallSpeed * t) * 0.35, t * 0.2) * _NoiseScale * float3(1.0, 1.0, 1.0);
                fallP.x *= 2.5; // streaks
                float nFlat = Fbm(flatP);
                float nFall = Fbm(fallP);
                float noise = lerp(nFlat, nFall, steep);

                // Perturb the normal a little for moving highlights.
                float3 pn = normalize(n + float3(nFlat - 0.5, 0.0, nFall - 0.5) * 0.35);

                half3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, noise);
                half alpha = lerp(_DeepColor.a, _ShallowColor.a, noise);
                half foam = smoothstep(_FoamThreshold - _FoamSoftness, _FoamThreshold + _FoamSoftness, noise + steep * 0.25);
                baseCol = lerp(baseCol, _FoamColor.rgb, foam);
                alpha = max(alpha, foam * _FoamColor.a);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = saturate(dot(pn, light.direction)) * 0.6 + 0.4;
                half3 ambient = SampleSH(pn);
                half3 lit = baseCol * (light.color * ndl * light.shadowAttenuation + ambient);

                float3 h = SafeNormalize(light.direction + viewDir);
                half spec = pow(saturate(dot(pn, h)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness;
                half fres = pow(1.0 - saturate(dot(pn, viewDir)), _FresnelPower);
                lit += light.color * spec * light.shadowAttenuation + ambient * fres * 0.5;
                alpha = saturate(alpha + fres * 0.3);

                lit = MixFog(lit, i.fogFactor);
                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

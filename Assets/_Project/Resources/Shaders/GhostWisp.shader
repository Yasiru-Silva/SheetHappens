// Ghost body for Sheet Happens (URP).
// Keeps the ghost's own texture and colour, adds a glowing rim, and dissolves the lower part of the body into
// drifting, wispy smoke: the fade height is set every frame by GhostDissolve (_FadeY0 = fully gone, _FadeY1 = solid).
Shader "SheetHappens/GhostWisp"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _RimColor ("Rim Glow", Color) = (0.8, 0.8, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.95
        _FadeY0 ("Fade Bottom (world Y)", Float) = -10000
        _FadeY1 ("Fade Top (world Y)", Float) = -9999
        _Glow ("Glow", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _RimColor;
            half _Opacity;
            float _FadeY0;
            float _FadeY1;
            half _Glow;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float fogFactor : TEXCOORD3;
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // 1 = solid body, 0 = gone: the bottom dissolves into streaky wisps that flow upwards over time.
        float SmokeFade(float3 positionWS)
        {
            float t = _Time.y;
            float2 q = float2(dot(positionWS.xz, float2(3.1, 2.3)), positionWS.y * 3.5 - t * 1.3);
            float wisps = ValueNoise(q) * 0.6 + ValueNoise(q * 2.7 + 11.0) * 0.4;
            float h = (positionWS.y - _FadeY0) / max(0.001, _FadeY1 - _FadeY0); // 0 at the bottom, 1 where it is solid
            float a = saturate(h * 1.25 - 0.1 + (wisps - 0.5) * 0.9);
            return a * a * (3.0 - 2.0 * a);
        }

        Varyings vert(Attributes v)
        {
            Varyings o;
            VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS = pos.positionCS;
            o.positionWS = pos.positionWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.fogFactor = ComputeFogFactor(pos.positionCS.z);
            return o;
        }
        ENDHLSL

        // Depth of the solid part first, so the ghost's own face and eyes always sort correctly.
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            half4 fragDepth(Varyings i) : SV_Target
            {
                clip(SmokeFade(i.positionWS) - 0.6);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "GhostForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Soft lighting: ambient + a little main light + a self-lit floor so ghosts read in the dark.
                Light mainLight = GetMainLight();
                half3 light = SampleSH(n) + mainLight.color * saturate(dot(n, mainLight.direction)) * 0.5h + 0.35h;
                half rim = pow(1.0h - saturate(dot(n, viewDir)), 2.5h);
                half3 col = tex.rgb * light + (_EmissionColor.rgb + _RimColor.rgb * rim * 0.8h) * _Glow;

                float a = SmokeFade(i.positionWS);
                // The fading part glows a little in the ghost's colour, like smoke catching its light.
                col += _RimColor.rgb * (1.0 - a) * 0.35 * _Glow;

                col = MixFog(col, i.fogFactor);
                return half4(col, tex.a * _Opacity * a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

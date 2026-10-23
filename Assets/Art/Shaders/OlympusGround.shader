// 기지 지면 — 원래 지면(황폐/녹지 체커 또는 풀) 위에, 건물이 선 자리만 흙바닥으로 바꾼다.
//
// 건물 에셋에는 바닥을 넣지 않는다(OlympusArt/background/art-direction.md). 대신 건물의
// 풋프린트(정사각형·직사각형 칸 범위)만큼 이 셰이더가 지면을 흙으로 칠한다.
//
// 흙 위치는 머티리얼이 아니라 전역 텍스처 _OlympusDirtMask가 들고 있다 — 칸 하나당 1픽셀,
// 건물이 덮는 칸이 1. TerritoryGround.SetDirtPatches가 건물 상태가 바뀔 때만 다시 굽는다.
// 전역으로 두는 이유: 황폐/녹지 두 머티리얼이 같은 마스크를 봐야 하고, 머티리얼 에셋에
// 런타임 텍스처를 꽂으면 에디터에서 에셋이 더러워진다.
//
// 가장자리: 마스크를 바이리니어로 읽으면 칸 경계에서 정확히 0.5가 된다. 그 0.5 등고선을
// 문턱으로 자르면 풋프린트와 딱 맞는 사각형이 나오고, 문턱에 저주파 노이즈를 더해
// 가장자리만 살짝 흙이 번진 느낌을 준다. 텍스처 샘플 2번 + 노이즈 몇 줄이라 모바일에서도 싸다.
Shader "Olympus/Ground"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (지면)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Dirt)]
        _DirtColor ("흙 색", Color) = (0.55, 0.42, 0.29, 1)
        _DirtColorDark ("흙 색 (어두운 얼룩)", Color) = (0.46, 0.34, 0.23, 1)
        _DirtMap ("흙 텍스처 (선택, 월드 반복)", 2D) = "white" {}
        _DirtTiling ("흙 텍스처 반복 (칸당)", Float) = 0.5
        _DirtBlotchScale ("얼룩 크기 (칸당 주파수)", Float) = 0.6

        [Header(Edge)]
        _DirtGrow ("풋프린트 밖으로 번짐 (칸)", Range(-0.5, 0.5)) = 0.15
        _DirtEdgeSoftness ("가장자리 부드러움", Range(0.001, 0.3)) = 0.06
        _DirtEdgeNoise ("가장자리 울퉁불퉁", Range(0, 0.4)) = 0.12
        _DirtEdgeNoiseScale ("가장자리 노이즈 주파수 (칸당)", Float) = 1.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _DirtColor;
            half4 _DirtColorDark;
            float4 _DirtMap_ST;
            float _DirtTiling;
            float _DirtBlotchScale;
            float _DirtGrow;
            float _DirtEdgeSoftness;
            float _DirtEdgeNoise;
            float _DirtEdgeNoiseScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);
            // 전역 — TerritoryGround가 Shader.SetGlobal*로 넣는다. 안 넣었으면 흙 없음.
            TEXTURE2D(_OlympusDirtMask);    SAMPLER(sampler_OlympusDirtMask);
            float4 _OlympusDirtMaskRect;    // xy = 마스크가 덮는 월드 XZ 최소 코너, zw = 1/월드 크기
            float _OlympusDirtCellSize;     // 칸 하나의 월드 크기 (노이즈·번짐을 칸 단위로)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            // 값 노이즈 — 텍스처 없이 얼룩·가장자리 흔들림을 만든다.
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
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half3 DirtAlbedo(float2 cellXZ)
            {
                // 얼룩: 큰 덩어리 두 옥타브로 단색 흙이 밋밋하지 않게.
                float blotch = ValueNoise(cellXZ * _DirtBlotchScale) * 0.65
                             + ValueNoise(cellXZ * _DirtBlotchScale * 2.7 + 17.0) * 0.35;
                half3 dirt = lerp(_DirtColorDark.rgb, _DirtColor.rgb, smoothstep(0.3, 0.7, blotch));
                half3 tex = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, cellXZ * _DirtTiling).rgb;
                return dirt * tex;
            }

            half DirtCoverage(float3 positionWS, float2 cellXZ)
            {
                if (_OlympusDirtMaskRect.z <= 0.0)
                    return 0;

                float2 maskUV = (positionWS.xz - _OlympusDirtMaskRect.xy) * _OlympusDirtMaskRect.zw;
                // 마스크 밖은 흙 없음 — 클램프로 가장자리 칸이 기지 밖까지 번지지 않게.
                if (any(maskUV < 0.0) || any(maskUV > 1.0))
                    return 0;

                // 바이리니어: 칸 중심=텍셀 중심이라 칸 경계에서 값이 정확히 0.5가 된다.
                float m = SAMPLE_TEXTURE2D(_OlympusDirtMask, sampler_OlympusDirtMask, maskUV).r;

                // 칸 경계 근처에서 m은 칸 1개 폭에 걸쳐 0→1로 선형 변한다. 그래서 문턱을
                // 0.5에서 g만큼 내리면 가장자리가 대략 g칸 밖으로 나간다.
                float n = (ValueNoise(cellXZ * _DirtEdgeNoiseScale) - 0.5) * 2.0 * _DirtEdgeNoise;
                float threshold = 0.5 - _DirtGrow + n;
                return (half)smoothstep(threshold - _DirtEdgeSoftness, threshold + _DirtEdgeSoftness, m);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 baseAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;

                float cell = max(_OlympusDirtCellSize, 1e-4);
                float2 cellXZ = input.positionWS.xz / cell;

                half dirt = DirtCoverage(input.positionWS, cellXZ);
                half3 albedo = lerp(baseAlbedo, DirtAlbedo(cellXZ), dirt);

                // 조명 — 무광 지면이라 램버트 + 구면조화 환경광이면 URP/Lit(Smoothness 0)과 같은 톤이다.
                half3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 ambient = SampleSH(normalWS);
                half occlusion = 1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
                    occlusion = ao.indirectAmbientOcclusion;
                    mainLight.color *= ao.directAmbientOcclusion;
                #endif

                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 lit = albedo * (ambient * occlusion
                          + mainLight.color * (ndotl * mainLight.shadowAttenuation * mainLight.distanceAttenuation));

                lit = MixFog(lit, input.fogFactor);
                return half4(lit, 1);
            }
            ENDHLSL
        }

        // SSAO·깊이 텍스처가 지면을 알아야 해서 둔다. 지면은 평면이라 노멀은 버텍스 그대로.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
            };

            DNVaryings DNVert(DNAttributes input)
            {
                DNVaryings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DNFrag(DNVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

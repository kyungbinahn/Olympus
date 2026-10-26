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
        _BaseWorldTiling ("지면 텍스처 한 장 = 몇 칸 (0이면 메쉬 UV — 칸당 0~1, 체커용)", Float) = 0
        _BaseMacroStrength ("지면 큰 얼룩 밝기 흔들림 (반복 티 가리기)", Range(0, 0.3)) = 0
        _BaseMacroScale ("큰 얼룩 크기 (칸당 주파수)", Float) = 0.07

        [Header(Restored)]
        _RestoredMap ("녹지 텍스처 (복구된 칸, 같은 월드 타일링)", 2D) = "white" {}
        _RestoredColor ("녹지 색", Color) = (1, 1, 1, 1)
        _RestoreEdgeSoftness ("녹지 경계 부드러움", Range(0.01, 0.5)) = 0.18
        _RestoreEdgeNoise ("녹지 경계 울퉁불퉁", Range(0, 0.4)) = 0.14
        _RestoreEdgeNoiseScale ("녹지 경계 노이즈 주파수 (칸당)", Float) = 0.45

        [Header(Dirt)]
        _DirtColor ("흙 색", Color) = (0.55, 0.42, 0.29, 1)
        _DirtColorDark ("흙 색 (어두운 얼룩)", Color) = (0.46, 0.34, 0.23, 1)
        _DirtMap ("흙 텍스처 (선택, 월드 반복)", 2D) = "white" {}
        _DirtTiling ("흙 텍스처 반복 (칸당)", Float) = 0.5
        _DirtBlotchScale ("얼룩 크기 (칸당 주파수)", Float) = 0.6

        [Header(Edge)]
        // 번짐 + 부드러움 + 울퉁불퉁은 합쳐서 1칸 미만이어야 한다 — 주변 3×3 칸만 보기 때문.
        _DirtGrow ("풋프린트 밖으로 번짐 (칸)", Range(-0.3, 0.45)) = 0.3
        _DirtEdgeSoftness ("가장자리 부드러움 (칸)", Range(0.01, 0.4)) = 0.3
        _DirtEdgeNoise ("가장자리 울퉁불퉁 (칸)", Range(0, 0.3)) = 0.22
        _DirtEdgeNoiseScale ("가장자리 노이즈 주파수 (칸당)", Float) = 0.9
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
            float _BaseWorldTiling;
            float _BaseMacroStrength;
            float _BaseMacroScale;
            float4 _RestoredMap_ST;
            half4 _RestoredColor;
            float _RestoreEdgeSoftness;
            float _RestoreEdgeNoise;
            float _RestoreEdgeNoiseScale;
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
            TEXTURE2D(_RestoredMap);        SAMPLER(sampler_RestoredMap);
            TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);
            // 전역 — TerritoryGround가 Shader.SetGlobal*로 넣는다. 안 넣었으면 흙·녹지 블렌딩 없음.
            // 두 마스크는 같은 격자 범위를 덮어서 사각형(Rect)을 같이 쓴다.
            TEXTURE2D(_OlympusDirtMask);    SAMPLER(sampler_OlympusDirtMask);
            TEXTURE2D(_OlympusRestoreMask); SAMPLER(sampler_OlympusRestoreMask);
            float4 _OlympusGroundMaskRect;  // xy = 마스크가 덮는 월드 XZ 최소 코너, zw = 1/월드 크기
            float _OlympusRestoreBlend;     // 1이면 녹지를 마스크로 섞는다 (메쉬가 서브메쉬로 안 나눈 상태)
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

            // 녹지 비율. 마스크는 칸당 1픽셀을 CPU에서 한 번 흐려 둔 것이라 0.5 등고선이 둥글게 나온다.
            // 거기에 저주파 노이즈로 문턱을 흔들어 원·사각형 모양이 그대로 드러나지 않게 한다.
            half RestoreCoverage(float3 positionWS, float2 cellXZ)
            {
                if (_OlympusGroundMaskRect.z <= 0.0 || _OlympusRestoreBlend <= 0.0)
                    return 0;

                float2 rawUV = (positionWS.xz - _OlympusGroundMaskRect.xy) * _OlympusGroundMaskRect.zw;
                float m = SAMPLE_TEXTURE2D(_OlympusRestoreMask, sampler_OlympusRestoreMask, saturate(rawUV)).r;

                // 기지 밖(바깥 지면 띠)은 마스크 가장자리 값이 클램프로 끝없이 늘어나므로, 기지 테두리에서
                // 1.5칸 안에 사라지게 한다 — 가장자리에 붙은 녹지는 살짝 번지고 끝난다.
                float cellWorld = _OlympusDirtCellSize > 0.0 ? _OlympusDirtCellSize : 1.0;
                float2 outsideCells = max(max(-rawUV, rawUV - 1.0), 0.0) / (_OlympusGroundMaskRect.zw * cellWorld);
                m *= saturate(1.0 - length(outsideCells) / 1.5);

                float n = (ValueNoise(cellXZ * _RestoreEdgeNoiseScale + 11.0) - 0.5) * 1.5
                        + (ValueNoise(cellXZ * _RestoreEdgeNoiseScale * 2.9 + 3.0) - 0.5) * 0.5;
                float threshold = 0.5 + n * _RestoreEdgeNoise;
                return (half)smoothstep(threshold - _RestoreEdgeSoftness, threshold + _RestoreEdgeSoftness, m);
            }

            // 흙 비율. 녹지처럼 부드럽게 섞되, 모양은 풋프린트 사각형을 지켜야 한다.
            //
            // 예전엔 마스크를 바이리니어로 읽고 0.5 등고선을 잘랐는데, 그 방식은 1×1 건물에서 값이
            // 가운데 한 점에서만 1이라 흙이 점처럼 줄어들고(부드럽게 하려고 문턱 폭을 넓히면 더 심해짐),
            // 녹지처럼 마스크를 흐리면 아예 사라진다. 그래서 주변 3×3 칸을 직접 읽어 "가장 가까운 흙 칸
            // 사각형까지의 거리"(안쪽이면 음수)를 재고, 그 거리로 가장자리를 만든다 — 크기와 상관없이
            // 풋프린트 + 번짐만큼의 둥근 사각형이 되고 부드러움은 칸 단위로 그대로 조절된다.
            half DirtCoverage(float3 positionWS, float2 cellXZ)
            {
                if (_OlympusGroundMaskRect.z <= 0.0)
                    return 0;

                float cell = _OlympusDirtCellSize > 0.0 ? _OlympusDirtCellSize : 1.0;
                float2 g = (positionWS.xz - _OlympusGroundMaskRect.xy) / cell;     // 마스크 최소 코너 기준 칸 좌표
                int2 size = (int2)round(1.0 / (_OlympusGroundMaskRect.zw * cell)); // 마스크 칸 수
                int2 c0 = (int2)floor(g);

                float dOut = 1.0;   // 가장 가까운 흙 칸까지 (밖에 있을 때)
                float dIn = 1.0;    // 가장 가까운 흙 아닌 칸까지 (흙 안에 있을 때)

                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 c = c0 + int2(dx, dy);
                        // 마스크 밖(기지 밖)은 흙 아님 — 가장자리 칸의 흙이 기지 밖까지는 번지지만 새로 생기진 않는다.
                        bool inside = all(c >= 0) && all(c < size);
                        float v = inside ? LOAD_TEXTURE2D(_OlympusDirtMask, clamp(c, 0, size - 1)).r : 0.0;

                        float2 q = abs(g - ((float2)c + 0.5)) - 0.5;
                        float d = length(max(q, 0.0));   // 이 칸 사각형까지의 거리, 안이면 0

                        if (v > 0.5)
                            dOut = min(dOut, d);
                        else
                            dIn = min(dIn, d);
                    }
                }

                float sd = dOut > 0.0 ? dOut : -dIn;

                float n = ((ValueNoise(cellXZ * _DirtEdgeNoiseScale) - 0.5) * 1.5
                         + (ValueNoise(cellXZ * _DirtEdgeNoiseScale * 2.9 + 5.0) - 0.5) * 0.5) * _DirtEdgeNoise;
                return (half)(1.0 - smoothstep(_DirtGrow - _DirtEdgeSoftness, _DirtGrow + _DirtEdgeSoftness, sd + n));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 전역은 TerritoryGround가 흙 패치를 넣을 때 들어온다. 그 전(에디터 씬 뷰 등)엔 0이라
                // 칸 크기 기본값 1로 본다 — 안 그러면 지면 텍스처가 점 크기로 반복된다.
                float cell = _OlympusDirtCellSize > 0.0 ? _OlympusDirtCellSize : 1.0;
                float2 cellXZ = input.positionWS.xz / cell;

                // 실제 지면 텍스처는 칸 여러 개에 한 장씩 월드 좌표로 깐다 — 메쉬 UV(칸당 0~1)로
                // 깔면 텍스처가 칸마다 반복돼 격자가 보인다. 0이면 예전처럼 메쉬 UV(체커).
                float2 baseUV = _BaseWorldTiling > 0.0
                    ? cellXZ / _BaseWorldTiling * _BaseMap_ST.xy + _BaseMap_ST.zw
                    : input.uv;
                half3 baseAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV).rgb * _BaseColor.rgb;
                // 녹지 — 같은 UV로 샘플해서 두 지면의 얼룩 크기가 맞는다. 체커 모드(메쉬 UV)에서는
                // 블렌딩 전역이 꺼져 있어 이 값이 0이다.
                // 분기 없이 항상 샘플한다 — 분기 안의 암묵 미분 샘플은 경계 픽셀에서 밉이 튄다.
                half restore = RestoreCoverage(input.positionWS, cellXZ);
                half3 restoredAlbedo = SAMPLE_TEXTURE2D(_RestoredMap, sampler_RestoredMap, baseUV).rgb * _RestoredColor.rgb;
                baseAlbedo = lerp(baseAlbedo, restoredAlbedo, restore);
                // 타일보다 훨씬 큰 밝기 얼룩 — 같은 무늬가 줄지어 보이는 걸 깬다.
                float macro = ValueNoise(cellXZ * _BaseMacroScale + 31.0) * 0.6
                            + ValueNoise(cellXZ * _BaseMacroScale * 2.3 + 7.0) * 0.4;
                baseAlbedo *= 1.0 + (macro - 0.5) * 2.0 * _BaseMacroStrength;

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

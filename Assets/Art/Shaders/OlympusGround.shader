// 기지 지면 — 원래 지면(황폐/녹지 체커 또는 풀) 위에, 건물이 선 자리만 흙바닥으로 바꾼다.
//
// 건물 에셋에는 바닥을 넣지 않는다(OlympusArt/background/art-direction.md). 대신 건물의
// 풋프린트(정사각형·직사각형 칸 범위)만큼 이 셰이더가 지면을 흙으로 칠한다.
//
// 흙 위치는 머티리얼이 아니라 전역 텍스처 _OlympusDirtMask가 들고 있다 — 건물 자리(돌린 사각형)까지의
// 거리장(DirtCoverage 주석). TerritoryGround.SetDirtPatches가 건물 상태가 바뀔 때만 다시 굽는다.
// 전역으로 두는 이유: 황폐/녹지 두 머티리얼이 같은 마스크를 봐야 하고, 머티리얼 에셋에
// 런타임 텍스처를 꽂으면 에디터에서 에셋이 더러워진다.
//
// 가장자리: 거리장의 0 등고선이 건물 자리 사각형이고, 번짐(_DirtGrow)만큼 밖으로 밀고 저주파 노이즈를 더해
// 가장자리만 살짝 흙이 번진 느낌을 준다. 텍스처 샘플 2번 + 노이즈 몇 줄이라 모바일에서도 싸다.
//
// 성 밖(Outside): 성벽 안팎 거리장(TerritoryWallView, 전역 _OlympusWallField*)으로 성벽 밖을 그을린 땅·용암 틈·연기로 바꾼다.
// 지금 용암은 노이즈(보로노이) 시안이다 — 아트 용암 텍스처가 오면 바꾼다(docs/hq1-5-frontline.md "성벽 2차").
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

        [Header(Outside the wall)]
        // 성 밖 — 그을린 땅 + 갈라진 틈의 용암 + 연기. 성벽 안팎 거리장은 TerritoryWallView가 전역으로 넣는다(없으면 효과 없음).
        _OutsideColor ("성 밖 땅 색", Color) = (0.2, 0.16, 0.14, 1)
        _OutsideColorDark ("성 밖 땅 색 (어두운 얼룩)", Color) = (0.07, 0.06, 0.06, 1)
        _OutsideAmount ("성 밖을 바꾸는 정도 (0 = 안과 같음)", Range(0, 1)) = 1
        _OutsideEdge ("성벽에서 완전히 바뀌기까지 (m)", Range(0.5, 12)) = 1.5
        _OutsideRuinDirt ("성 밖 폐허 자리 흙 밝기", Range(0, 1)) = 0.45
        [HDR] _LavaColor ("용암 색 (빛)", Color) = (2.6, 0.75, 0.12, 1)
        _LavaStrength ("용암 세기 (0 = 용암 없음, 어둡기만)", Range(0, 2)) = 1
        _LavaStart ("성벽에서 용암이 시작하는 거리 (m)", Range(0, 20)) = 4
        _LavaRamp ("용암이 다 드러나기까지 더 (m)", Range(0.5, 20)) = 8
        _LavaScale ("갈라짐 크기 (칸당 주파수)", Float) = 0.4
        _LavaCrackWidth ("틈 굵기", Range(0.01, 0.3)) = 0.05
        _LavaPoolAmount ("용암 웅덩이 양", Range(0, 1)) = 0.3
        _RoadHalfWidth ("성문~전선 길 반폭 (m, 용암 없음)", Range(0, 8)) = 2.5
        _SmokeColor ("연기 색", Color) = (0.1, 0.07, 0.07, 1)
        _SmokeStrength ("연기 짙기", Range(0, 1)) = 0.55
        _SmokeDistance ("연기가 다 짙어지는 거리 (m)", Range(2, 80)) = 28
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
            half4 _OutsideColor;
            half4 _OutsideColorDark;
            float _OutsideAmount;
            float _OutsideEdge;
            float _OutsideRuinDirt;
            half4 _LavaColor;
            float _LavaStrength;
            float _LavaStart;
            float _LavaRamp;
            float _LavaScale;
            float _LavaCrackWidth;
            float _LavaPoolAmount;
            float _RoadHalfWidth;
            half4 _SmokeColor;
            float _SmokeStrength;
            float _SmokeDistance;
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
            TEXTURE2D(_OlympusDirtMask);    SAMPLER(sampler_OlympusDirtMask);
            TEXTURE2D(_OlympusRestoreMask); SAMPLER(sampler_OlympusRestoreMask);
            float4 _OlympusGroundMaskRect;  // 녹지 마스크 — xy = 덮는 월드 XZ 최소 코너, zw = 1/월드 크기
            float4 _OlympusDirtFieldRect;   // 흙 거리장 — 같은 뜻, 기지보다 2칸씩 넓다
            float _OlympusRestoreBlend;     // 1이면 녹지를 마스크로 섞는다 (메쉬가 서브메쉬로 안 나눈 상태)
            float _OlympusDirtCellSize;     // 칸 하나의 월드 크기 (노이즈·번짐을 칸 단위로)
            // 성벽 안팎 거리장 — TerritoryWallView. 성벽이 넓어질 때 옛(Old)→새(New) 거리를 _OlympusWallBlend로 섞어 땅이 같이 넓어진다.
            TEXTURE2D(_OlympusWallFieldOld); SAMPLER(sampler_OlympusWallFieldOld);
            TEXTURE2D(_OlympusWallFieldNew);
            float4 _OlympusWallFieldRect;   // xy = 최소 코너, zw = 1/월드 크기. z가 0이면 성벽 없음
            float _OlympusWallBlend;
            float4 _OlympusWallRoad;        // xy = 성문 가운데, zw = 바깥 방향(단위) — 성문에서 전선으로 뻗는 길

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

            // 흙 비율. 녹지처럼 부드럽게 섞되, 모양은 건물 자리 사각형(돌린 것 포함)을 지켜야 한다.
            //
            // 전역 _OlympusDirtMask는 흙 마스크가 아니라 거리장이다 — 텍셀마다 "가장 가까운 흙 자리까지의 거리(칸)"를
            // -1~1.5칸 범위로 담았다(Core DirtField, 칸당 4텍셀). 바이리니어로 읽어 거리를 되살리고,
            // 그 거리에 번짐·부드러움·노이즈를 적용한다 — 크기와 각도에 상관없이 자리 + 번짐만큼의 둥근 사각형이 된다.
            // (예전엔 칸당 1픽셀 흙/아님이라 격자에 붙은 사각형만 됐고, 비스듬히 돌린 건물 밑 흙이 따로 놀았다.)
            half DirtCoverage(float3 positionWS, float2 cellXZ)
            {
                if (_OlympusDirtFieldRect.z <= 0.0)
                    return 0;

                float2 uv = (positionWS.xz - _OlympusDirtFieldRect.xy) * _OlympusDirtFieldRect.zw;
                // 텍스처 밖은 흙 아님 — 클램프로 가장자리 값이 끝없이 늘어나지 않게.
                if (any(uv < 0.0) || any(uv > 1.0))
                    return 0;

                float sd = lerp(-1.0, 1.5, SAMPLE_TEXTURE2D(_OlympusDirtMask, sampler_OlympusDirtMask, uv).r);

                float n = ((ValueNoise(cellXZ * _DirtEdgeNoiseScale) - 0.5) * 1.5
                         + (ValueNoise(cellXZ * _DirtEdgeNoiseScale * 2.9 + 5.0) - 0.5) * 0.5) * _DirtEdgeNoise;
                return (half)(1.0 - smoothstep(_DirtGrow - _DirtEdgeSoftness, _DirtGrow + _DirtEdgeSoftness, sd + n));
            }

            // 성벽 선까지의 부호 있는 거리(m, 밖이 양수). 거리장 범위(Core WallField: -4~28m) 밖은 그 끝값에서 더 멀어진다.
            float WallDistance(float3 positionWS)
            {
                if (_OlympusWallFieldRect.z <= 0.0)
                    return -100.0;

                float2 uv = (positionWS.xz - _OlympusWallFieldRect.xy) * _OlympusWallFieldRect.zw;
                float2 cuv = saturate(uv);
                float a = SAMPLE_TEXTURE2D(_OlympusWallFieldOld, sampler_OlympusWallFieldOld, cuv).r;
                float b = SAMPLE_TEXTURE2D(_OlympusWallFieldNew, sampler_OlympusWallFieldOld, cuv).r;
                float d = lerp(-4.0, 28.0, lerp(a, b, _OlympusWallBlend));
                float2 beyond = max(max(-uv, uv - 1.0), 0.0) / _OlympusWallFieldRect.zw;
                return d + length(beyond);
            }

            // 보로노이 — (가장 가까운 점까지, 두 번째로 가까운 점까지). 둘의 차가 0에 가까운 곳이 갈라진 틈이다.
            float2 Voronoi(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float f1 = 8.0, f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float2 o = float2(Hash21(i + g), Hash21(i + g + 19.19));
                        float d = length(g + o - f);
                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) { f2 = d; }
                    }
                }
                return float2(f1, f2);
            }

            // 성 밖: 땅을 그을리고(albedo), 용암 빛(emission)과 성벽에서 멀수록 짙어지는 연기 양(smoke)을 돌려준다.
            // 폐허 건물 자리(dirt)에는 용암을 내지 않는다 — 폐허가 용암 위에 떠 보이지 않게.
            void Outside(float wd, float3 positionWS, float2 cellXZ, half dirt, inout half3 albedo, out half3 emission, out half smoke)
            {
                emission = 0;
                smoke = 0;
                if (wd < -1.5 || _OutsideAmount <= 0.0)
                    return;

                float wobble = (ValueNoise(cellXZ * 0.18 + 41.0) - 0.5) * 2.0;
                float d = wd + wobble;
                half outside = (half)(smoothstep(-0.5, _OutsideEdge, d) * _OutsideAmount);

                float n = ValueNoise(cellXZ * 0.11 + 9.0) * 0.6 + ValueNoise(cellXZ * 0.37 + 2.0) * 0.4;
                half3 charred = lerp(_OutsideColorDark.rgb, _OutsideColor.rgb, smoothstep(0.25, 0.75, n));
                charred = lerp(charred, albedo * _OutsideRuinDirt, dirt);
                albedo = lerp(albedo, charred, outside);

                // 성문에서 전선 쪽으로 뻗은 길 — 병사가 지나는 곳이라 용암을 내지 않는다(그을린 땅만).
                float road = 0.0;
                if (dot(_OlympusWallRoad.zw, _OlympusWallRoad.zw) > 0.5)
                {
                    float2 rel = positionWS.xz - _OlympusWallRoad.xy;
                    float along = dot(rel, _OlympusWallRoad.zw);
                    float side = abs(rel.x * _OlympusWallRoad.w - rel.y * _OlympusWallRoad.z) + wobble * 0.6;
                    road = (1.0 - smoothstep(_RoadHalfWidth, _RoadHalfWidth + 2.0, side)) * step(-2.0, along);
                }

                float zone = smoothstep(_LavaStart, _LavaStart + _LavaRamp, d) * (1.0 - dirt) * (1.0 - road) * _OutsideAmount;
                if (zone > 0.0 && _LavaStrength > 0.0)
                {
                    float2 warp = float2(ValueNoise(cellXZ * 0.08), ValueNoise(cellXZ * 0.08 + 7.7)) * 2.0;
                    float2 v = Voronoi(cellXZ * _LavaScale + warp);
                    float width = _LavaCrackWidth * (0.6 + zone * 0.8);
                    float crack = 1.0 - smoothstep(0.0, width, v.y - v.x);
                    // 잔금 — 큰 틈 사이를 더 잘게 가른다(한 크기만 있으면 벌집 무늬로 보인다).
                    float2 v2 = Voronoi(cellXZ * _LavaScale * 2.6 + warp * 1.7 + 31.0);
                    crack = max(crack, (1.0 - smoothstep(0.0, width * 0.6, v2.y - v2.x)) * 0.45);
                    // 모든 틈이 빛나지 않게 — 저주파 노이즈로 군데군데만, 세기도 들쭉날쭉.
                    float glowMask = ValueNoise(cellXZ * 0.09 + 3.3);
                    crack *= smoothstep(0.25, 0.65, glowMask) * (0.5 + 0.5 * ValueNoise(cellXZ * 0.3 + 8.0));
                    float poolNoise = ValueNoise(cellXZ * 0.045 + 13.0) * 0.7 + ValueNoise(cellXZ * 0.13 + 1.0) * 0.3;
                    float pool = smoothstep(1.0 - _LavaPoolAmount * 0.5, 1.05 - _LavaPoolAmount * 0.5, poolNoise);
                    float lava = saturate(max(crack, pool)) * zone;

                    float pulse = 0.75 + 0.25 * sin(_Time.y * 1.3 + poolNoise * 9.0);
                    albedo = lerp(albedo, albedo * 0.3, lava);
                    emission = _LavaColor.rgb * (lava * pulse * _LavaStrength);
                }

                smoke = (half)(saturate(d / _SmokeDistance) * _SmokeStrength * _OutsideAmount);
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

                half3 emission;
                half smoke;
                Outside(WallDistance(input.positionWS), input.positionWS, cellXZ, dirt, albedo, emission, smoke);

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

                lit = lerp(lit, _SmokeColor.rgb, smoke) + emission;
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

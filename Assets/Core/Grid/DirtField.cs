using System;
using System.Collections.Generic;

namespace Olympus.Core.Grid
{
    /// <summary>흙바닥 한 자리 — 바닥 위의 돌린 직사각형(월드 단위).</summary>
    public readonly struct GroundPatch
    {
        public readonly WorldXZ Center;
        public readonly float HalfX;
        public readonly float HalfZ;

        /// <summary>Unity <c>Quaternion.Euler(0, yaw, 0)</c>와 같은 방향 — 양수면 +Z가 +X 쪽으로 돈다.</summary>
        public readonly float YawDegrees;

        public GroundPatch(WorldXZ center, float halfX, float halfZ, float yawDegrees)
        {
            Center = center;
            HalfX = halfX;
            HalfZ = halfZ;
            YawDegrees = yawDegrees;
        }

        /// <summary>
        /// 이 사각형까지의 부호 있는 거리 — 밖이면 양수, 안이면 음수(가장 가까운 변까지).
        /// </summary>
        public float SignedDistance(WorldXZ point)
        {
            double rad = YawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            float dx = point.X - Center.X, dz = point.Z - Center.Z;

            // 월드 → 사각형 자신의 축(돌린 것을 되돌린다).
            float lx = dx * cos - dz * sin;
            float lz = dx * sin + dz * cos;

            float qx = Math.Abs(lx) - HalfX, qz = Math.Abs(lz) - HalfZ;
            float ox = Math.Max(qx, 0f), oz = Math.Max(qz, 0f);
            return (float)Math.Sqrt(ox * ox + oz * oz) + Math.Min(Math.Max(qx, qz), 0f);
        }
    }

    /// <summary>
    /// 흙바닥 거리장(距離場)을 굽는다 — 텍셀마다 "가장 가까운 흙 사각형까지의 거리(칸 단위)"를 1바이트로 담는다.
    ///
    /// 예전 흙 마스크는 칸 하나당 1픽셀(흙/아님)이라 격자에 붙은 사각형만 그릴 수 있었고, 건물을 비스듬히 돌려
    /// 놓으면 흙은 반듯한 칸 모양으로 남아 건물과 따로 놀았다(형님 "건물이 틀어져 있다면 땅도 거기에 맞춰서", 2026-10-27).
    /// 거리를 담으면 셰이더가 바이리니어로 읽어도 모서리가 유지되고, 가장자리 번짐·부드러움은 그대로 칸 단위로 조절된다.
    /// </summary>
    public static class DirtField
    {
        /// <summary>담는 거리 범위(칸). 이보다 안쪽은 전부 흙, 바깥은 전부 흙 아님 — 셰이더의 번짐+부드러움+노이즈가 이 안에 들어가야 한다.</summary>
        public const float NearCells = -1f;
        public const float FarCells = 1.5f;

        public static byte Encode(float distanceCells)
        {
            float t = (distanceCells - NearCells) / (FarCells - NearCells);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return (byte)Math.Round(t * 255f);
        }

        public static float Decode(byte value) => NearCells + value / 255f * (FarCells - NearCells);

        /// <summary>
        /// <paramref name="pixels"/>(가로 <paramref name="width"/> × 세로 <paramref name="height"/>, 행 = Z)에 굽는다.
        /// 텍셀 (i, j)의 중심은 <paramref name="origin"/> + ((i + 0.5)·texel, (j + 0.5)·texel)이다.
        /// 여러 사각형이 겹치면 가까운 쪽(최솟값) — 합집합이다.
        /// </summary>
        public static void Bake(byte[] pixels, int width, int height, WorldXZ origin, float texel, float cellSize, IEnumerable<GroundPatch> patches)
        {
            if (pixels == null || pixels.Length < width * height)
                throw new ArgumentException("픽셀 배열이 가로×세로보다 작다.", nameof(pixels));
            if (texel <= 0f || cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(texel), "텍셀·칸 크기는 0보다 커야 한다.");

            byte far = Encode(FarCells);
            for (int i = 0; i < width * height; i++)
                pixels[i] = far;

            if (patches == null)
                return;

            float reach = FarCells * cellSize;
            foreach (GroundPatch p in patches)
            {
                // 돌려도 중심에서 대각선 반 길이 + 담는 거리 밖으로는 안 나간다 — 그 상자만 훑는다.
                float r = (float)Math.Sqrt(p.HalfX * p.HalfX + p.HalfZ * p.HalfZ) + reach;
                int i0 = Math.Max(0, (int)Math.Floor((p.Center.X - r - origin.X) / texel));
                int i1 = Math.Min(width - 1, (int)Math.Ceiling((p.Center.X + r - origin.X) / texel));
                int j0 = Math.Max(0, (int)Math.Floor((p.Center.Z - r - origin.Z) / texel));
                int j1 = Math.Min(height - 1, (int)Math.Ceiling((p.Center.Z + r - origin.Z) / texel));

                for (int j = j0; j <= j1; j++)
                {
                    float z = origin.Z + (j + 0.5f) * texel;
                    for (int i = i0; i <= i1; i++)
                    {
                        float x = origin.X + (i + 0.5f) * texel;
                        byte v = Encode(p.SignedDistance(new WorldXZ(x, z)) / cellSize);
                        int k = j * width + i;
                        if (v < pixels[k])
                            pixels[k] = v;
                    }
                }
            }
        }
    }
}

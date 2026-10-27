using System;
using System.Collections.Generic;
using Olympus.Core.Grid;

namespace Olympus.Core.Territory
{
    public enum WallPieceKind
    {
        Straight = 0,
        Post = 1,

        /// <summary>전선 쪽 모서리를 비스듬히 자른 자리. 성문 모델이 없는 단계는 비워 둔다(길만 열린다).</summary>
        Gate = 2,
    }

    /// <summary>성벽 한 조각. 조각 모델의 길이 방향 = 로컬 X, 바깥 = 로컬 +Z(돌림은 Unity yaw와 같은 방향).</summary>
    public readonly struct WallPiece
    {
        public readonly WallPieceKind Kind;
        public readonly WorldXZ Center;
        public readonly float YawDegrees;

        /// <summary>성문 자리의 길이(m) — 성문 조각만 쓴다. 성문 모델이 이보다 짧으면 남는 양 끝을 기둥이 덮는다.</summary>
        public readonly float Span;

        public WallPiece(WallPieceKind kind, WorldXZ center, float yawDegrees, float span = 0f)
        {
            Kind = kind;
            Center = center;
            YawDegrees = yawDegrees;
            Span = span;
        }
    }

    /// <summary>본부 레벨 한 단계의 성벽 — 기지 가운데를 둘러싼 정사각형의 반 변(m).</summary>
    public sealed class WallTier
    {
        public int HqLevel { get; }
        public float HalfSize { get; }

        public WallTier(int hqLevel, float halfSize)
        {
            HqLevel = hqLevel;
            HalfSize = halfSize;
        }
    }

    /// <summary>
    /// 본부 레벨마다 넓어지는 성벽(형님 "본부 레벨별로 계속 기지 확장", 2026-10-27; 설계 docs/hq1-5-frontline.md "영토·성벽 확장").
    /// 숫자는 <c>Assets/Data/walls.json</c> — 그 레벨에 열리는 건물이 모두 안에 들어오게 잡는다(RealData 시험이 확인).
    /// </summary>
    public sealed class WallConfig
    {
        private readonly List<WallTier> _tiers = new List<WallTier>();

        /// <summary>직선 한 조각 길이 = 기둥 중심 간격(m). 아트 성벽 키트가 이 길이로 나온다(OlympusArt wall_kit.py UNIT).</summary>
        public float Unit { get; }

        public IReadOnlyList<WallTier> Tiers => _tiers;

        public WallConfig(float unit)
        {
            Unit = unit;
        }

        public void Add(WallTier tier) => _tiers.Add(tier);

        /// <summary>본부 <paramref name="hqLevel"/>에 서 있을 성벽 — 그 레벨 이하 중 가장 높은 단계. 없으면 null(성벽 없음).</summary>
        public WallTier ForHq(int hqLevel)
        {
            WallTier best = null;
            foreach (WallTier t in _tiers)
            {
                if (t.HqLevel <= hqLevel && (best == null || t.HqLevel > best.HqLevel))
                    best = t;
            }
            return best;
        }

        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Unit <= 0f)
                problems.Add("성벽 조각 길이(unit)가 0 이하");

            for (int i = 0; i < _tiers.Count; i++)
            {
                WallTier t = _tiers[i];
                float sides = t.HalfSize * 2f / Unit;
                if (Unit > 0f && Math.Abs(sides - Math.Round(sides)) > 1e-3)
                    problems.Add("본부 Lv." + t.HqLevel + " 성벽: 한 변 " + (t.HalfSize * 2f) + "m가 조각 길이 " + Unit + "m의 배수가 아님 — 모서리에 빈틈이 생긴다");
                if (Unit > 0f && sides < 3)
                    problems.Add("본부 Lv." + t.HqLevel + " 성벽: 한 변이 조각 3개보다 짧음 — 성문 자리를 낼 수 없다");
                if (i > 0 && (t.HqLevel <= _tiers[i - 1].HqLevel || t.HalfSize < _tiers[i - 1].HalfSize))
                    problems.Add("본부 Lv." + t.HqLevel + " 성벽: 레벨은 오르는 순서, 크기는 줄지 않아야 함");
            }
            return problems;
        }
    }

    /// <summary>
    /// 성벽 둘레를 조각으로 나눈다. 기둥 중심 간격 = 조각 길이, 그 사이마다 직선 하나 — 이음매와 모서리는 기둥이 덮는다
    /// (아트 키트의 조립 규칙). 성문은 전선 쪽(+X+Z) 모서리를 비스듬히 잘라 낸다 — 전선이 그 대각선 방향으로 뻗어 있어서
    /// 장정이 성문을 지나 곧장 전선으로 나간다. 모서리 대신 변 가운데에 두면 성 밖에서 한 번 꺾어 걸어야 한다.
    /// </summary>
    public static class WallRing
    {
        /// <param name="gateUnits">모서리에서 양쪽으로 잘라 낼 조각 수(성문 모델 길이에 맞춘다 — 4m 성문 1, 8m 성문 2).</param>
        public static List<WallPiece> Layout(WorldXZ center, float halfSize, float unit, int gateUnits)
        {
            if (unit <= 0f)
                throw new ArgumentOutOfRangeException(nameof(unit));
            int n = (int)Math.Round(halfSize * 2f / unit);
            if (gateUnits < 1 || gateUnits > n - 2)
                throw new ArgumentOutOfRangeException(nameof(gateUnits), "성문 자리는 1조각 이상, 한 변에서 2조각은 남겨야 한다");

            var pieces = new List<WallPiece>();
            float h = n * unit * 0.5f;
            float cut = h - gateUnits * unit;   // 전선 쪽 두 변은 이 좌표까지만 벽이 선다

            // 네 변: (시작 코너, 진행 방향, 바깥 yaw). 반시계로 돈다.
            var sides = new (float sx, float sz, float dx, float dz, float yaw, bool towardGate, bool fromGate)[]
            {
                (-h, -h, 1f, 0f, 180f, false, false),   // 앞(z = -h), 바깥 -Z
                (h, -h, 0f, 1f, 90f, true, false),      // 오른쪽(x = +h), 끝이 성문 모서리
                (h, h, -1f, 0f, 0f, false, true),       // 뒤(z = +h), 시작이 성문 모서리
                (-h, h, 0f, -1f, -90f, false, false),   // 왼쪽(x = -h)
            };

            foreach (var s in sides)
            {
                int first = s.fromGate ? gateUnits : 0;
                int last = s.towardGate ? n - gateUnits : n;   // 조각 번호 [first, last)

                for (int k = first; k < last; k++)
                {
                    float t = (k + 0.5f) * unit;
                    pieces.Add(new WallPiece(WallPieceKind.Straight, At(center, s.sx + s.dx * t, s.sz + s.dz * t), s.yaw));
                }

                // 기둥: 변의 시작점(코너 포함)부터 — 끝 코너는 다음 변이 시작점으로 놓는다. 성문 모서리 기둥은 없다.
                for (int k = first; k < last + (s.towardGate ? 1 : 0); k++)
                {
                    float t = k * unit;
                    pieces.Add(new WallPiece(WallPieceKind.Post, At(center, s.sx + s.dx * t, s.sz + s.dz * t), 0f));
                }
            }

            // 성문: (cut, h) ~ (h, cut)을 잇는 대각선 가운데, 바깥 = (+1, +1) → yaw 45.
            float mid = (cut + h) * 0.5f;
            float span = (float)(gateUnits * unit * Math.Sqrt(2.0));
            pieces.Add(new WallPiece(WallPieceKind.Gate, At(center, mid, mid), 45f, span));
            return pieces;
        }

        private static WorldXZ At(WorldXZ c, float x, float z) => new WorldXZ(c.X + x, c.Z + z);
    }

    /// <summary><c>walls.json</c>의 모양. 파싱은 Game(JsonUtility), 규칙 객체 변환과 검증은 여기.</summary>
    [Serializable]
    public sealed class WallFile
    {
        public float unit = 4f;
        public List<WallTierRecord> tiers = new List<WallTierRecord>();

        public WallConfig ToConfig()
        {
            var config = new WallConfig(unit);
            if (tiers != null)
            {
                foreach (WallTierRecord r in tiers)
                    config.Add(new WallTier(r.hqLevel, r.halfSize));
            }
            return config;
        }
    }

    [Serializable]
    public sealed class WallTierRecord
    {
        public int hqLevel;
        public float halfSize;
    }
}

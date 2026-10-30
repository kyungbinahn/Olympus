using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.State;

namespace Olympus.Core.Territory
{
    public enum WallPieceKind
    {
        Straight = 0,
        Post = 1,

        /// <summary>전선 쪽 둘레에 낸 성문 자리. 성문 모델이 없는 단계는 비워 둔다(길만 열린다).</summary>
        Gate = 2,
    }

    /// <summary>성벽 한 조각. 조각 모델의 길이 방향 = 로컬 X, 바깥 = 로컬 +Z(돌림은 Unity yaw와 같은 방향).</summary>
    public readonly struct WallPiece
    {
        public readonly WallPieceKind Kind;
        public readonly WorldXZ Center;
        public readonly float YawDegrees;

        /// <summary>이 조각이 덮을 길이(m) — 직선·성문만 쓴다. 모델 길이와 다르면 길이 방향으로 늘이거나 줄인다.</summary>
        public readonly float Span;

        public WallPiece(WallPieceKind kind, WorldXZ center, float yawDegrees, float span = 0f)
        {
            Kind = kind;
            Center = center;
            YawDegrees = yawDegrees;
            Span = span;
        }
    }

    /// <summary>
    /// 성벽 한 단계(영토 단계). <see cref="HqLevel"/>은 이 성벽이 둘러쌀 마을 — 본부 Lv.N까지 열리는 건물이다.
    /// 언제 서는지는 <see cref="Unlock"/>이 정한다(도적 소굴·거인 처치 — docs/village-growth.md).
    /// 성문 길이는 그 단계 아트 성문 모델의 길이(m).
    /// </summary>
    public sealed class WallTier
    {
        public int HqLevel { get; }
        public float GateLength { get; }

        /// <summary>이 단계가 열리는 조건(본부 조건과 같은 꼴). 비어 있으면 본부 레벨만 본다.</summary>
        public IReadOnlyList<Requirement> Unlock { get; }

        public WallTier(int hqLevel, float gateLength, IReadOnlyList<Requirement> unlock = null)
        {
            HqLevel = hqLevel;
            GateLength = gateLength;
            Unlock = unlock ?? Array.Empty<Requirement>();
        }
    }

    /// <summary>
    /// 성벽 규칙(<c>Assets/Data/walls.json</c>). 모양은 숫자로 정하지 않고 그 레벨까지 열리는 건물에서 나온다 —
    /// 형님 "외곽이 마을 형태를 따라서 유기적", 2026-10-27(그 전엔 레벨별 정사각형 반 변을 적었다).
    /// </summary>
    public sealed class WallConfig
    {
        private readonly List<WallTier> _tiers = new List<WallTier>();

        /// <summary>직선 한 조각 길이(m). 아트 성벽 키트가 이 길이로 나온다(OlympusArt wall_kit.py UNIT).</summary>
        public float Unit { get; set; } = 4f;

        /// <summary>
        /// 기둥과 기둥 사이에 넣는 직선 조각 수. 1이면 기둥이 4m마다 서서 기둥만 줄지어 보였다(형님 "사이마다 3개씩", 2026-10-27).
        /// </summary>
        public int PiecesPerSpan { get; set; } = 3;

        /// <summary>건물 자리에서 성벽 중심선까지의 최소 거리(m). 모자라면 계산이 0.5m씩 늘려 다시 짓는다.</summary>
        public float Margin { get; set; } = 2.5f;

        /// <summary>
        /// 건물 사이를 메우는 정도(m) — 건물 자리 거리장을 부드럽게 합칠 때의 반경. 0이면 건물마다 따로 부푼
        /// 울퉁불퉁한 모양, 클수록 둥근 덩어리가 된다.
        /// </summary>
        public float Blend { get; set; } = 3f;

        /// <summary>둘레를 각도 방향으로 흐리는 정도(도). 마을 가운데에서 본 반경을 이 폭으로 고르게 한다.</summary>
        public float SmoothDegrees { get; set; } = 10f;

        /// <summary>성벽(두께·기둥 폭 포함)이 건물 자리를 덮지 않게 남길 여유(m). RealData 시험과 재계산이 같은 값을 쓴다.</summary>
        public float Clearance { get; set; } = 1.5f;

        public IReadOnlyList<WallTier> Tiers => _tiers;

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

        /// <summary>
        /// 지금 서 있을 성벽 — 앞에서부터 조건이 찬 단계까지(중간을 건너뛰지 않는다). 거인을 이긴 순간 넓어지고,
        /// 본부 공사를 기다리지 않는다(형님 2026-10-28, docs/village-growth.md 4-1 B안).
        ///
        /// 본부 레벨로 정해지는 단계(<see cref="ForHq"/>)보다 작아지지는 않는다. 데이터가 맞으면(<see cref="CheckAgainst"/>)
        /// 본부가 땅보다 앞설 일이 없지만, 조건이 생기기 전 세이브는 거인 없이 본부가 올라가 있다 — 그때 열린 건물이
        /// 성 밖에 서면 안 된다. 본부가 없으면(폐허) 성벽도 없다.
        /// </summary>
        public WallTier Current(GameState state, ProgressionConfig progression)
        {
            WallTier floor = ForHq(ProgressionRules.HqLevel(state, progression));
            if (floor == null)
                return null;

            WallTier best = floor;
            foreach (WallTier t in _tiers)
            {
                if (!ProgressionRules.AllMet(t.Unlock, state, progression))
                    break;
                if (t.HqLevel > best.HqLevel)
                    best = t;
            }
            return best;
        }

        /// <summary>
        /// 본부 Lv.N의 조건에 영토 N의 조건이 들어 있는지 본다. 빠지면 본부가 땅보다 앞서서
        /// 새로 열린 건물이 성 밖에 서고, 지을 수 있는데 성 밖이라 안 되는 막힘이 생긴다.
        /// </summary>
        public List<string> CheckAgainst(ProgressionConfig progression)
        {
            var problems = new List<string>();
            if (progression == null)
                return problems;

            foreach (WallTier t in _tiers)
            {
                if (t.Unlock.Count == 0)
                    continue;

                HqLevelDef hq;
                if (!progression.TryGetHqLevel(t.HqLevel, out hq))
                {
                    problems.Add("영토 " + t.HqLevel + "에 조건이 있는데 본부 Lv." + t.HqLevel + "이 progression.json에 없다");
                    continue;
                }

                foreach (Requirement u in t.Unlock)
                {
                    bool covered = false;
                    foreach (Requirement r in hq.Requirements)
                    {
                        if (r.Kind == u.Kind && r.Target == u.Target && r.Amount >= u.Amount)
                            covered = true;
                    }
                    if (!covered)
                        problems.Add("본부 Lv." + t.HqLevel + " 조건에 영토 " + t.HqLevel + " 조건(" + u + ")이 없다 — " +
                                     "본부가 땅보다 앞서면 새로 열린 건물이 성 밖에 선다");
                }
            }
            return problems;
        }

        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Unit <= 0f)
                problems.Add("성벽 조각 길이(unit)가 0 이하");
            if (PiecesPerSpan < 1)
                problems.Add("기둥 사이 조각 수(piecesPerSpan)가 1보다 작음");
            if (Margin < Clearance)
                problems.Add("건물~성벽 거리(margin)가 여유(clearance)보다 작음 — 계산이 매번 늘려 다시 짓는다");
            if (Blend < 0f || SmoothDegrees < 0f)
                problems.Add("blend·smoothDegrees는 0 이상");

            for (int i = 0; i < _tiers.Count; i++)
            {
                WallTier t = _tiers[i];
                if (t.GateLength <= 0f)
                    problems.Add("본부 Lv." + t.HqLevel + " 성벽: 성문 길이(gateLength)가 0 이하");
                if (i > 0 && t.HqLevel <= _tiers[i - 1].HqLevel)
                    problems.Add("본부 Lv." + t.HqLevel + " 성벽: 레벨은 오르는 순서여야 함");
            }
            return problems;
        }
    }

    /// <summary>한 단계 성벽을 지은 결과 — 조각들, 둘레(기둥 자리를 이은 닫힌 다각형), 성문.</summary>
    public sealed class WallPlan
    {
        public List<WallPiece> Pieces { get; } = new List<WallPiece>();

        /// <summary>성문 한쪽 끝부터 둘레를 돌아 다른 쪽 끝까지의 기둥 자리. 마지막 → 첫 점이 성문이다.</summary>
        public List<WorldXZ> Outline { get; } = new List<WorldXZ>();

        public WorldXZ GateCenter { get; internal set; }
        public float GateYawDegrees { get; internal set; }

        /// <summary>실제로 쓴 건물~성벽 거리(m). 여유가 모자라 늘렸으면 설정값보다 크다.</summary>
        public float MarginUsed { get; internal set; }

        /// <summary>점이 둘레 안에 있으면 양수 — 가장 가까운 성벽 선까지의 거리(m). 밖이면 음수.</summary>
        public float InsideDistance(WorldXZ p) => WallRing.InsideDistance(Outline, p);
    }

    /// <summary>
    /// 마을을 따라 도는 성벽을 짓는다.
    ///
    /// 1. 건물 자리(돌린 사각형)마다 거리장을 만들고 부드럽게 합친다(smooth-min) — 가까운 건물 사이는 메워지고
    ///    떨어진 건물 쪽으로는 둘레가 뻗는다. 그 등고선(건물에서 margin)이 마을 모양이다.
    /// 2. 마을 가운데에서 1도마다 광선을 쏴 등고선의 가장 바깥 반경을 잰다. 반경을 각도 방향으로 흐려서
    ///    둥글게 하되, 흐린 결과가 건물 쪽으로 파고들면 합치기 전(건물 하나하나) 등고선까지는 반드시 밀어낸다.
    ///    광선 방식이라 둘레가 늘 고리 하나다 — 등고선을 직접 따면 떨어진 건물이 섬으로 갈라질 수 있다.
    /// 3. 전선 쪽(<paramref name="frontDirection"/>) 둘레에 성문을 내고, 나머지 둘레를 같은 길이 구간으로 나눠
    ///    기둥을 세운다. 구간마다 직선 PiecesPerSpan개 — 구간 길이가 조각 길이의 배수가 아니면 조각을 살짝 늘이거나 줄인다.
    /// 4. 기둥 사이 직선은 곡선의 현이라 볼록한 곳에서 안쪽으로 파고든다. 그래서 다 지은 뒤 건물마다 여유를 재고,
    ///    모자라면 margin을 0.5m 늘려 다시 짓는다.
    /// </summary>
    public static class WallRing
    {
        private const int Rays = 360;
        private const int MaxRetries = 16;

        /// <summary>
        /// 본부 <paramref name="hqLevel"/>까지 열리는 건물 자리(풋프린트 그대로) — 성벽이 둘러쌀 마을. 게임 화면과 RealData 시험이
        /// 같은 함수를 쓴다(보이는 성벽과 시험한 성벽이 갈라지지 않게). 아직 안 열린 건물은 성 밖 폐허로 남는다.
        /// </summary>
        public static List<GroundPatch> VillagePatches(IEnumerable<(string defId, GridPos anchor)> slots, BuildingCatalog catalog,
            SquareGrid grid, int hqLevel)
        {
            var patches = new List<GroundPatch>();
            foreach ((string defId, GridPos anchor) in slots)
            {
                BuildingDef def;
                if (!catalog.TryGet(defId, out def) || def.UnlockHqLevel > hqLevel)
                    continue;
                WorldXZ c = def.Footprint.CenterAt(anchor, grid);
                patches.Add(new GroundPatch(c, def.Footprint.Width * grid.CellSize * 0.5f, def.Footprint.Height * grid.CellSize * 0.5f, 0f));
            }
            return patches;
        }

        public static WallPlan Plan(IReadOnlyList<GroundPatch> buildings, WallConfig config, float gateLength, WorldXZ frontDirection)
        {
            if (buildings == null || buildings.Count == 0)
                return null;
            if (config.Unit <= 0f || config.PiecesPerSpan < 1)
                throw new ArgumentOutOfRangeException(nameof(config), "조각 길이·기둥 사이 조각 수가 잘못됐다");

            var rects = new Rect[buildings.Count];
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < rects.Length; i++)
            {
                rects[i] = new Rect(buildings[i]);
                foreach (WorldXZ c in Corners(buildings[i]))
                {
                    minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                    minZ = Math.Min(minZ, c.Z); maxZ = Math.Max(maxZ, c.Z);
                }
            }
            var center = new WorldXZ((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);

            WallPlan plan = null;
            float margin = Math.Max(config.Margin, config.Clearance);
            for (int attempt = 0; attempt <= MaxRetries; attempt++, margin += 0.5f)
            {
                plan = Build(rects, center, maxX - minX, maxZ - minZ, margin, config, gateLength, frontDirection);
                if (MinRoom(plan, buildings) >= config.Clearance)
                    break;
            }
            return plan;
        }

        /// <summary>건물들 중 성벽까지 여유가 가장 작은 것의 여유(m) — 꼭짓점이 둘레 안쪽으로 얼마나, 기둥이 건물에서 얼마나.</summary>
        public static float MinRoom(WallPlan plan, IReadOnlyList<GroundPatch> buildings)
        {
            float room = float.MaxValue;
            foreach (GroundPatch b in buildings)
                room = Math.Min(room, Room(plan, b));
            return room;
        }

        /// <summary>건물 하나의 여유(m) — 네 꼭짓점의 둘레 안쪽 거리와, 둘레 꼭짓점(기둥)이 건물에서 떨어진 거리 중 작은 것.</summary>
        public static float Room(WallPlan plan, GroundPatch building)
        {
            float room = float.MaxValue;
            foreach (WorldXZ c in Corners(building))
                room = Math.Min(room, InsideDistance(plan.Outline, c));
            foreach (WorldXZ v in plan.Outline)
                room = Math.Min(room, building.SignedDistance(v));
            return room;
        }

        /// <summary>닫힌 다각형 안이면 양수(가장 가까운 변까지 거리), 밖이면 음수.</summary>
        public static float InsideDistance(IReadOnlyList<WorldXZ> polygon, WorldXZ p)
        {
            int n = polygon.Count;
            if (n < 3)
                return -float.MaxValue;

            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                WorldXZ a = polygon[j], b = polygon[i];
                best = Math.Min(best, SegmentDistance(p, a, b));
                if ((b.Z > p.Z) != (a.Z > p.Z) && p.X < (a.X - b.X) * (p.Z - b.Z) / (a.Z - b.Z) + b.X)
                    inside = !inside;
            }
            return inside ? best : -best;
        }

        private static WallPlan Build(Rect[] rects, WorldXZ center, float spanX, float spanZ, float margin,
            WallConfig config, float gateLength, WorldXZ frontDirection)
        {
            float reach = (float)Math.Sqrt(spanX * spanX + spanZ * spanZ) * 0.5f + margin + config.Blend * 4f + 8f;
            var hard = new float[Rays];
            var soft = new float[Rays];
            for (int k = 0; k < Rays; k++)
            {
                double a = k * 2.0 * Math.PI / Rays;
                var dir = new WorldXZ((float)Math.Sin(a), (float)Math.Cos(a));
                hard[k] = OuterRadius(rects, center, dir, reach, margin, 0f);
                soft[k] = OuterRadius(rects, center, dir, reach, margin, config.Blend);
            }

            // 아무 건물도 안 닿는 방향(드물다 — 합치기 반경보다 멀리 떨어진 사이)은 이웃 방향 값으로 메운다.
            FillGaps(soft);
            FillGaps(hard);

            float[] smooth = SmoothCircular(soft, config.SmoothDegrees * Rays / 360f);
            var radius = new float[Rays];
            for (int k = 0; k < Rays; k++)
                radius[k] = Math.Max(smooth[k], hard[k]);

            // 둘레 곡선(1도마다 한 점)과 누적 길이.
            var curve = new WorldXZ[Rays];
            for (int k = 0; k < Rays; k++)
            {
                double a = k * 2.0 * Math.PI / Rays;
                curve[k] = new WorldXZ(center.X + (float)Math.Sin(a) * radius[k], center.Z + (float)Math.Cos(a) * radius[k]);
            }
            var arc = new float[Rays + 1];
            for (int k = 0; k < Rays; k++)
                arc[k + 1] = arc[k] + Distance(curve[k], curve[(k + 1) % Rays]);
            float total = arc[Rays];

            // 성문 가운데 = 전선 방향 광선이 둘레를 지나는 점.
            double frontAngle = Math.Atan2(frontDirection.X, frontDirection.Z);
            if (frontAngle < 0)
                frontAngle += 2.0 * Math.PI;
            double fk = frontAngle / (2.0 * Math.PI) * Rays;
            int k0 = (int)Math.Floor(fk) % Rays;
            float sGate = arc[k0] + (float)(fk - Math.Floor(fk)) * (arc[k0 + 1] - arc[k0]);

            // 성문 양 끝 — 둘레를 따라 같은 거리만큼, 두 끝 사이 직선 거리가 성문 길이가 되게 맞춘다.
            float half = gateLength * 0.5f;
            WorldXZ gateA = default, gateB = default;
            for (int it = 0; it < 6; it++)
            {
                gateA = At(curve, arc, sGate - half);
                gateB = At(curve, arc, sGate + half);
                half += (gateLength - Distance(gateA, gateB)) * 0.5f;
            }
            float sB = sGate + half;
            float rest = total - 2f * half;

            float spanLength = config.Unit * config.PiecesPerSpan;
            int spans = Math.Max(3, (int)Math.Round(rest / spanLength));

            var plan = new WallPlan { MarginUsed = margin };
            for (int j = 0; j <= spans; j++)
                plan.Outline.Add(At(curve, arc, sB + rest * j / spans));

            // 직선: 구간마다 PiecesPerSpan개. 바깥 = 마을 가운데 반대쪽 수직.
            var spanYaw = new float[spans];
            for (int j = 0; j < spans; j++)
            {
                WorldXZ p = plan.Outline[j], q = plan.Outline[j + 1];
                spanYaw[j] = OutwardYaw(p, q, center);
                float piece = Distance(p, q) / config.PiecesPerSpan;
                for (int i = 0; i < config.PiecesPerSpan; i++)
                {
                    float t = (i + 0.5f) / config.PiecesPerSpan;
                    plan.Pieces.Add(new WallPiece(WallPieceKind.Straight, Lerp(p, q, t), spanYaw[j], piece));
                }
            }

            // 성문: 마지막 기둥(A 쪽) → 첫 기둥(B 쪽)을 잇는 현.
            WorldXZ ga = plan.Outline[spans], gb = plan.Outline[0];
            float gateYaw = OutwardYaw(ga, gb, center);
            plan.GateCenter = Lerp(ga, gb, 0.5f);
            plan.GateYawDegrees = gateYaw;
            plan.Pieces.Add(new WallPiece(WallPieceKind.Gate, plan.GateCenter, gateYaw, Distance(ga, gb)));

            // 기둥: 꺾이는 자리마다 — 양쪽 구간 방향의 가운데를 보게 돌린다(네모 기둥이 한쪽 벽에만 맞춰 비뚤어 보이지 않게).
            for (int j = 0; j <= spans; j++)
            {
                float before = j == 0 ? gateYaw : spanYaw[j - 1];
                float after = j == spans ? gateYaw : spanYaw[j];
                plan.Pieces.Add(new WallPiece(WallPieceKind.Post, plan.Outline[j], MeanAngle(before, after)));
            }
            return plan;
        }

        /// <summary>광선을 따라 바깥에서 안쪽으로 걸으며 거리장이 margin 이하가 되는 가장 바깥 반경. 안 닿으면 NaN.</summary>
        private static float OuterRadius(Rect[] rects, WorldXZ center, WorldXZ dir, float reach, float margin, float blend)
        {
            const float Step = 0.5f;
            float prev = reach;
            for (float r = reach; r >= 0f; r -= Step)
            {
                if (Field(rects, center.X + dir.X * r, center.Z + dir.Z * r, blend) <= margin)
                {
                    // r(안) ~ prev(밖) 사이를 반씩 좁힌다.
                    float lo = r, hi = prev;
                    for (int i = 0; i < 8; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Field(rects, center.X + dir.X * mid, center.Z + dir.Z * mid, blend) <= margin)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    return lo;
                }
                prev = r;
            }
            return float.NaN;
        }

        /// <summary>건물 자리까지의 거리 — blend가 0이면 가장 가까운 것(합집합), 크면 부드러운 최솟값(사이가 메워진다).</summary>
        private static float Field(Rect[] rects, float x, float z, float blend)
        {
            float min = float.MaxValue;
            for (int i = 0; i < rects.Length; i++)
                min = Math.Min(min, rects[i].Distance(x, z));
            if (blend <= 0f)
                return min;

            double sum = 0;
            for (int i = 0; i < rects.Length; i++)
                sum += Math.Exp(-(rects[i].Distance(x, z) - min) / blend);
            return min - blend * (float)Math.Log(sum);
        }

        private static void FillGaps(float[] r)
        {
            int n = r.Length;
            int any = -1;
            for (int k = 0; k < n; k++)
            {
                if (!float.IsNaN(r[k]))
                {
                    any = k;
                    break;
                }
            }
            if (any < 0)
            {
                for (int k = 0; k < n; k++)
                    r[k] = 0f;
                return;
            }

            var src = (float[])r.Clone();
            for (int k = 0; k < n; k++)
            {
                if (!float.IsNaN(src[k]))
                    continue;
                int back = 1, fwd = 1;
                while (float.IsNaN(src[(k - back + n) % n])) back++;
                while (float.IsNaN(src[(k + fwd) % n])) fwd++;
                float t = (float)back / (back + fwd);
                r[k] = src[(k - back + n) % n] * (1f - t) + src[(k + fwd) % n] * t;
            }
        }

        private static float[] SmoothCircular(float[] r, float sigma)
        {
            int n = r.Length;
            if (sigma < 0.5f)
                return (float[])r.Clone();

            int reach = (int)Math.Ceiling(sigma * 3f);
            var weights = new float[reach * 2 + 1];
            float wsum = 0f;
            for (int i = -reach; i <= reach; i++)
            {
                weights[i + reach] = (float)Math.Exp(-0.5 * i * i / (sigma * sigma));
                wsum += weights[i + reach];
            }

            var o = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = 0f;
                for (int i = -reach; i <= reach; i++)
                    s += r[((k + i) % n + n) % n] * weights[i + reach];
                o[k] = s / wsum;
            }
            return o;
        }

        /// <summary>누적 길이 s 지점의 둘레 점(한 바퀴 넘으면 감는다).</summary>
        private static WorldXZ At(WorldXZ[] curve, float[] arc, float s)
        {
            int n = curve.Length;
            float total = arc[n];
            s %= total;
            if (s < 0f)
                s += total;

            int lo = 0, hi = n;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (arc[mid] <= s) lo = mid; else hi = mid;
            }
            float seg = arc[lo + 1] - arc[lo];
            float t = seg > 1e-6f ? (s - arc[lo]) / seg : 0f;
            return Lerp(curve[lo], curve[(lo + 1) % n], t);
        }

        /// <summary>p→q 선분의 바깥(마을 가운데 반대쪽) 수직 방향의 yaw(도). 바깥 = 조각 로컬 +Z.</summary>
        private static float OutwardYaw(WorldXZ p, WorldXZ q, WorldXZ center)
        {
            float dx = q.X - p.X, dz = q.Z - p.Z;
            float nx = dz, nz = -dx;
            WorldXZ mid = Lerp(p, q, 0.5f);
            if (nx * (mid.X - center.X) + nz * (mid.Z - center.Z) < 0f)
            {
                nx = -nx;
                nz = -nz;
            }
            return (float)(Math.Atan2(nx, nz) * 180.0 / Math.PI);
        }

        private static float MeanAngle(float a, float b)
        {
            double ra = a * Math.PI / 180.0, rb = b * Math.PI / 180.0;
            return (float)(Math.Atan2(Math.Sin(ra) + Math.Sin(rb), Math.Cos(ra) + Math.Cos(rb)) * 180.0 / Math.PI);
        }

        private static float SegmentDistance(WorldXZ p, WorldXZ a, WorldXZ b)
        {
            float abx = b.X - a.X, abz = b.Z - a.Z;
            float len2 = abx * abx + abz * abz;
            float t = len2 > 1e-9f ? ((p.X - a.X) * abx + (p.Z - a.Z) * abz) / len2 : 0f;
            t = Math.Max(0f, Math.Min(1f, t));
            float dx = p.X - (a.X + abx * t), dz = p.Z - (a.Z + abz * t);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Distance(WorldXZ a, WorldXZ b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static WorldXZ Lerp(WorldXZ a, WorldXZ b, float t) => new WorldXZ(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        public static IEnumerable<WorldXZ> Corners(GroundPatch p)
        {
            double rad = p.YawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float lx = sx * p.HalfX, lz = sz * p.HalfZ;
                    // GroundPatch.SignedDistance의 역변환(로컬 → 월드).
                    yield return new WorldXZ(p.Center.X + lx * cos + lz * sin, p.Center.Z - lx * sin + lz * cos);
                }
            }
        }

        /// <summary>건물 자리 — 거리 계산을 빠르게 하려고 돌림 cos·sin을 미리 구해 둔다(광선 360개 × 수백 걸음).</summary>
        private readonly struct Rect
        {
            private readonly float _cx, _cz, _hx, _hz, _cos, _sin;

            public Rect(GroundPatch p)
            {
                _cx = p.Center.X;
                _cz = p.Center.Z;
                _hx = p.HalfX;
                _hz = p.HalfZ;
                double rad = p.YawDegrees * Math.PI / 180.0;
                _cos = (float)Math.Cos(rad);
                _sin = (float)Math.Sin(rad);
            }

            public float Distance(float x, float z)
            {
                float dx = x - _cx, dz = z - _cz;
                float qx = Math.Abs(dx * _cos - dz * _sin) - _hx;
                float qz = Math.Abs(dx * _sin + dz * _cos) - _hz;
                float ox = Math.Max(qx, 0f), oz = Math.Max(qz, 0f);
                return (float)Math.Sqrt(ox * ox + oz * oz) + Math.Min(Math.Max(qx, qz), 0f);
            }
        }
    }

    /// <summary>
    /// 성벽 안팎 거리장 — 텍셀마다 "성벽 선까지의 부호 있는 거리(m, 밖이 양수)"를 1바이트로 담는다.
    /// 지면 셰이더가 이걸 읽어 성 밖을 그을린 땅·용암으로 바꾼다(형님 "성 밖 바닥은 어둡게 황폐하게", "용암이 있는 세상", 2026-10-27).
    /// </summary>
    public static class WallField
    {
        public const float NearMeters = -4f;
        public const float FarMeters = 28f;

        public static byte Encode(float meters)
        {
            float t = (meters - NearMeters) / (FarMeters - NearMeters);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return (byte)Math.Round(t * 255f);
        }

        public static float Decode(byte value) => NearMeters + value / 255f * (FarMeters - NearMeters);

        /// <summary>텍셀 (i, j)의 중심은 <paramref name="origin"/> + ((i + 0.5)·texel, (j + 0.5)·texel). 둘레가 없으면 전부 "안"(효과 없음).</summary>
        public static void Bake(byte[] pixels, int width, int height, WorldXZ origin, float texel, IReadOnlyList<WorldXZ> outline)
        {
            if (pixels == null || pixels.Length < width * height)
                throw new ArgumentException("픽셀 배열이 가로×세로보다 작다.", nameof(pixels));

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float d;
                    if (outline == null || outline.Count < 3)
                        d = NearMeters;
                    else
                        d = -WallRing.InsideDistance(outline, new WorldXZ(origin.X + (i + 0.5f) * texel, origin.Z + (j + 0.5f) * texel));
                    pixels[j * width + i] = Encode(d);
                }
            }
        }
    }

    /// <summary><c>walls.json</c>의 모양. 파싱은 Game(JsonUtility), 규칙 객체 변환과 검증은 여기.</summary>
    [Serializable]
    public sealed class WallFile
    {
        public float unit = 4f;
        public int piecesPerSpan = 3;
        public float margin = 2.5f;
        public float blend = 3f;
        public float smoothDegrees = 10f;
        public float clearance = 1.5f;
        public List<WallTierRecord> tiers = new List<WallTierRecord>();

        public WallConfig ToConfig()
        {
            var config = new WallConfig
            {
                Unit = unit,
                PiecesPerSpan = piecesPerSpan,
                Margin = margin,
                Blend = blend,
                SmoothDegrees = smoothDegrees,
                Clearance = clearance,
            };
            if (tiers != null)
            {
                foreach (WallTierRecord r in tiers)
                {
                    var unlock = new List<Requirement>();
                    if (r.unlock != null)
                    {
                        foreach (RequirementRecord u in r.unlock)
                            unlock.Add(ProgressionFileMapper.ToRequirement(u, "walls.json 영토 " + r.hqLevel));
                    }
                    config.Add(new WallTier(r.hqLevel, r.gateLength, unlock));
                }
            }
            return config;
        }
    }

    [Serializable]
    public sealed class WallTierRecord
    {
        public int hqLevel;
        public float gateLength = 8f;
        public List<RequirementRecord> unlock = new List<RequirementRecord>();
    }
}

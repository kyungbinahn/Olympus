using System;
using System.Collections.Generic;

namespace Olympus.Core.Battle
{
    public enum BattleTeam
    {
        Player = 0,
        Enemy = 1,
    }

    /// <summary>싸움터의 병사 하나. 화면이 매 프레임 읽어서 그린다 — 화면이 고치는 길은 없다.</summary>
    public sealed class BattleUnit
    {
        public int Id { get; }
        public BattleTeam Team { get; }
        public UnitDef Def { get; }
        public long MaxHp { get; }
        public long Attack { get; }

        /// <summary>
        /// 크기 배수. 같은 거인이라도 우두머리는 크게 보이게 하려는 화면용 값이고, 몸 반지름에도 곱한다.
        /// </summary>
        public float Scale { get; }

        public float X { get; internal set; }
        public float Z { get; internal set; }
        public long Hp { get; internal set; }
        public bool IsAlive => Hp > 0L;

        /// <summary>이번 걸음에 움직였나. 화면이 걸음 흔들림을 켤지 정한다.</summary>
        public bool IsMoving { get; internal set; }

        /// <summary>바라보는 방향(단위 벡터). 화면이 몸을 돌린다.</summary>
        public float FacingX { get; internal set; }
        public float FacingZ { get; internal set; }

        internal long CooldownMs;
        internal int TargetId = -1;

        internal BattleUnit(int id, BattleTeam team, UnitDef def, long maxHp, long attack, float scale, float x, float z)
        {
            Id = id;
            Team = team;
            Def = def;
            MaxHp = maxHp;
            Hp = maxHp;
            Attack = attack;
            Scale = scale;
            X = x;
            Z = z;
            FacingZ = team == BattleTeam.Player ? 1f : -1f;
        }

        public float Radius => Def.Radius * Scale;

        /// <summary>
        /// 전투력에 더하는 몫 — 체력 절반 + 초당 피해 10배. 화면에 "내 전투력 vs 적"으로 띄우는
        /// 숫자일 뿐 승패는 시뮬레이션이 정한다(탑포스도 전투력이 낮아도 이기는 경우가 있다).
        /// </summary>
        public long Power => MaxHp / 2L + Attack * 10_000L / Def.AttackIntervalMs;
    }

    public enum BattleEventKind
    {
        Attack = 0,
        Death = 1,
    }

    /// <summary>한 걸음 동안 일어난 일. 화면이 찌르기·화살·쓰러짐 연출을 붙인다.</summary>
    public readonly struct BattleEvent
    {
        public readonly BattleEventKind Kind;
        public readonly int SourceId;
        public readonly int TargetId;
        public readonly long Damage;

        public BattleEvent(BattleEventKind kind, int sourceId, int targetId, long damage)
        {
            Kind = kind;
            SourceId = sourceId;
            TargetId = targetId;
            Damage = damage;
        }
    }

    /// <summary>병사 하나를 세울 내용 — 종류와, 건물·스테이지가 준 배수.</summary>
    public readonly struct UnitSpawn
    {
        public readonly UnitDef Def;
        public readonly double HpMultiplier;
        public readonly double AttackMultiplier;
        public readonly float Scale;

        public UnitSpawn(UnitDef def, double hpMultiplier, double attackMultiplier, float scale = 1f)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            HpMultiplier = hpMultiplier;
            AttackMultiplier = attackMultiplier;
            Scale = scale;
        }
    }

    /// <summary>
    /// 자동 전투 한 판. 고정 걸음(50ms)으로 돌고, 무작위가 없다 — <b>같은 군대·같은 스테이지면
    /// 결과가 늘 같다.</b>
    ///
    /// 그 성질 때문에 두 가지가 된다. (1) 화면은 같은 시뮬레이션을 한 걸음씩 돌리며 그리기만 하고,
    /// 결과 적용은 Core(<see cref="BattleService.Fight"/>)가 따로 끝까지 돌려서 한다 — 둘이 같다.
    /// (2) 시험과 밸런스 봇이 화면 없이 수천 판을 돌려 본다.
    ///
    /// 규칙은 일부러 단순하다: 가장 가까운 적에게 걸어가고, 사거리에 들면 간격마다 친다.
    /// 같은 편끼리 겹치면 서로 밀어낸다. 진형·병과 상성은 아직 없다.
    /// </summary>
    public sealed class BattleSimulation
    {
        public const long StepMs = 50L;

        /// <summary>앞줄 한 줄에 세우는 병사 수.</summary>
        private const int RowWidth = 6;
        private const float Spacing = 1.3f;

        /// <summary>양 진영이 처음 서는 거리(중앙선에서).</summary>
        private const float StartDistance = 7f;

        private readonly List<BattleUnit> _units = new List<BattleUnit>();
        private readonly List<BattleEvent> _events = new List<BattleEvent>();
        private readonly long _timeLimitMs;

        public long ElapsedMs { get; private set; }
        public bool IsFinished { get; private set; }

        /// <summary>끝났을 때만 의미가 있다. 시간 초과는 아군 패배다.</summary>
        public bool PlayerWon { get; private set; }

        public IReadOnlyList<BattleUnit> Units => _units;

        /// <summary>바로 전 <see cref="Step"/>에서 일어난 일.</summary>
        public IReadOnlyList<BattleEvent> LastEvents => _events;

        public BattleSimulation(IReadOnlyList<UnitSpawn> player, IReadOnlyList<UnitSpawn> enemy, long timeLimitMs)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));

            _timeLimitMs = timeLimitMs;

            Deploy(player, BattleTeam.Player);
            Deploy(enemy, BattleTeam.Enemy);

            if (player.Count == 0 || enemy.Count == 0)
            {
                IsFinished = true;
                PlayerWon = player.Count > 0;
            }
        }

        public long PowerOf(BattleTeam team)
        {
            long sum = 0L;
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Team == team)
                    sum += _units[i].Power;
            }

            return sum;
        }

        public int AliveCount(BattleTeam team)
        {
            int n = 0;
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Team == team && _units[i].IsAlive)
                    n++;
            }

            return n;
        }

        public bool TryGetUnit(int id, out BattleUnit unit)
        {
            unit = id >= 0 && id < _units.Count ? _units[id] : null;
            return unit != null;
        }

        /// <summary>끝날 때까지 돌린다. 제한 시간이 있어 무한히 돌지 않는다.</summary>
        public void RunToEnd()
        {
            while (!IsFinished)
                Step();
        }

        /// <summary>
        /// 근접은 앞줄, 활은 뒷줄 — 같은 종류는 붙여 세운다. 줄은 가운데부터 좌우로 채운다.
        /// </summary>
        private void Deploy(IReadOnlyList<UnitSpawn> spawns, BattleTeam team)
        {
            var melee = new List<UnitSpawn>();
            var ranged = new List<UnitSpawn>();

            for (int i = 0; i < spawns.Count; i++)
            {
                if (spawns[i].Def.IsRanged) ranged.Add(spawns[i]);
                else melee.Add(spawns[i]);
            }

            float sign = team == BattleTeam.Player ? -1f : 1f;
            float depth = PlaceRows(melee, team, sign, 0f);
            PlaceRows(ranged, team, sign, depth);
        }

        /// <summary>
        /// 줄 간격은 그 줄에서 가장 큰 몸에 맞춘다 — 고정 간격이면 거인(반지름 1m 넘음)이 서자마자 겹쳐서
        /// 밀어내기가 첫 몇 초를 잡아먹고 화면에서는 덩어리로 보인다. 사람 병사는 기존 간격 그대로다.
        /// </summary>
        private float PlaceRows(List<UnitSpawn> spawns, BattleTeam team, float sign, float depth)
        {
            for (int start = 0; start < spawns.Count; start += RowWidth)
            {
                int count = Math.Min(RowWidth, spawns.Count - start);

                float step = Spacing;
                for (int k = 0; k < count; k++)
                {
                    UnitSpawn s = spawns[start + k];
                    step = Math.Max(step, s.Def.Radius * s.Scale * 2f + 0.3f);
                }

                float z = sign * (StartDistance + depth + (step - Spacing) * 0.5f);

                for (int k = 0; k < count; k++)
                {
                    UnitSpawn s = spawns[start + k];
                    float x = (k - (count - 1) * 0.5f) * step;

                    long hp = Math.Max(1L, (long)Math.Round(s.Def.Hp * s.HpMultiplier));
                    long attack = Math.Max(0L, (long)Math.Round(s.Def.Attack * s.AttackMultiplier));

                    _units.Add(new BattleUnit(_units.Count, team, s.Def, hp, attack, s.Scale, x, z));
                }

                depth += step;
            }

            return depth;
        }

        /// <summary>한 걸음(50ms) 나아간다. 일어난 일은 <see cref="LastEvents"/>에 남는다.</summary>
        public void Step()
        {
            _events.Clear();

            if (IsFinished)
                return;

            ElapsedMs += StepMs;
            float dt = StepMs / 1000f;

            // 번호 순서로 돈다 — 순서가 매번 같아야 결과가 같다.
            for (int i = 0; i < _units.Count; i++)
            {
                BattleUnit u = _units[i];
                if (!u.IsAlive)
                    continue;

                if (u.CooldownMs > 0L)
                    u.CooldownMs = Math.Max(0L, u.CooldownMs - StepMs);

                BattleUnit target = AcquireTarget(u);
                if (target == null)
                {
                    u.IsMoving = false;
                    continue;
                }

                float dx = target.X - u.X;
                float dz = target.Z - u.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);

                if (dist > 0.0001f)
                {
                    u.FacingX = dx / dist;
                    u.FacingZ = dz / dist;
                }

                float gap = dist - u.Radius - target.Radius;

                if (gap > u.Def.Range)
                {
                    float move = Math.Min(u.Def.MoveSpeed * dt, gap - u.Def.Range);
                    u.X += u.FacingX * move;
                    u.Z += u.FacingZ * move;
                    u.IsMoving = move > 0f;
                    continue;
                }

                u.IsMoving = false;

                if (u.CooldownMs > 0L)
                    continue;

                u.CooldownMs = u.Def.AttackIntervalMs;
                target.Hp = Math.Max(0L, target.Hp - u.Attack);
                _events.Add(new BattleEvent(BattleEventKind.Attack, u.Id, target.Id, u.Attack));

                if (!target.IsAlive)
                {
                    target.IsMoving = false;
                    _events.Add(new BattleEvent(BattleEventKind.Death, u.Id, target.Id, 0L));
                }
            }

            Separate();

            bool playerAlive = AliveCount(BattleTeam.Player) > 0;
            bool enemyAlive = AliveCount(BattleTeam.Enemy) > 0;

            if (!enemyAlive || !playerAlive)
            {
                IsFinished = true;
                PlayerWon = playerAlive;
            }
            else if (ElapsedMs >= _timeLimitMs)
            {
                IsFinished = true;
                PlayerWon = false;
            }
        }

        /// <summary>지금 목표가 살아 있으면 그대로, 아니면 가장 가까운 적. 거리가 같으면 번호가 작은 쪽.</summary>
        private BattleUnit AcquireTarget(BattleUnit u)
        {
            BattleUnit current;
            if (u.TargetId >= 0 && TryGetUnit(u.TargetId, out current) && current.IsAlive)
                return current;

            BattleUnit best = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < _units.Count; i++)
            {
                BattleUnit o = _units[i];
                if (o.Team == u.Team || !o.IsAlive)
                    continue;

                float dx = o.X - u.X;
                float dz = o.Z - u.Z;
                float sq = dx * dx + dz * dz;

                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = o;
                }
            }

            u.TargetId = best == null ? -1 : best.Id;
            return best;
        }

        /// <summary>
        /// 겹친 병사를 서로 밀어낸다. 적과도 민다 — 안 그러면 근접 병사들이 한 점에 포개진다.
        /// 완전히 같은 자리면 번호로 방향을 정한다(나누기 0을 피하고 결과를 매번 같게).
        /// </summary>
        private void Separate()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                BattleUnit a = _units[i];
                if (!a.IsAlive)
                    continue;

                for (int j = i + 1; j < _units.Count; j++)
                {
                    BattleUnit b = _units[j];
                    if (!b.IsAlive)
                        continue;

                    float dx = b.X - a.X;
                    float dz = b.Z - a.Z;
                    float min = a.Radius + b.Radius;
                    float sq = dx * dx + dz * dz;

                    if (sq >= min * min)
                        continue;

                    float dist = (float)Math.Sqrt(sq);
                    float nx, nz;

                    if (dist < 0.0001f)
                    {
                        nx = (i + j) % 2 == 0 ? 1f : -1f;
                        nz = 0f;
                        dist = 0f;
                    }
                    else
                    {
                        nx = dx / dist;
                        nz = dz / dist;
                    }

                    float push = (min - dist) * 0.5f;
                    a.X -= nx * push;
                    a.Z -= nz * push;
                    b.X += nx * push;
                    b.Z += nz * push;
                }
            }
        }
    }
}

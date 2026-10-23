using System;
using System.Collections.Generic;
using Olympus.Core.Battle;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Chapter
{
    /// <summary>본부 레벨에 따른 도적단 한 판. 본부 레벨 이하에서 가장 높은 줄을 쓴다.</summary>
    public sealed class RaidWave
    {
        public int HqLevel { get; }
        public IReadOnlyList<EnemyGroup> Enemies { get; }
        public IReadOnlyList<ResourceCost> RewardResources { get; }

        public RaidWave(int hqLevel, IReadOnlyList<EnemyGroup> enemies, IReadOnlyList<ResourceCost> rewardResources)
        {
            if (enemies == null || enemies.Count == 0)
                throw new ArgumentException("도적단 Lv." + hqLevel + "에 적이 없다.", nameof(enemies));

            HqLevel = hqLevel;
            Enemies = enemies;
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
        }
    }

    /// <summary>
    /// 도적 습격 규칙. 탑포스처럼 기지 둘레에서 <b>늘 무언가 싸우고 있는</b> 느낌을 낸다 — 거인(큰 사건) 사이를
    /// 메우는 작은 싸움이다(2026-10-22 형님). 마을 수비대(고정) + 기른 용사 일부가 나가 막는다.
    /// </summary>
    public sealed class RaidDef
    {
        public int StartHqLevel { get; }
        public long FirstDelayMs { get; }
        public long IntervalMs { get; }

        /// <summary>도착하기 이만큼 전부터 성 밖에 보이며 걸어온다(화면용). 그 전에는 안 보인다.</summary>
        public long ApproachMs { get; }

        public string MilitiaUnitId { get; }
        public int MilitiaCount { get; }
        public double MilitiaMultiplier { get; }

        /// <summary>습격 한 번에 나가는 기른 용사 최대 수. 다 내보내면 300 모으기가 도적 막기에 다 녹는다.</summary>
        public int DefenderCap { get; }

        /// <summary>지면 빼앗기는 목재·석재 비율(%).</summary>
        public int LossPercent { get; }

        /// <summary>도착 뒤 이만큼 지나도 화면이 싸움을 안 붙이면(앱이 꺼져 있었다) Core가 결판낸다.</summary>
        public long AutoResolveMs { get; }

        public int RowWidth { get; }
        public long TimeLimitMs { get; }
        public IReadOnlyList<RaidWave> Waves { get; }

        public RaidDef(int startHqLevel, long firstDelayMs, long intervalMs, long approachMs,
            string militiaUnitId, int militiaCount, double militiaMultiplier, int defenderCap, int lossPercent,
            long autoResolveMs, int rowWidth, long timeLimitMs, IReadOnlyList<RaidWave> waves)
        {
            if (intervalMs <= 0L || firstDelayMs < 0L || approachMs < 0L || autoResolveMs <= 0L)
                throw new ArgumentOutOfRangeException(nameof(intervalMs), "도적 습격 시간 값이 잘못됐다.");
            if (waves == null || waves.Count == 0)
                throw new ArgumentException("도적단 줄이 없다.", nameof(waves));

            StartHqLevel = startHqLevel;
            FirstDelayMs = firstDelayMs;
            IntervalMs = intervalMs;
            ApproachMs = approachMs;
            MilitiaUnitId = militiaUnitId;
            MilitiaCount = Math.Max(0, militiaCount);
            MilitiaMultiplier = militiaMultiplier > 0.0 ? militiaMultiplier : 1.0;
            DefenderCap = Math.Max(0, defenderCap);
            LossPercent = Math.Max(0, Math.Min(100, lossPercent));
            AutoResolveMs = autoResolveMs;
            RowWidth = rowWidth;
            TimeLimitMs = timeLimitMs > 0L ? timeLimitMs : 60_000L;
            Waves = waves;
        }

        public RaidWave WaveFor(int hqLevel)
        {
            RaidWave best = Waves[0];
            for (int i = 0; i < Waves.Count; i++)
            {
                if (Waves[i].HqLevel <= hqLevel && Waves[i].HqLevel >= best.HqLevel)
                    best = Waves[i];
            }

            return best;
        }
    }

    public enum RaidPhase
    {
        /// <summary>도적이 아직 안 온다(본부 레벨 미달 또는 규칙 없음).</summary>
        Off = 0,

        /// <summary>다음 습격을 기다린다 — 아직 안 보인다.</summary>
        Waiting = 1,

        /// <summary>성 밖에 보이며 걸어온다.</summary>
        Approaching = 2,

        /// <summary>도착 — 수비대가 나가 싸울 차례.</summary>
        Arrived = 3,
    }

    public readonly struct RaidView
    {
        public readonly RaidPhase Phase;
        public readonly long ArrivesAtUnixMs;

        /// <summary>0 = 막 보임, 1 = 도착. Approaching일 때만 의미.</summary>
        public readonly float Progress;

        public readonly RaidWave Wave;

        /// <summary>몇 번째 습격인가(0부터). 화면이 오는 방향을 돌리는 데 쓴다.</summary>
        public readonly int Number;

        public RaidView(RaidPhase phase, long arrivesAt, float progress, RaidWave wave, int number)
        {
            Phase = phase;
            ArrivesAtUnixMs = arrivesAt;
            Progress = progress;
            Wave = wave;
            Number = number;
        }
    }

    public readonly struct RaidOutcome
    {
        public readonly bool Resolved;
        public readonly bool Won;
        public readonly bool Automatic;
        public readonly int TroopsSent;
        public readonly int TroopsLost;
        public readonly IReadOnlyList<ResourceCost> Gained;
        public readonly IReadOnlyList<ResourceCost> Lost;

        public RaidOutcome(bool resolved, bool won, bool automatic, int troopsSent, int troopsLost,
            IReadOnlyList<ResourceCost> gained, IReadOnlyList<ResourceCost> lost)
        {
            Resolved = resolved;
            Won = won;
            Automatic = automatic;
            TroopsSent = troopsSent;
            TroopsLost = troopsLost;
            Gained = gained ?? Array.Empty<ResourceCost>();
            Lost = lost ?? Array.Empty<ResourceCost>();
        }
    }

    /// <summary>
    /// 도적 습격. 다음 도착 시각 하나를 든다. 화면은 도착하면 <see cref="Engage"/>로 싸움을 잡고 재생한 뒤
    /// <see cref="Resolve"/>를 부른다(거인·스테이지와 같은 재생 → 같은 입력으로 다시 돌려 적용).
    ///
    /// 거인과 달리 앱이 꺼져 있으면 Core가 알아서 결판낸다(<see cref="RaidDef.AutoResolveMs"/>) — 작은 싸움까지
    /// 다 보여 주려고 기다리면, 돌아왔을 때 밀린 습격이 줄을 선다. 한 번에 하나만 쌓인다(다음 습격은 결판 뒤에 잡힌다).
    /// </summary>
    public sealed class RaidService
    {
        public const string NextKey = "raid.next";
        public const string EngagedKey = "raid.engaged";
        public const string CountKey = "raid.count";
        public const string WonKey = "raid.won";

        private readonly StateStore _store;
        private readonly IClock _clock;
        private readonly ChapterConfig _config;
        private readonly ProgressionConfig _progression;
        private readonly BattleService _battles;

        /// <summary>Core가 저절로 결판낸 습격(앱이 꺼져 있던 사이). 화면이 "자리를 비운 사이 도적을 막았다"를 띄운다.</summary>
        public event Action<RaidOutcome> AutoResolved;

        public RaidService(StateStore store, IClock clock, ChapterConfig config, ProgressionConfig progression, BattleService battles)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ChapterConfig.Empty;
            _progression = progression ?? ProgressionConfig.Empty;
            _battles = battles ?? throw new ArgumentNullException(nameof(battles));
        }

        public RaidDef Def => _config.Raids;

        private int Hq => ProgressionRules.HqLevel(_store.State, _progression);

        public RaidView Describe()
        {
            RaidDef def = Def;
            CountPool c = _store.State.Counters;
            int number = (int)c.Get(CountKey);
            long next = c.Get(NextKey);

            if (def == null || next == 0L)
                return new RaidView(RaidPhase.Off, 0L, 0f, null, number);

            RaidWave wave = def.WaveFor(Hq);
            long now = _clock.NowUnixMs;

            if (now >= next)
                return new RaidView(RaidPhase.Arrived, next, 1f, wave, number);

            long remaining = next - now;
            if (def.ApproachMs <= 0L || remaining > def.ApproachMs)
                return new RaidView(RaidPhase.Waiting, next, 0f, wave, number);

            return new RaidView(RaidPhase.Approaching, next, 1f - (float)remaining / def.ApproachMs, wave, number);
        }

        public bool IsEngaged => _store.State.Counters.Get(EngagedKey) > 0L;

        /// <summary>첫 습격 시각을 잡고, 앱이 꺼져 있던 사이 도착해 버린 습격을 결판낸다. 매 프레임 불러도 된다.</summary>
        public void Tick()
        {
            RaidDef def = Def;
            if (def == null)
                return;

            CountPool c = _store.State.Counters;
            long now = _clock.NowUnixMs;
            long next = c.Get(NextKey);

            if (next == 0L)
            {
                if (Hq >= def.StartHqLevel)
                    _store.Apply(new StateDelta().WithCounter(CountChange.Absolute(NextKey, now + def.FirstDelayMs)));
                return;
            }

            if (now < next)
                return;

            long engaged = c.Get(EngagedKey);
            long since = engaged > 0L ? engaged : next;
            if (now - since >= def.AutoResolveMs)
            {
                RaidOutcome o = Resolve(automatic: true);
                if (o.Resolved)
                    AutoResolved?.Invoke(o);
            }
        }

        /// <summary>화면이 싸움을 잡는다 — 재생하는 동안 Core가 저절로 결판내지 않게.</summary>
        public bool Engage()
        {
            if (Describe().Phase != RaidPhase.Arrived)
                return false;

            _store.Apply(new StateDelta().WithCounter(CountChange.Absolute(EngagedKey, _clock.NowUnixMs)));
            return true;
        }

        public BattleSimulation CreateSimulation()
        {
            RaidDef def = Def;
            if (def == null)
                throw new InvalidOperationException("도적 습격 규칙이 없다.");

            int sent;
            return Build(def, def.WaveFor(Hq), out sent);
        }

        private BattleSimulation Build(RaidDef def, RaidWave wave, out int troopsSent)
        {
            var player = new List<UnitSpawn>();
            UnitDef unit;

            if (_battles.Config.TryGetUnit(def.MilitiaUnitId, out unit))
            {
                double hp = _battles.BuffMultiplier(unit.UnitId, BattleStat.Hp) * def.MilitiaMultiplier;
                double atk = _battles.BuffMultiplier(unit.UnitId, BattleStat.Attack) * def.MilitiaMultiplier;
                for (int i = 0; i < def.MilitiaCount; i++)
                    player.Add(new UnitSpawn(unit, hp, atk, 1f, 0));
            }

            troopsSent = 0;
            TrainingDef training = _config.Training;
            if (training != null && _battles.Config.TryGetUnit(training.UnitId, out unit))
            {
                long troops = _store.State.Counters.Get(ChapterKeys.Troops(training.UnitId));
                troopsSent = (int)Math.Min(troops, def.DefenderCap);
                double hp = _battles.BuffMultiplier(unit.UnitId, BattleStat.Hp);
                double atk = _battles.BuffMultiplier(unit.UnitId, BattleStat.Attack);
                for (int i = 0; i < troopsSent; i++)
                    player.Add(new UnitSpawn(unit, hp, atk, 1f, 1));
            }

            var enemy = new List<UnitSpawn>();
            for (int i = 0; i < wave.Enemies.Count; i++)
            {
                EnemyGroup g = wave.Enemies[i];
                if (!_battles.Config.TryGetUnit(g.UnitId, out unit))
                    continue;
                for (int k = 0; k < g.Count; k++)
                    enemy.Add(new UnitSpawn(unit, g.Multiplier, g.Multiplier));
            }

            return new BattleSimulation(player, enemy, def.TimeLimitMs, def.RowWidth);
        }

        /// <summary>
        /// 끝까지 싸우고 적용한다. 이기면 전리품, 지면 목재·석재 일부를 빼앗긴다. 나간 용사 중 쓰러진 만큼 줄어든다.
        /// 다음 습격 시각을 잡는다. 도착 전이면 아무것도 하지 않는다.
        /// </summary>
        public RaidOutcome Resolve(bool automatic = false)
        {
            RaidDef def = Def;
            if (def == null || Describe().Phase != RaidPhase.Arrived)
                return new RaidOutcome(false, false, automatic, 0, 0, null, null);

            RaidWave wave = def.WaveFor(Hq);
            int sent;
            BattleSimulation sim = Build(def, wave, out sent);
            sim.RunToEnd();

            int survivors = 0;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                BattleUnit u = sim.Units[i];
                if (u.Team == BattleTeam.Player && u.Tag == 1 && u.IsAlive)
                    survivors++;
            }

            int lost = sent - survivors;
            long now = _clock.NowUnixMs;
            var delta = new StateDelta()
                .WithCounter(CountChange.Absolute(NextKey, now + def.IntervalMs))
                .WithCounter(CountChange.Absolute(EngagedKey, 0))
                .WithCounter(CountChange.Delta(CountKey, 1));

            TrainingDef training = _config.Training;
            if (training != null && lost > 0)
                delta.WithCounter(CountChange.Delta(ChapterKeys.Troops(training.UnitId), -lost));

            var gained = new List<ResourceCost>();
            var taken = new List<ResourceCost>();

            if (sim.PlayerWon)
            {
                delta.WithCounter(CountChange.Delta(WonKey, 1)).WithResources(wave.RewardResources);
                gained.AddRange(wave.RewardResources);
            }
            else
            {
                foreach (ResourceKind kind in new[] { ResourceKind.Wood, ResourceKind.Stone })
                {
                    long amount = _store.State.Resources.Get(kind) * def.LossPercent / 100;
                    if (amount <= 0L)
                        continue;
                    delta.WithResource(ResourceChange.Delta(kind, -amount));
                    taken.Add(new ResourceCost(kind, amount));
                }
            }

            _store.Apply(delta);
            return new RaidOutcome(true, sim.PlayerWon, automatic, sent, lost, gained, taken);
        }
    }
}

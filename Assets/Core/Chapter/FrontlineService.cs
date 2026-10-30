using System;
using System.Collections.Generic;
using Olympus.Core.Battle;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Chapter
{
    /// <summary>전선 끝의 적 기지 하나. 차례대로 무너뜨린다.</summary>
    public sealed class OutpostDef
    {
        public string OutpostId { get; }
        public string NameKey { get; }

        /// <summary>무너뜨리는 데 드는 누적 피해.</summary>
        public long Hp { get; }

        /// <summary>
        /// 버티는 힘(초당). 우리 전선 화력이 이보다 약하면 깎이지 않는다 — "더 안 키우면 밀린다".
        /// 탑포스의 두 번째 전초기지가 그렇다(형님 관찰): 첫 기지는 그냥 밀리고, 다음 기지에서 막혀 본부·장비를 올리게 된다.
        /// </summary>
        public long Resist { get; }

        /// <summary>이 본부 레벨이 돼야 공격이 시작된다(그 전에는 보이기만).</summary>
        public int MinHqLevel { get; }

        public string EnemyUnitId { get; }
        public IReadOnlyList<ResourceCost> RewardResources { get; }
        public IReadOnlyList<ItemGrant> RewardItems { get; }
        public string StoryKey { get; }

        public OutpostDef(string outpostId, string nameKey, long hp, long resist, int minHqLevel, string enemyUnitId,
            IReadOnlyList<ResourceCost> rewardResources, IReadOnlyList<ItemGrant> rewardItems, string storyKey)
        {
            if (string.IsNullOrEmpty(outpostId)) throw new ArgumentException("전초기지 이름이 없다.", nameof(outpostId));
            if (hp <= 0L || resist < 0L) throw new ArgumentOutOfRangeException(nameof(hp), outpostId + ": 체력·버팀이 잘못됐다.");

            OutpostId = outpostId;
            NameKey = nameKey;
            Hp = hp;
            Resist = resist;
            MinHqLevel = minHqLevel;
            EnemyUnitId = enemyUnitId;
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
            RewardItems = rewardItems ?? Array.Empty<ItemGrant>();
            StoryKey = storyKey;
        }
    }

    /// <summary>
    /// 전선 규칙. 화력은 건물이 주는 군대(battle.json의 army·buffs)에서 나온다 — 스테이지 전투와 같은 출처라
    /// "병영을 올리면 세진다"가 두 곳에서 같게 읽힌다.
    /// </summary>
    public sealed class FrontlineDef
    {
        public IReadOnlyList<OutpostDef> Outposts { get; }

        /// <summary>피해 1000마다 떨어지는 전리품. 적을 죽이며 자원을 얻는 고리(탑포스의 코인 비).</summary>
        public IReadOnlyList<ResourceCost> LootPerThousand { get; }

        public FrontlineDef(IReadOnlyList<OutpostDef> outposts, IReadOnlyList<ResourceCost> lootPerThousand)
        {
            if (outposts == null || outposts.Count == 0)
                throw new ArgumentException("전초기지가 없다.", nameof(outposts));

            Outposts = outposts;
            LootPerThousand = lootPerThousand ?? Array.Empty<ResourceCost>();
        }
    }

    public readonly struct FrontView
    {
        /// <summary>지금 상대하는 기지. 다 무너뜨렸으면 null.</summary>
        public readonly OutpostDef Outpost;

        /// <summary>몇 번째 기지인가(0부터). 무너뜨린 수와 같다.</summary>
        public readonly int Index;

        public readonly long Damage;

        /// <summary>우리 전선 화력(초당).</summary>
        public readonly long Power;

        /// <summary>실제로 깎이는 속도(초당) = 화력 − 버팀(0 이상).</summary>
        public readonly long Rate;

        /// <summary>본부 레벨이 모자라 아직 공격하지 않는다.</summary>
        public readonly bool Waiting;

        public FrontView(OutpostDef outpost, int index, long damage, long power, long rate, bool waiting)
        {
            Outpost = outpost;
            Index = index;
            Damage = damage;
            Power = power;
            Rate = rate;
            Waiting = waiting;
        }

        public bool HasOutpost => Outpost != null;

        /// <summary>화력이 버팀보다 약하다 — 밀리고 있다. 화면이 "더 강해져야 한다"를 띄운다.</summary>
        public bool Stalled => HasOutpost && !Waiting && Rate <= 0L;

        public float Fraction => HasOutpost ? Math.Min(1f, (float)Damage / Outpost.Hp) : 1f;

        /// <summary>이 속도로 무너지기까지(ms). 밀리고 있으면 -1.</summary>
        public long RemainingMs => !HasOutpost ? 0L : Rate <= 0L ? -1L : (Outpost.Hp - Damage) * 1000L / Rate;
    }

    /// <summary>
    /// 성문 밖 상시 전선(형님 결정 2026-10-23: 처음부터 전쟁 중인 군영). 우리 병사는 끊임없이 나가 싸우고,
    /// 전선 끝의 적 기지를 차례로 무너뜨린다. 적은 기지 안으로 들어오지 않는다 — 져도 기지는 안 부서진다.
    ///
    /// 계산은 생산(<see cref="ProductionService"/>)과 같은 방식이다: "마지막으로 정산한 시각 + 그때까지 쌓인 피해"를 들고,
    /// 지금 피해 = 쌓인 피해 + 속도 × 지난 시간. 앱을 꺼 둬도 전선은 계속 싸운다.
    ///
    /// 속도는 건물에서 나오므로 건물이 바뀌는 순간 정산해야 한다. 바뀐 <b>뒤에</b> 통지가 오므로, 바뀌기 전 속도를
    /// 기억해 두었다가(<see cref="_rateBefore"/>) 그 속도로 지난 구간을 정산한 뒤 새 속도로 갈아탄다.
    /// 화면은 매 프레임 상태를 쓰지 않고 <see cref="Describe"/>로 그 순간 값을 계산해 그린다.
    /// </summary>
    public sealed class FrontlineService
    {
        public const string IndexKey = "front.outpost";
        public const string DamageKey = "front.damage";
        public const string AnchorKey = "front.anchor";

        /// <summary>전리품을 자원에 넣는 간격. 매 프레임 쓰면 세이브·통지가 너무 잦고, 너무 드물면 자원이 멈춰 보인다.</summary>
        public const long SettleIntervalMs = 5000L;

        private readonly StateStore _store;
        private readonly IClock _clock;
        private readonly ChapterConfig _config;
        private readonly ProgressionConfig _progression;
        private readonly BattleService _battles;

        private long _rateBefore = -1L;
        private bool _settling;

        /// <summary>기지를 하나 무너뜨렸다. 화면이 이야기·보상을 띄운다.</summary>
        public event Action<OutpostDef> OutpostDestroyed;

        public FrontlineService(StateStore store, IClock clock, ChapterConfig config, ProgressionConfig progression,
            BattleService battles)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _config = config ?? ChapterConfig.Empty;
            _progression = progression ?? ProgressionConfig.Empty;
            _battles = battles ?? throw new ArgumentNullException(nameof(battles));

            _store.Changed += OnChanged;
        }

        public FrontlineDef Def => _config.Frontline;

        private int Hq => ProgressionRules.HqLevel(_store.State, _progression);

        /// <summary>
        /// 전선 화력(초당 피해). 건물이 주는 군대 전원의 초당 공격 × 체력 배수의 반(오래 버티면 더 오래 친다 —
        /// 치유의 집·성문이 화력에 보탬이 되게).
        /// </summary>
        public long Power()
        {
            List<UnitSpawn> army = _battles.BuildPlayerArmy();
            double sum = 0.0;

            for (int i = 0; i < army.Count; i++)
            {
                UnitSpawn s = army[i];
                double dps = s.Def.Attack * s.AttackMultiplier * 1000.0 / s.Def.AttackIntervalMs;
                sum += dps * (0.5 + 0.5 * s.HpMultiplier);
            }

            return (long)Math.Round(sum);
        }

        private OutpostDef CurrentOutpost(out int index)
        {
            index = (int)_store.State.Counters.Get(IndexKey);
            FrontlineDef def = Def;
            return def != null && index < def.Outposts.Count ? def.Outposts[index] : null;
        }

        private long RateNow(OutpostDef outpost)
        {
            if (outpost == null || Hq < outpost.MinHqLevel)
                return 0L;

            return Math.Max(0L, Power() - outpost.Resist);
        }

        public FrontView Describe()
        {
            int index;
            OutpostDef outpost = CurrentOutpost(out index);
            long power = Def == null ? 0L : Power();

            if (outpost == null)
                return new FrontView(null, index, 0L, power, 0L, false);

            bool waiting = Hq < outpost.MinHqLevel;
            long rate = waiting ? 0L : Math.Max(0L, power - outpost.Resist);
            long damage = DamageAt(_clock.NowUnixMs, rate);

            return new FrontView(outpost, index, Math.Min(damage, outpost.Hp), power, rate, waiting);
        }

        private long DamageAt(long now, long rate)
        {
            CountPool c = _store.State.Counters;
            long anchor = c.Get(AnchorKey);
            long stored = c.Get(DamageKey);
            if (anchor == 0L || now <= anchor)
                return stored;

            return stored + rate * (now - anchor) / 1000L;
        }

        /// <summary>
        /// 무너진 기지를 정리하고 다음 기지로 넘긴다. 매 프레임 불러도 된다 — 상태는 기지가 무너질 때만 쓴다.
        /// 한 번에 여러 기지가 무너질 수 있다(오래 꺼 둔 뒤).
        /// </summary>
        public void Tick()
        {
            if (Def == null)
                return;

            if (_store.State.Counters.Get(AnchorKey) == 0L)
            {
                Settle(_clock.NowUnixMs, 0L);
                return;
            }

            // 무너짐을 먼저 본다 — 정산을 먼저 하면 기지 체력을 넘친 피해가 앞 기지에 쌓여 다음 기지로 안 넘어간다.
            for (int guard = 0; guard < 16; guard++)
            {

                int index;
                OutpostDef outpost = CurrentOutpost(out index);
                if (outpost == null)
                    return;

                long now = _clock.NowUnixMs;
                long rate = RateNow(outpost);
                long damage = DamageAt(now, rate);
                if (damage < outpost.Hp)
                    return;

                // 무너진 시각을 거꾸로 구해 남은 시간을 다음 기지로 넘긴다 — 꺼 둔 사이의 진행을 버리지 않는다.
                long stored = _store.State.Counters.Get(DamageKey);
                long anchor = _store.State.Counters.Get(AnchorKey);
                long fellAt = rate > 0L ? anchor + (outpost.Hp - stored) * 1000L / rate : now;
                fellAt = Math.Min(now, Math.Max(anchor, fellAt));

                var delta = new StateDelta()
                    .WithCounter(CountChange.Absolute(IndexKey, index + 1))
                    .WithCounter(CountChange.Absolute(DamageKey, 0))
                    .WithCounter(CountChange.Absolute(AnchorKey, fellAt))
                    .WithResources(outpost.RewardResources);
                AppendLoot(delta, outpost.Hp - stored);

                for (int i = 0; i < outpost.RewardItems.Count; i++)
                    delta.WithItem(CountChange.Delta(outpost.RewardItems[i].ItemId, outpost.RewardItems[i].Amount));

                Apply(delta);
                OutpostDestroyed?.Invoke(outpost);
            }

            if (_rateBefore >= 0L && _clock.NowUnixMs - _store.State.Counters.Get(AnchorKey) >= SettleIntervalMs)
                Settle(_clock.NowUnixMs, _rateBefore);
        }

        /// <summary>건물이 바뀐 직후 — 바뀌기 전 속도로 지난 구간을 정산하고 새 속도로 갈아탄다.</summary>
        private void OnChanged(StateDelta delta)
        {
            if (_settling || Def == null)
                return;

            if (delta.Buildings != null && _rateBefore >= 0L)
                Settle(_clock.NowUnixMs, _rateBefore);

            int index;
            _rateBefore = RateNow(CurrentOutpost(out index));
        }

        private void Settle(long now, long rate)
        {
            long anchor = _store.State.Counters.Get(AnchorKey);
            long gained = anchor == 0L ? 0L : rate * Math.Max(0L, now - anchor) / 1000L;

            // 지금 기지 체력을 넘겨 쌓지 않는다 — 넘친 몫은 무너짐 처리(Tick)가 다음 기지로 넘긴다.
            int index;
            OutpostDef outpost = CurrentOutpost(out index);
            if (outpost != null)
                gained = Math.Min(gained, Math.Max(0L, outpost.Hp - _store.State.Counters.Get(DamageKey)));

            var delta = new StateDelta()
                .WithCounter(CountChange.Absolute(AnchorKey, now));
            if (gained > 0L)
            {
                delta.WithCounter(CountChange.Delta(DamageKey, gained));
                AppendLoot(delta, gained);
            }

            Apply(delta);
        }

        private void AppendLoot(StateDelta delta, long damage)
        {
            if (damage <= 0L)
                return;

            for (int i = 0; i < Def.LootPerThousand.Count; i++)
            {
                ResourceCost per = Def.LootPerThousand[i];
                long amount = per.Amount * damage / 1000L;
                if (amount > 0L)
                    delta.WithResource(ResourceChange.Delta(per.Kind, amount));
            }
        }

        private void Apply(StateDelta delta)
        {
            _settling = true;
            try
            {
                _store.Apply(delta);
            }
            finally
            {
                _settling = false;
            }

            int index;
            _rateBefore = RateNow(CurrentOutpost(out index));
        }

        /// <summary>쌓인 전리품을 지금까지로 정산한다(화면이 자원 숫자를 맞출 때·세이브 직전).</summary>
        public void SettleNow()
        {
            if (Def == null || _rateBefore < 0L)
                return;

            Settle(_clock.NowUnixMs, _rateBefore);
        }

        public void Dispose()
        {
            _store.Changed -= OnChanged;
        }
    }
}

using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// 본부 레벨 하나 — 그 레벨로 올라가는 조건과, 올라갔을 때 바뀌는 것.
    ///
    /// 조건이 세 갈래(<see cref="Requirements"/> · <see cref="Costs"/> · <see cref="DurationMs"/>)인
    /// 것은 탑포스의 "기지 레벨 3조건"(별 + 코인 + 선행 조건, 그리고 타이머)을 그대로 옮긴 것이다.
    /// 앞 레벨은 조건이 가볍고(Lv.2는 즉시), Lv.4부터 "선행 건물 레벨"이 들어간다 —
    /// 탑포스가 Lv.3→4에서 조건 문법을 바꾸는 지점과 맞췄다.
    /// </summary>
    public sealed class HqLevelDef
    {
        public int Level { get; }

        /// <summary>쓰지 않고 확인만 하는 조건(번영도·선행 건물 레벨).</summary>
        public IReadOnlyList<Requirement> Requirements { get; }

        /// <summary>올릴 때 빠지는 자원.</summary>
        public IReadOnlyList<ResourceCost> Costs { get; }

        public long DurationMs { get; }

        /// <summary>
        /// 이 레벨에서 동시에 돌릴 수 있는 공사 수. <b>0이면 제한 없음</b>이다 —
        /// 탑포스는 Lv.3까지 대기열 개념이 아예 없고(전부 초 단위라 필요 없다) Lv.4에서
        /// "망치 0/1"이 처음 생긴다. 그 "없던 것이 생긴다"를 표현하려면 "무제한" 상태가 필요하다.
        /// </summary>
        public int BuildQueueSlots { get; }

        /// <summary>도달하면 받는 것.</summary>
        public IReadOnlyList<ResourceCost> RewardResources { get; }

        public IReadOnlyList<ItemGrant> RewardItems { get; }

        /// <summary>도달했을 때 띄우는 이야기 한 줄(로컬라이즈 키). 비워도 된다.</summary>
        public string StoryKey { get; }

        public HqLevelDef(
            int level,
            IReadOnlyList<Requirement> requirements,
            IReadOnlyList<ResourceCost> costs,
            long durationMs,
            int buildQueueSlots,
            IReadOnlyList<ResourceCost> rewardResources,
            IReadOnlyList<ItemGrant> rewardItems,
            string storyKey)
        {
            if (level < 1)
                throw new ArgumentOutOfRangeException(nameof(level), "본부 레벨은 1부터다.");
            if (durationMs < 0L)
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (buildQueueSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(buildQueueSlots));

            Level = level;
            Requirements = requirements ?? Array.Empty<Requirement>();
            Costs = costs ?? Array.Empty<ResourceCost>();
            DurationMs = durationMs;
            BuildQueueSlots = buildQueueSlots;
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
            RewardItems = rewardItems ?? Array.Empty<ItemGrant>();
            StoryKey = storyKey;
        }
    }

    /// <summary>
    /// 본부가 아닌 건물의 레벨 한 칸 — 그 레벨로 올릴 때 비용 배수와 걸리는 시간.
    ///
    /// 건물마다 레벨 표를 따로 두지 않고 "복구 비용 × 배수" 한 줄로 둔 이유 — 건물이 21종이라
    /// 표를 따로 두면 숫자가 수백 개가 되고, 곡선을 통째로 옮기고 싶을 때 손댈 곳이 21군데다.
    /// 지금은 곡선 모양을 찾는 단계라 손잡이가 적어야 한다. 건물별로 갈라야 할 이유가 생기면 그때 쪼갠다.
    /// </summary>
    public sealed class UpgradeStep
    {
        public int Level { get; }
        public double CostMultiplier { get; }
        public long DurationMs { get; }

        public UpgradeStep(int level, double costMultiplier, long durationMs)
        {
            if (level < 2)
                throw new ArgumentOutOfRangeException(nameof(level), "업그레이드는 Lv.2부터다(Lv.1은 복구).");
            if (costMultiplier < 0.0)
                throw new ArgumentOutOfRangeException(nameof(costMultiplier));
            if (durationMs < 0L)
                throw new ArgumentOutOfRangeException(nameof(durationMs));

            Level = level;
            CostMultiplier = costMultiplier;
            DurationMs = durationMs;
        }
    }

    /// <summary>가속 아이템 한 종류 — 공사 끝나는 시각을 얼마나 당기나.</summary>
    public sealed class SpeedUpItemDef
    {
        public string ItemId { get; }
        public string DisplayNameKey { get; }
        public long ReduceMs { get; }

        public SpeedUpItemDef(string itemId, string displayNameKey, long reduceMs)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("아이템 이름은 비울 수 없다.", nameof(itemId));
            if (reduceMs <= 0L)
                throw new ArgumentOutOfRangeException(nameof(reduceMs));

            ItemId = itemId;
            DisplayNameKey = displayNameKey;
            ReduceMs = reduceMs;
        }
    }

    /// <summary>아이템 지급 한 줄.</summary>
    public readonly struct ItemGrant
    {
        public readonly string ItemId;
        public readonly long Amount;

        public ItemGrant(string itemId, long amount)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("아이템 이름은 비울 수 없다.", nameof(itemId));
            if (amount < 0L)
                throw new ArgumentOutOfRangeException(nameof(amount));

            ItemId = itemId;
            Amount = amount;
        }
    }

    /// <summary>
    /// 진척 규칙 한 벌 — 본부 레벨 표, 건물 레벨 곡선, 가속 아이템, 시작 지급, 임무, 기능 해금.
    ///
    /// 전부 데이터(<c>Assets/Data/progression.json</c> 외)에서 온다. 코드에 숫자를 두지 않는 이유는
    /// 이 숫자들이 "눈으로·손으로 돌려 보며 정하는 값"이라서다 — 기획이 고칠 때마다 빌드가
    /// 필요하면 그 반복이 안 돈다.
    /// </summary>
    public sealed class ProgressionConfig
    {
        private readonly Dictionary<int, HqLevelDef> _hqLevels = new Dictionary<int, HqLevelDef>();
        private readonly Dictionary<int, UpgradeStep> _upgradeSteps = new Dictionary<int, UpgradeStep>();
        private readonly Dictionary<string, SpeedUpItemDef> _speedUps = new Dictionary<string, SpeedUpItemDef>();
        private readonly List<SpeedUpItemDef> _speedUpOrder = new List<SpeedUpItemDef>();

        /// <summary>본부 역할을 하는 건물 종류. 그 건물의 레벨이 곧 본부 레벨이다. null이면 본부가 없다.</summary>
        public string HqDefId { get; }

        public IReadOnlyList<ResourceCost> StartingResources { get; }
        public IReadOnlyList<ItemGrant> StartingItems { get; }

        public QuestChain Quests { get; }
        public FeatureUnlockTable Features { get; }

        public ProgressionConfig(
            string hqDefId,
            IReadOnlyList<ResourceCost> startingResources = null,
            IReadOnlyList<ItemGrant> startingItems = null,
            QuestChain quests = null,
            FeatureUnlockTable features = null)
        {
            HqDefId = hqDefId;
            StartingResources = startingResources ?? Array.Empty<ResourceCost>();
            StartingItems = startingItems ?? Array.Empty<ItemGrant>();
            Quests = quests ?? new QuestChain();
            Features = features ?? new FeatureUnlockTable();
        }

        /// <summary>본부도 레벨 곡선도 없는 빈 규칙. 진척 없이 복구만 시험할 때 쓴다.</summary>
        public static ProgressionConfig Empty => new ProgressionConfig(null);

        public void AddHqLevel(HqLevelDef def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));
            if (_hqLevels.ContainsKey(def.Level))
                throw new InvalidOperationException("본부 레벨이 중복이다: " + def.Level);

            _hqLevels.Add(def.Level, def);
        }

        public void AddUpgradeStep(UpgradeStep step)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (_upgradeSteps.ContainsKey(step.Level))
                throw new InvalidOperationException("건물 레벨 단계가 중복이다: " + step.Level);

            _upgradeSteps.Add(step.Level, step);
        }

        public void AddSpeedUp(SpeedUpItemDef def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));
            if (_speedUps.ContainsKey(def.ItemId))
                throw new InvalidOperationException("가속 아이템이 중복이다: " + def.ItemId);

            _speedUps.Add(def.ItemId, def);
            _speedUpOrder.Add(def);
        }

        public bool TryGetHqLevel(int level, out HqLevelDef def) => _hqLevels.TryGetValue(level, out def);

        public bool TryGetUpgradeStep(int level, out UpgradeStep step) => _upgradeSteps.TryGetValue(level, out step);

        public bool TryGetSpeedUp(string itemId, out SpeedUpItemDef def)
        {
            if (itemId == null)
            {
                def = null;
                return false;
            }

            return _speedUps.TryGetValue(itemId, out def);
        }

        /// <summary>데이터에 적힌 순서(보통 짧은 것부터). 화면에 버튼을 늘어놓는 순서다.</summary>
        public IReadOnlyList<SpeedUpItemDef> SpeedUps => _speedUpOrder;

        /// <summary>
        /// 데이터끼리 맞는지 본다. 문제를 문장으로 돌려준다(없으면 빈 목록).
        ///
        /// 특히 "달성할 수 없는 조건"을 잡는다 — 본부 Lv.4 조건에 Lv.4에야 열리는 건물을 걸면
        /// 게임은 안 터지고 그냥 영원히 막힌다. 그건 플레이해서 거기까지 가 봐야 알게 되는데,
        /// 여기서 잡으면 켜는 순간 안다.
        /// </summary>
        public List<string> Validate(BuildingCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var problems = new List<string>();

            if (!string.IsNullOrEmpty(HqDefId))
            {
                BuildingDef hq;
                if (!catalog.TryGet(HqDefId, out hq))
                    problems.Add("본부 건물 '" + HqDefId + "'이 건물 정의에 없다.");
                else if (hq.UnlockHqLevel != 0)
                    problems.Add("본부 건물의 unlockHqLevel은 0이어야 한다(지금 " + hq.UnlockHqLevel + ") — 본부 레벨은 본부를 복구해야 1이 된다.");
            }

            int max = MaxHqLevel;
            for (int level = 1; level <= max; level++)
            {
                HqLevelDef def;
                if (!_hqLevels.TryGetValue(level, out def))
                {
                    problems.Add("본부 레벨 " + level + "이 비어 있다(1부터 " + max + "까지 빠짐없이 있어야 한다).");
                    continue;
                }

                for (int i = 0; i < def.Requirements.Count; i++)
                {
                    CheckReachable(def.Requirements[i], level - 1, catalog, "본부 Lv." + level + " 조건", problems);
                }

                CheckItems(def.RewardItems, "본부 Lv." + level + " 보상", problems);
            }

            // 건물은 본부 레벨까지 오를 수 있어야 한다 — 단계가 빠지면 그 레벨에서 업그레이드 버튼이 영원히 "최고 레벨"이다.
            for (int level = 2; level <= max; level++)
            {
                if (!_upgradeSteps.ContainsKey(level))
                    problems.Add("건물 레벨 단계 " + level + "이 없다 — 본부가 Lv." + max + "까지 있으므로 건물도 그만큼 올라가야 한다.");
            }

            for (int i = 0; i < Quests.Count; i++)
            {
                QuestDef q = Quests[i];
                CheckReachable(q.Objective, max, catalog, "임무 " + q.QuestId, problems);
                CheckItems(q.RewardItems, "임무 " + q.QuestId + " 보상", problems);

                BuildingDef focus;
                if (!string.IsNullOrEmpty(q.FocusDefId) && !catalog.TryGet(q.FocusDefId, out focus))
                    problems.Add("임무 " + q.QuestId + "의 focusDefId '" + q.FocusDefId + "'가 건물 정의에 없다.");
            }

            CheckItems(StartingItems, "시작 아이템", problems);
            return problems;
        }

        /// <summary>
        /// 건물 레벨 조건이 본부 <paramref name="hqLevelAtCheck"/>에서 달성 가능한지 본다 —
        /// 그 건물이 그때 열려 있어야 하고, 요구 레벨이 그때의 상한(= 본부 레벨)을 넘으면 안 된다.
        /// </summary>
        private void CheckReachable(Requirement r, int hqLevelAtCheck, BuildingCatalog catalog, string where, List<string> problems)
        {
            if (r.Kind != RequirementKind.BuildingLevel)
                return;

            BuildingDef def;
            if (!catalog.TryGet(r.Target, out def))
            {
                problems.Add(where + ": 건물 '" + r.Target + "'이 건물 정의에 없다.");
                return;
            }

            if (r.Target == HqDefId)
                return;

            if (def.UnlockHqLevel > hqLevelAtCheck)
                problems.Add(where + ": '" + r.Target + "'은 본부 Lv." + def.UnlockHqLevel + "에 열리는데 본부 Lv." + hqLevelAtCheck + "에서 요구한다 — 달성할 수 없다.");
            else if (r.Amount > hqLevelAtCheck)
                problems.Add(where + ": '" + r.Target + "' Lv." + r.Amount + "을 요구하지만 그때 건물 상한은 본부 레벨인 " + hqLevelAtCheck + "이다 — 달성할 수 없다.");
        }

        private void CheckItems(IReadOnlyList<ItemGrant> items, string where, List<string> problems)
        {
            for (int i = 0; i < items.Count; i++)
            {
                // 지금 아이템은 가속뿐이다. 다른 종류가 생기면 아이템 표를 따로 두고 여기서 그 표를 본다.
                if (!_speedUps.ContainsKey(items[i].ItemId))
                    problems.Add(where + ": 모르는 아이템 '" + items[i].ItemId + "'.");
            }
        }

        /// <summary>데이터에 있는 가장 높은 본부 레벨. 본부 표가 비어 있으면 1(복구만 가능).</summary>
        public int MaxHqLevel
        {
            get
            {
                int max = 1;
                foreach (int level in _hqLevels.Keys)
                {
                    if (level > max)
                        max = level;
                }

                return max;
            }
        }
    }
}

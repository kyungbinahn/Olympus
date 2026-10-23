using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.Save;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 시험용 진척 한 벌. 실제 데이터(progression.json)의 축소판 — 본부 3레벨, 건물 3종.
    /// 숫자는 시험이 읽기 쉬운 값으로 골랐고 실제 밸런스와 무관하다.
    /// </summary>
    internal static class ProgressionTestFactory
    {
        public const string Hq = "hq";
        public const string Lumber = "lumber";     // Lv.1에 열리는 목재 생산 건물
        public const string Wall = "wall";         // Lv.2에 열리는 건물
        public const string Wing1m = "wing_1m";
        public const string Wing5m = "wing_5m";

        public const int HqSlot = 1;
        public const int LumberSlot = 2;
        public const int WallSlot = 3;

        public static BuildingCatalog NewCatalog()
        {
            var catalog = new BuildingCatalog();

            catalog.Add(new BuildingDef(Hq, "building.hq", GridFootprint.Square(2), 0L, null, 0));

            catalog.Add(new BuildingDef(
                Lumber, "building.lumber", GridFootprint.Square(2), 10_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 20) },
                1,
                new ProductionSpec(ResourceKind.Wood, 30, 10)));

            catalog.Add(new BuildingDef(
                Wall, "building.wall", GridFootprint.Square(2), 0L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 10) },
                2));

            return catalog;
        }

        public static ProgressionConfig NewConfig()
        {
            var quests = new QuestChain();
            quests.Add(new QuestDef("q1", "quest.q1", Requirement.BuildingLevel(Hq, 1),
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 100) },
                new List<ItemGrant> { new ItemGrant(Wing1m, 2) }, Hq));
            quests.Add(new QuestDef("q2", "quest.q2", Requirement.Counter(ProgressCounters.Collect, 1),
                null, null, Lumber));

            var features = new FeatureUnlockTable();
            features.Add("world_map", 3);
            features.Add("bag", 2);

            var config = new ProgressionConfig(
                Hq,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 500) },
                new List<ItemGrant> { new ItemGrant(Wing5m, 1) },
                quests,
                features);

            // Lv.1 — 대기열 무제한(0). 도달 보상으로 1분 날개 3개.
            config.AddHqLevel(new HqLevelDef(1, null, null, 0L, 0, null,
                new List<ItemGrant> { new ItemGrant(Wing1m, 3) }, null));

            // Lv.2 — 번영도 1 + 목재 100, 3분. 대기열 1칸이 생긴다.
            config.AddHqLevel(new HqLevelDef(2,
                new List<Requirement> { Requirement.Prosperity(1) },
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 100) },
                180_000L, 1,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 50) }, null, "story.hq2"));

            // Lv.3 — 목재소 Lv.2가 선행 조건.
            config.AddHqLevel(new HqLevelDef(3,
                new List<Requirement> { Requirement.BuildingLevel(Lumber, 2) },
                null, 120_000L, 1, null, null, null));

            config.AddUpgradeStep(new UpgradeStep(2, 1.5, 20_000L));
            config.AddUpgradeStep(new UpgradeStep(3, 3.0, 40_000L));

            config.AddSpeedUp(new SpeedUpItemDef(Wing1m, "item.wing_1m", 60_000L));
            config.AddSpeedUp(new SpeedUpItemDef(Wing5m, "item.wing_5m", 300_000L));

            return config;
        }

        public static BaseLayout NewLayout()
        {
            var layout = new BaseLayout(new GridRect(0, 0, 20, 20));
            layout.Add(new BaseSlot(HqSlot, Hq, new GridPos(0, 0)));
            layout.Add(new BaseSlot(LumberSlot, Lumber, new GridPos(5, 5)));
            layout.Add(new BaseSlot(WallSlot, Wall, new GridPos(10, 10)));
            return layout;
        }

        public static TerritoryRuntime NewRuntime(ManualClock clock)
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var runtime = new TerritoryRuntime(state, NewCatalog(), clock, 2f, NewConfig());
            runtime.Initialize(NewLayout());
            return runtime;
        }

        public static BuildingInstance Get(TerritoryRuntime runtime, int slotId)
        {
            BuildingInstance b;
            runtime.State.TryGetBuilding(slotId, out b);
            return b;
        }
    }

    public class HqProgressionTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = ProgressionTestFactory.NewRuntime(_clock);
        }

        [Test]
        public void 처음_시작하면_시작_자원과_아이템을_받는다()
        {
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(500));
            Assert.That(_rt.State.Items.Get(ProgressionTestFactory.Wing5m), Is.EqualTo(1));
        }

        [Test]
        public void 본부가_폐허면_본부_레벨은_0이고_Lv1_건물도_잠겨_있다()
        {
            Assert.That(_rt.HqLevel, Is.EqualTo(0));
            Assert.That(_rt.Construction.CanStartRepair(ProgressionTestFactory.LumberSlot).Rejection,
                Is.EqualTo(BuildRejection.HqLevelTooLow));
        }

        [Test]
        public void 본부를_복구하면_즉시_Lv1이_되고_Lv1_보상을_받는다()
        {
            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot).Started, Is.True);

            Assert.That(_rt.HqLevel, Is.EqualTo(1));
            Assert.That(_rt.State.Items.Get(ProgressionTestFactory.Wing1m), Is.EqualTo(3));
            Assert.That(_rt.Construction.CanStartRepair(ProgressionTestFactory.LumberSlot).Started, Is.True);
        }

        [Test]
        public void 다음_본부_레벨에_열리는_건물은_그때까지_잠겨_있다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);

            Assert.That(_rt.Construction.CanStartRepair(ProgressionTestFactory.WallSlot).Rejection,
                Is.EqualTo(BuildRejection.HqLevelTooLow));
        }

        [Test]
        public void 번영도가_모자라면_본부를_못_올리고_조건별_진행을_보여준다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);

            UpgradePlan plan = _rt.Upgrades.Describe(ProgressionTestFactory.HqSlot);

            Assert.That(plan.CanStart, Is.False);
            Assert.That(plan.Rejection, Is.EqualTo(BuildRejection.RequirementsNotMet));
            Assert.That(plan.Requirements.Count, Is.EqualTo(1));
            Assert.That(plan.Requirements[0].Current, Is.EqualTo(0));
            Assert.That(plan.Requirements[0].Target, Is.EqualTo(1));
        }

        [Test]
        public void 조건이_차면_본부가_공사에_들어가고_끝나면_보상과_함께_레벨이_오른다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _clock.Advance(10_000L);
            _rt.Tick();

            long woodBefore = _rt.State.Resources.Get(ResourceKind.Wood);
            Assert.That(_rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot).Started, Is.True);

            BuildingInstance hq = ProgressionTestFactory.Get(_rt, ProgressionTestFactory.HqSlot);
            Assert.That(hq.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(_rt.HqLevel, Is.EqualTo(1), "공사가 끝나기 전까지 레벨은 그대로다.");
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(woodBefore - 100));

            _clock.Advance(180_000L);
            _rt.Tick();

            Assert.That(_rt.HqLevel, Is.EqualTo(2));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(50));
        }

        [Test]
        public void 선행_건물_레벨이_모자라면_본부를_못_올린다()
        {
            ReachHq2();

            UpgradePlan plan = _rt.Upgrades.Describe(ProgressionTestFactory.HqSlot);
            Assert.That(plan.Rejection, Is.EqualTo(BuildRejection.RequirementsNotMet));
            Assert.That(plan.Requirements[0].Requirement.Target, Is.EqualTo(ProgressionTestFactory.Lumber));
        }

        [Test]
        public void 다음_레벨이_데이터에_없으면_최고_레벨이다()
        {
            ReachHq2();
            _rt.Upgrades.TryStart(ProgressionTestFactory.LumberSlot);
            _rt.DebugCompleteAllConstruction();
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            _rt.DebugCompleteAllConstruction();

            Assert.That(_rt.HqLevel, Is.EqualTo(3));
            Assert.That(_rt.Upgrades.Describe(ProgressionTestFactory.HqSlot).Rejection,
                Is.EqualTo(BuildRejection.MaxLevel));
        }

        private void ReachHq2()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _rt.DebugCompleteAllConstruction();
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            _rt.DebugCompleteAllConstruction();
            Assert.That(_rt.HqLevel, Is.EqualTo(2));
        }
    }

    public class BuildingUpgradeTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = ProgressionTestFactory.NewRuntime(_clock);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _clock.Advance(10_000L);
            _rt.Tick();
        }

        [Test]
        public void 건물_레벨은_본부_레벨을_넘을_수_없다()
        {
            UpgradePlan plan = _rt.Upgrades.Describe(ProgressionTestFactory.LumberSlot);

            Assert.That(plan.CanStart, Is.False);
            Assert.That(plan.Rejection, Is.EqualTo(BuildRejection.HqLevelTooLow));
            Assert.That(plan.RequiredHqLevel, Is.EqualTo(2));
        }

        [Test]
        public void 업그레이드_비용은_복구_비용에_배수를_곱한_것이다()
        {
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            _rt.DebugCompleteAllConstruction();

            UpgradePlan plan = _rt.Upgrades.Describe(ProgressionTestFactory.LumberSlot);

            Assert.That(plan.CanStart, Is.True);
            Assert.That(plan.TargetLevel, Is.EqualTo(2));
            Assert.That(plan.Costs[0].Amount, Is.EqualTo(30), "목재 20 × 1.5");
            Assert.That(plan.DurationMs, Is.EqualTo(20_000L));
        }

        [Test]
        public void 업그레이드를_시작하면_쌓인_생산을_먼저_거둬준다()
        {
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            _rt.DebugCompleteAllConstruction();

            _clock.Advance(120_000L);     // 2분 → Lv1 분당 30 = 60
            long before = _rt.State.Resources.Get(ResourceKind.Wood);

            _rt.Upgrades.TryStart(ProgressionTestFactory.LumberSlot);

            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(before + 60 - 30));
        }

        [Test]
        public void 폐허나_공사_중인_건물은_올릴_수_없다()
        {
            Assert.That(_rt.Upgrades.Describe(ProgressionTestFactory.WallSlot).Rejection,
                Is.EqualTo(BuildRejection.NotComplete));
        }
    }

    public class BuildQueueTests
    {
        [Test]
        public void 대기열이_차면_새_공사를_못_시작하고_즉시_공사는_자리를_안_먹는다()
        {
            var clock = new ManualClock(1_000_000L);
            TerritoryRuntime rt = ProgressionTestFactory.NewRuntime(clock);

            rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            rt.DebugCompleteAllConstruction();
            rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            rt.DebugCompleteAllConstruction();

            // Lv.2 — 대기열 1칸. 목재소 업그레이드(20초)가 그 칸을 쓴다.
            Assert.That(rt.Upgrades.TryStart(ProgressionTestFactory.LumberSlot).Started, Is.True);

            // 성벽(0초)은 즉시 끝나므로 막히지 않는다.
            Assert.That(rt.Construction.CanStartRepair(ProgressionTestFactory.WallSlot).Started, Is.True);

            // 본부 Lv.3은 조건(목재소 Lv.2)이 먼저 막는다 — 여기서는 대기열만 본다.
            Assert.That(ProgressionRules.IsQueueFull(rt.State, rt.Progression, 1L), Is.True);
        }
    }

    public class ProductionTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = ProgressionTestFactory.NewRuntime(_clock);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _clock.Advance(10_000L);
            _rt.Tick();
        }

        private long Stored()
        {
            BuildingDef def;
            _rt.Catalog.TryGet(ProgressionTestFactory.Lumber, out def);
            return ProductionService.StoredAmount(
                ProgressionTestFactory.Get(_rt, ProgressionTestFactory.LumberSlot), def, _clock.NowUnixMs);
        }

        [Test]
        public void 완성_직후에는_비어_있고_시간이_지나면_쌓인다()
        {
            Assert.That(Stored(), Is.EqualTo(0));

            _clock.Advance(60_000L);
            Assert.That(Stored(), Is.EqualTo(30));
        }

        [Test]
        public void 보관_한도를_넘게_쌓이지_않는다()
        {
            _clock.Advance(60L * 60_000L);      // 한 시간 — 한도는 10분어치
            Assert.That(Stored(), Is.EqualTo(300));
        }

        [Test]
        public void 앱을_꺼둔_사이에_끝난_공사는_끝난_시각부터_생산이_쌓인다()
        {
            // 공사가 끝난 지 1분 뒤에야 Tick이 돌았다 — 그 1분 몫이 있어야 한다.
            var clock = new ManualClock(1_000_000L);
            TerritoryRuntime rt = ProgressionTestFactory.NewRuntime(clock);
            rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);

            clock.Advance(10_000L + 60_000L);
            rt.Tick();

            BuildingDef def;
            rt.Catalog.TryGet(ProgressionTestFactory.Lumber, out def);
            Assert.That(ProductionService.StoredAmount(
                ProgressionTestFactory.Get(rt, ProgressionTestFactory.LumberSlot), def, clock.NowUnixMs),
                Is.EqualTo(30));
        }

        [Test]
        public void 거두면_자원이_들어오고_수확_기록이_오른다()
        {
            _clock.Advance(60_000L);
            long before = _rt.State.Resources.Get(ResourceKind.Wood);

            CollectRejection rejection;
            long got = _rt.Production.TryCollect(ProgressionTestFactory.LumberSlot, out rejection);

            Assert.That(rejection, Is.EqualTo(CollectRejection.None));
            Assert.That(got, Is.EqualTo(30));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(before + 30));
            Assert.That(_rt.State.Counters.Get(ProgressCounters.Collect), Is.EqualTo(1));
            Assert.That(Stored(), Is.EqualTo(0));
        }

        [Test]
        public void 자주_거둬도_1개가_되기_직전의_몫을_잃지_않는다()
        {
            // 분당 30 = 2초에 1개. 3초마다 거두면 매번 1개씩 + 1초가 남는다.
            // 남은 1초를 버리면 6초 동안 2개, 버리지 않으면 3개다.
            CollectRejection r;
            long total = 0;
            for (int i = 0; i < 2; i++)
            {
                _clock.Advance(3_000L);
                total += _rt.Production.TryCollect(ProgressionTestFactory.LumberSlot, out r);
            }

            Assert.That(total + Stored(), Is.EqualTo(3));
        }

        [Test]
        public void 안_쌓였으면_못_거두고_이유를_말한다()
        {
            CollectRejection rejection;
            _rt.Production.TryCollect(ProgressionTestFactory.LumberSlot, out rejection);
            Assert.That(rejection, Is.EqualTo(CollectRejection.NothingStored));

            _rt.Production.TryCollect(ProgressionTestFactory.HqSlot, out rejection);
            Assert.That(rejection, Is.EqualTo(CollectRejection.NotProducer));
        }

        [Test]
        public void 레벨이_오르면_생산량도_오른다()
        {
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);
            _rt.DebugCompleteAllConstruction();
            _rt.Upgrades.TryStart(ProgressionTestFactory.LumberSlot);
            _rt.DebugCompleteAllConstruction();

            _clock.Advance(60_000L);
            Assert.That(Stored(), Is.EqualTo(60));
        }

        [Test]
        public void 한꺼번에_거두기는_쌓인_건물만_거둔다()
        {
            _clock.Advance(60_000L);
            Assert.That(_rt.Production.CollectAll(), Is.EqualTo(1));
            Assert.That(_rt.Production.CollectAll(), Is.EqualTo(0));
        }
    }

    public class SpeedUpTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = ProgressionTestFactory.NewRuntime(_clock);
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);   // 1분 날개 3개를 받는다
            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _rt.DebugCompleteAllConstruction();
            _rt.Upgrades.TryStart(ProgressionTestFactory.HqSlot);               // 3분 공사
        }

        [Test]
        public void 날개를_쓰면_끝나는_시각이_당겨지고_하나가_줄어든다()
        {
            long before = ProgressionTestFactory.Get(_rt, ProgressionTestFactory.HqSlot).ConstructionEndsAtUnixMs;

            Assert.That(_rt.SpeedUps.TryUse(ProgressionTestFactory.HqSlot, ProgressionTestFactory.Wing1m),
                Is.EqualTo(SpeedUpRejection.None));

            BuildingInstance hq = ProgressionTestFactory.Get(_rt, ProgressionTestFactory.HqSlot);
            Assert.That(hq.Phase, Is.EqualTo(BuildingPhase.Constructing), "3분 공사에 1분 날개 — 아직 남는다.");
            Assert.That(hq.ConstructionEndsAtUnixMs, Is.EqualTo(before - 60_000L));
            Assert.That(_rt.State.Items.Get(ProgressionTestFactory.Wing1m), Is.EqualTo(2));
            Assert.That(_rt.State.Counters.Get(ProgressCounters.SpeedUp), Is.EqualTo(1));
        }

        [Test]
        public void 당겨서_시각이_지나면_그_자리에서_끝나고_본부_보상도_받는다()
        {
            Assert.That(_rt.SpeedUps.TryUse(ProgressionTestFactory.HqSlot, ProgressionTestFactory.Wing5m),
                Is.EqualTo(SpeedUpRejection.None));

            Assert.That(_rt.HqLevel, Is.EqualTo(2), "Tick을 기다리지 않는다.");
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(50));
        }

        [Test]
        public void 날개가_없거나_공사_중이_아니면_못_쓰고_이유를_말한다()
        {
            Assert.That(_rt.SpeedUps.TryUse(ProgressionTestFactory.LumberSlot, ProgressionTestFactory.Wing1m),
                Is.EqualTo(SpeedUpRejection.NotConstructing));

            Assert.That(_rt.SpeedUps.TryUse(ProgressionTestFactory.HqSlot, "no_such_item"),
                Is.EqualTo(SpeedUpRejection.UnknownItem));

            _rt.Store.Apply(new StateDelta().WithItem(CountChange.Absolute(ProgressionTestFactory.Wing1m, 0)));
            Assert.That(_rt.SpeedUps.TryUse(ProgressionTestFactory.HqSlot, ProgressionTestFactory.Wing1m),
                Is.EqualTo(SpeedUpRejection.NoItem));
        }
    }

    public class QuestTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = ProgressionTestFactory.NewRuntime(_clock);
        }

        [Test]
        public void 목표를_못_채우면_보상을_못_받는다()
        {
            QuestView view = _rt.Quests.Current;

            Assert.That(view.Quest.QuestId, Is.EqualTo("q1"));
            Assert.That(view.CanClaim, Is.False);
            Assert.That(_rt.Quests.TryClaim(), Is.EqualTo(QuestClaimRejection.NotComplete));
        }

        [Test]
        public void 채우고_받으면_보상이_들어오고_다음_임무로_넘어간다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            long wood = _rt.State.Resources.Get(ResourceKind.Wood);

            Assert.That(_rt.Quests.TryClaim(), Is.EqualTo(QuestClaimRejection.None));

            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(wood + 100));
            Assert.That(_rt.State.Items.Get(ProgressionTestFactory.Wing1m), Is.EqualTo(3 + 2));
            Assert.That(_rt.Quests.Current.Quest.QuestId, Is.EqualTo("q2"));
            Assert.That(_rt.Quests.Current.Number, Is.EqualTo(2));
        }

        [Test]
        public void 해_봤는가를_세는_임무는_누적_기록으로_판정한다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Quests.TryClaim();
            Assert.That(_rt.Quests.Current.CanClaim, Is.False);

            _rt.Store.Apply(new StateDelta().WithCounter(CountChange.Delta(ProgressCounters.Collect, 1)));
            Assert.That(_rt.Quests.Current.CanClaim, Is.True);
        }

        [Test]
        public void 체인을_다_끝내면_더_받을_임무가_없다()
        {
            _rt.Store.Apply(new StateDelta { QuestIndex = 2 });

            Assert.That(_rt.Quests.Current.HasQuest, Is.False);
            Assert.That(_rt.Quests.TryClaim(), Is.EqualTo(QuestClaimRejection.NoQuest));
        }
    }

    public class FeatureUnlockTests
    {
        [Test]
        public void 본부_레벨이_닿아야_열리고_표에_없는_기능은_열려_있다()
        {
            var table = new FeatureUnlockTable();
            table.Add("world_map", 5);

            Assert.That(table.IsUnlocked("world_map", 4), Is.False);
            Assert.That(table.IsUnlocked("world_map", 5), Is.True);
            Assert.That(table.IsUnlocked("not_in_table", 0), Is.True);
        }

        [Test]
        public void 그_레벨에서_새로_열리는_것만_골라준다()
        {
            var table = new FeatureUnlockTable();
            table.Add("bag", 4);
            table.Add("world_map", 5);
            table.Add("explore", 5);

            Assert.That(table.UnlockedExactlyAt(5), Is.EquivalentTo(new[] { "world_map", "explore" }));
            Assert.That(FeatureUnlockTable.BuildingsUnlockedAt(ProgressionTestFactory.NewCatalog(), 2).Count,
                Is.EqualTo(1));
        }
    }

    public class ProgressionSaveTests
    {
        [Test]
        public void 가방_기록_임무_생산시각이_저장했다_읽으면_그대로다()
        {
            var clock = new ManualClock(1_000_000L);
            TerritoryRuntime rt = ProgressionTestFactory.NewRuntime(clock);
            rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            clock.Advance(10_000L);
            rt.Tick();
            rt.Quests.TryClaim();
            clock.Advance(60_000L);
            CollectRejection r;
            rt.Production.TryCollect(ProgressionTestFactory.LumberSlot, out r);
            clock.Advance(30_000L);

            SaveFile save = rt.CaptureSave();

            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var loaded = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), clock, 2f,
                ProgressionTestFactory.NewConfig());
            loaded.InitializeFromSave(ProgressionTestFactory.NewLayout(), save);

            Assert.That(loaded.State.Items.Get(ProgressionTestFactory.Wing1m),
                Is.EqualTo(rt.State.Items.Get(ProgressionTestFactory.Wing1m)));
            Assert.That(loaded.State.Counters.Get(ProgressCounters.Collect), Is.EqualTo(1));
            Assert.That(loaded.State.QuestIndex, Is.EqualTo(1));
            Assert.That(ProgressionTestFactory.Get(loaded, ProgressionTestFactory.LumberSlot).ProductionAnchorUnixMs,
                Is.EqualTo(ProgressionTestFactory.Get(rt, ProgressionTestFactory.LumberSlot).ProductionAnchorUnixMs));
        }

        [Test]
        public void 판1_세이브는_빈_가방과_첫_임무로_읽힌다()
        {
            var save = new SaveFile { version = 1, items = null, counters = null };
            var clock = new ManualClock(1_000_000L);
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), clock, 2f,
                ProgressionTestFactory.NewConfig());

            rt.InitializeFromSave(ProgressionTestFactory.NewLayout(), save);

            Assert.That(rt.State.QuestIndex, Is.EqualTo(0));
            Assert.That(rt.State.Items.Get(ProgressionTestFactory.Wing1m), Is.EqualTo(0));
        }
    }
    public class ProgressionValidationTests
    {
        [Test]
        public void 맞는_데이터는_문제가_없다()
        {
            Assert.That(ProgressionTestFactory.NewConfig().Validate(ProgressionTestFactory.NewCatalog()), Is.Empty);
        }

        [Test]
        public void 그때_아직_안_열린_건물을_본부_조건에_걸면_잡는다()
        {
            ProgressionConfig config = ProgressionTestFactory.NewConfig();

            // 본부 Lv.4로 가는 조건에 성벽 Lv.3 — 성벽은 Lv.2에 열리지만, 본부 Lv.3에서 건물 상한은 3이라 가능.
            // 그런데 본부 표는 3까지라 Lv.4가 생기면 "건물 레벨 단계 4가 없다"도 같이 잡혀야 한다.
            config.AddHqLevel(new HqLevelDef(4,
                new List<Requirement> { Requirement.BuildingLevel(ProgressionTestFactory.Wall, 4) },
                null, 0L, 1, null, null, null));

            List<string> problems = config.Validate(ProgressionTestFactory.NewCatalog());

            Assert.That(problems.Exists(p => p.Contains("달성할 수 없다")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("건물 레벨 단계 4")), Is.True);
        }

        [Test]
        public void 모르는_아이템과_없는_건물을_잡는다()
        {
            var quests = new QuestChain();
            quests.Add(new QuestDef("bad", "quest.bad", Requirement.BuildingLevel("nope", 1),
                null, new List<ItemGrant> { new ItemGrant("typo_wing", 1) }, "nope"));

            var config = new ProgressionConfig(ProgressionTestFactory.Hq, null, null, quests, null);
            List<string> problems = config.Validate(ProgressionTestFactory.NewCatalog());

            Assert.That(problems.Exists(p => p.Contains("건물 'nope'")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("typo_wing")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("focusDefId 'nope'")), Is.True);
        }
    }
    public class SlotPanelTests
    {
        [Test]
        public void 잠긴_폐허는_잠김으로_보이고_열리는_레벨을_알려준다()
        {
            var clock = new ManualClock(1_000_000L);
            TerritoryRuntime rt = ProgressionTestFactory.NewRuntime(clock);
            rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);

            SlotPanel panel;
            Assert.That(SlotPanel.TryDescribe(rt, ProgressionTestFactory.WallSlot, out panel), Is.True);
            Assert.That(panel.IsLocked, Is.True);
            Assert.That(panel.UnlockHqLevel, Is.EqualTo(2));
        }

        [Test]
        public void 생산_건물은_쌓인_양과_한도를_보여준다()
        {
            var clock = new ManualClock(1_000_000L);
            TerritoryRuntime rt = ProgressionTestFactory.NewRuntime(clock);
            rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);

            SlotPanel ruined;
            SlotPanel.TryDescribe(rt, ProgressionTestFactory.LumberSlot, out ruined);
            Assert.That(ruined.IsProducer, Is.True);
            Assert.That(ruined.BaseRatePerMinute, Is.EqualTo(30));
            Assert.That(ruined.CanCollect, Is.False);

            rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            clock.Advance(10_000L + 120_000L);
            rt.Tick();

            SlotPanel built;
            SlotPanel.TryDescribe(rt, ProgressionTestFactory.LumberSlot, out built);
            Assert.That(built.Stored, Is.EqualTo(60));
            Assert.That(built.Capacity, Is.EqualTo(300));
            Assert.That(built.CanCollect, Is.True);
            Assert.That(built.IsHq, Is.False);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.Save;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 세이브 시험.
    ///
    /// 제일 중요한 시험은 <see cref="기획이_비용을_바꿔도_저장은_그대로_읽힌다"/>다.
    /// 지금 건물 데이터는 전부 임시값이고 기획이 계속 고칠 예정이라, 세이브가 그 변화를
    /// 견디지 못하면 데이터를 만질 때마다 진행이 날아간다.
    /// </summary>
    public class SaveTests
    {
        private const string Hall = "hall";
        private const string Mine = "mine";

        private static BuildingCatalog Catalog(long hallCost, long hallDurationMs)
        {
            var catalog = new BuildingCatalog();

            catalog.Add(new BuildingDef(
                Hall, "building.hall", GridFootprint.Square(2), hallDurationMs,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, hallCost) }));

            catalog.Add(new BuildingDef(
                Mine, "building.mine", GridFootprint.Single, 30_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 40) }));

            return catalog;
        }

        private static BaseLayout Layout(params BaseSlot[] slots)
        {
            var layout = new BaseLayout(new GridRect(0, 0, 20, 20));
            foreach (BaseSlot s in slots)
            {
                layout.Add(s);
            }
            return layout;
        }

        private static BaseSlot HallSlot(int id = 1) => new BaseSlot(id, Hall, new GridPos(0, 0));
        private static BaseSlot MineSlot(int id = 2) => new BaseSlot(id, Mine, new GridPos(10, 10));

        private static TerritoryRuntime NewRuntime(BuildingCatalog catalog, IClock clock)
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            return new TerritoryRuntime(state, catalog, clock);
        }

        [Test]
        public void 저장하고_다시_불러오면_진행이_같다()
        {
            var clock = new ManualClock(1_000_000L);
            BuildingCatalog catalog = Catalog(100, 60_000L);
            BaseLayout layout = Layout(HallSlot(), MineSlot());

            TerritoryRuntime first = NewRuntime(catalog, clock);
            first.Initialize(layout);
            first.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 500))
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, 80)));

            first.Construction.TryStartRepair(1);   // 공사 시작 (60초)
            SaveFile save = first.CaptureSave();

            // 앱을 껐다 켠 셈 — 완전히 새 런타임에 저장만 물린다.
            TerritoryRuntime second = NewRuntime(catalog, clock);
            SaveLoadReport report = second.InitializeFromSave(layout, save);

            Assert.That(report.Restored, Is.EqualTo(2));
            Assert.That(report.HasMismatch, Is.False);

            BuildingInstance hall;
            second.State.TryGetBuilding(1, out hall);

            Assert.That(hall.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(hall.ConstructionEndsAtUnixMs, Is.EqualTo(1_060_000L), "끝나는 시각이 그대로여야 한다");
            Assert.That(second.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(400), "비용이 빠진 뒤 값");
            Assert.That(second.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(80));
        }

        [Test]
        public void 기획이_비용을_바꿔도_저장은_그대로_읽힌다()
        {
            // ★핵심 시험. 세이브에 비용·건설시간을 적지 않기 때문에 성립한다.
            var clock = new ManualClock(0L);
            BaseLayout layout = Layout(HallSlot(), MineSlot());

            TerritoryRuntime before = NewRuntime(Catalog(100, 60_000L), clock);
            before.Initialize(layout);
            before.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 1000)));
            before.Construction.TryStartRepair(1);

            SaveFile save = before.CaptureSave();

            // 기획이 비용을 100 → 777, 건설시간을 60초 → 5분으로 바꿨다.
            TerritoryRuntime after = NewRuntime(Catalog(777, 300_000L), clock);
            SaveLoadReport report = after.InitializeFromSave(layout, save);

            Assert.That(report.HasMismatch, Is.False, "숫자만 바뀐 것은 어긋남이 아니다");

            BuildingInstance hall;
            after.State.TryGetBuilding(1, out hall);

            Assert.That(hall.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(hall.ConstructionEndsAtUnixMs, Is.EqualTo(60_000L),
                "이미 시작한 공사는 저장된 완료 시각을 지킨다 — 새 건설시간이 소급되지 않는다");
            Assert.That(after.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(900));
        }

        [Test]
        public void 자리가_새로_늘면_그_자리만_폐허로_시작한다()
        {
            // 컨텐츠 추가 — 기존 진행은 지키고 새 자리만 붙는다.
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 0L);

            TerritoryRuntime before = NewRuntime(catalog, clock);
            before.Initialize(Layout(HallSlot()));
            before.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 1000)));
            before.Construction.TryStartRepair(1);   // 건설시간 0 → 바로 완성

            SaveFile save = before.CaptureSave();

            TerritoryRuntime after = NewRuntime(catalog, clock);
            SaveLoadReport report = after.InitializeFromSave(Layout(HallSlot(), MineSlot()), save);

            Assert.That(report.Restored, Is.EqualTo(1));
            Assert.That(report.Fresh, Is.EqualTo(1));

            BuildingInstance hall, mine;
            after.State.TryGetBuilding(1, out hall);
            after.State.TryGetBuilding(2, out mine);

            Assert.That(hall.Phase, Is.EqualTo(BuildingPhase.Complete), "기존 진행 유지");
            Assert.That(mine.Phase, Is.EqualTo(BuildingPhase.Ruined), "새 자리는 폐허로");
        }

        [Test]
        public void 자리가_사라지면_그_저장은_버린다()
        {
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 0L);

            TerritoryRuntime before = NewRuntime(catalog, clock);
            before.Initialize(Layout(HallSlot(), MineSlot()));
            SaveFile save = before.CaptureSave();

            // 기획이 광산 자리를 없앴다.
            TerritoryRuntime after = NewRuntime(catalog, clock);
            SaveLoadReport report = after.InitializeFromSave(Layout(HallSlot()), save);

            Assert.That(report.Restored, Is.EqualTo(1));
            Assert.That(report.Dropped, Is.EqualTo(1));
            Assert.That(after.State.BuildingCount, Is.EqualTo(1));
        }

        [Test]
        public void 같은_자리에_다른_건물이_서면_진행을_물려받지_않는다()
        {
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 0L);

            TerritoryRuntime before = NewRuntime(catalog, clock);
            before.Initialize(Layout(HallSlot(1)));
            before.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 1000)));
            before.Construction.TryStartRepair(1);   // 완성시킴

            SaveFile save = before.CaptureSave();

            // 1번 자리가 본부에서 광산으로 바뀌었다.
            TerritoryRuntime after = NewRuntime(catalog, clock);
            SaveLoadReport report = after.InitializeFromSave(
                Layout(new BaseSlot(1, Mine, new GridPos(0, 0))), save);

            Assert.That(report.Replaced, Is.EqualTo(1));

            BuildingInstance slot;
            after.State.TryGetBuilding(1, out slot);

            Assert.That(slot.DefId, Is.EqualTo(Mine));
            Assert.That(slot.Phase, Is.EqualTo(BuildingPhase.Ruined),
                "본부의 완성 상태를 광산이 물려받으면 안 된다");
        }

        [Test]
        public void 모르는_자원_이름은_건너뛰고_나머지는_읽는다()
        {
            // 나중에 자원 종류가 빠지거나 이름이 바뀌어도 세이브 전체가 죽으면 안 된다.
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 0L);

            var save = new SaveFile();
            save.resources.Add(new SavedResource { kind = "Wood", amount = 123 });
            save.resources.Add(new SavedResource { kind = "Ambrosia", amount = 999 });

            TerritoryRuntime rt = NewRuntime(catalog, clock);
            SaveLoadReport report = rt.InitializeFromSave(Layout(HallSlot()), save);

            Assert.That(report.SkippedResources, Is.EqualTo(1));
            Assert.That(rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(123));
        }

        [Test]
        public void 세이브가_없으면_전부_폐허로_시작한다()
        {
            var clock = new ManualClock(0L);
            TerritoryRuntime rt = NewRuntime(Catalog(100, 0L), clock);

            SaveLoadReport report = rt.InitializeFromSave(Layout(HallSlot(), MineSlot()), null);

            Assert.That(report.Fresh, Is.EqualTo(2));
            Assert.That(rt.State.BuildingCount, Is.EqualTo(2));

            foreach (BuildingInstance b in rt.State.Buildings)
            {
                Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Ruined));
            }
        }

        [Test]
        public void 꺼_둔_동안_지난_공사는_돌아오면_완료된다()
        {
            // 완료 시각을 저장하므로 앱이 꺼져 있어도 시간이 흐른다.
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 60_000L);
            BaseLayout layout = Layout(HallSlot());

            TerritoryRuntime before = NewRuntime(catalog, clock);
            before.Initialize(layout);
            before.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 1000)));
            before.Construction.TryStartRepair(1);

            SaveFile save = before.CaptureSave();

            // 앱을 끄고 10분 뒤에 켰다.
            var laterClock = new ManualClock(600_000L);
            TerritoryRuntime after = NewRuntime(catalog, laterClock);
            after.InitializeFromSave(layout, save);

            Assert.That(after.Tick(), Is.EqualTo(1), "돌아온 순간 공사가 완료돼야 한다");

            BuildingInstance hall;
            after.State.TryGetBuilding(1, out hall);
            Assert.That(hall.Phase, Is.EqualTo(BuildingPhase.Complete));
        }

        [Test]
        public void 레이아웃이_잘못되면_세이브가_있어도_터진다()
        {
            // 세이브가 있다고 데이터 실수를 건너뛰면 안 된다.
            var clock = new ManualClock(0L);
            BuildingCatalog catalog = Catalog(100, 0L);

            // 2x2 본부 두 채를 겹쳐 놓는다.
            var broken = new BaseLayout(new GridRect(0, 0, 20, 20));
            broken.Add(new BaseSlot(1, Hall, new GridPos(0, 0)));
            broken.Add(new BaseSlot(2, Hall, new GridPos(1, 1)));

            TerritoryRuntime rt = NewRuntime(catalog, clock);

            Assert.That(
                () => rt.InitializeFromSave(broken, new SaveFile()),
                Throws.InvalidOperationException);
        }
    }
}

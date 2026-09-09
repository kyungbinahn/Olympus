using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    public class ConstructionServiceTests
    {
        private StateStore _store;
        private BuildingCatalog _catalog;
        private ManualClock _clock;
        private ConstructionService _svc;

        private const string Barracks = "barracks";
        private const string Wall = "wall";
        private const string Instant = "instant";

        [SetUp]
        public void SetUp()
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 10, 10));
            _store = new StateStore(state);

            _catalog = new BuildingCatalog();
            _catalog.Add(new BuildingDef(
                Barracks, "building.barracks", GridFootprint.Square(2), 60_000L,
                new List<ResourceCost>
                {
                    new ResourceCost(ResourceKind.Wood, 100),
                    new ResourceCost(ResourceKind.Stone, 50),
                }));
            _catalog.Add(new BuildingDef(
                Wall, "building.wall", GridFootprint.Single, 10_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 10) }));
            _catalog.Add(new BuildingDef(
                Instant, "building.instant", GridFootprint.Single, 0L, null));

            _clock = new ManualClock(1_000_000L);
            _svc = new ConstructionService(_store, _catalog, _clock);

            GiveResources(1000, 1000);
        }

        private void GiveResources(long wood, long stone)
        {
            _store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, wood))
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, stone)));
        }

        [Test]
        public void 건설을_시작하면_비용이_빠지고_공사중_건물이_생긴다()
        {
            BuildResult r = _svc.TryStart(Barracks, new GridPos(0, 0));

            Assert.That(r.Started, Is.True);
            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(900));
            Assert.That(_store.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(950));

            BuildingInstance b;
            Assert.That(_store.State.TryGetBuilding(r.BuildingId, out b), Is.True);
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(b.Level, Is.EqualTo(0));
            Assert.That(b.ConstructionEndsAtUnixMs, Is.EqualTo(1_060_000L));
        }

        [Test]
        public void 비용과_건물이_같은_델타로_들어가_통지가_한_번만_온다()
        {
            // 자원 차감과 건물 추가가 따로 적용되면 그 사이에 HUD가 한 번 그려져서
            // "자원은 빠졌는데 건물이 없는" 프레임이 보인다.
            int notifications = 0;
            _store.Changed += _ => notifications++;

            _svc.TryStart(Barracks, new GridPos(0, 0));

            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void 자원이_모자라면_시작되지_않고_아무것도_바뀌지_않는다()
        {
            GiveResources(10, 10);

            BuildResult r = _svc.TryStart(Barracks, new GridPos(0, 0));

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.InsufficientResources));
            Assert.That(_store.State.BuildingCount, Is.EqualTo(0));
            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(10));
        }

        [Test]
        public void 겹치는_자리는_이유까지_알려준다()
        {
            _svc.TryStart(Barracks, new GridPos(0, 0));

            BuildResult r = _svc.TryStart(Wall, new GridPos(1, 1));

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.Overlapping));
        }

        [Test]
        public void 범위_밖은_이유까지_알려준다()
        {
            BuildResult r = _svc.TryStart(Barracks, new GridPos(9, 9));

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.OutOfBounds));
        }

        [Test]
        public void 모르는_정의는_거부한다()
        {
            BuildResult r = _svc.TryStart("없는건물", new GridPos(0, 0));

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.UnknownDef));
        }

        [Test]
        public void CanStart는_상태를_바꾸지_않는다()
        {
            // 배치 미리보기가 매 프레임 부르는 함수라 부수효과가 있으면 안 된다.
            long woodBefore = _store.State.Resources.Get(ResourceKind.Wood);

            for (int i = 0; i < 5; i++)
            {
                _svc.CanStart(Barracks, new GridPos(0, 0));
            }

            Assert.That(_store.State.BuildingCount, Is.EqualTo(0));
            Assert.That(_store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(woodBefore));
        }

        [Test]
        public void 시간이_지나야_완성되고_그_전에는_안_된다()
        {
            _svc.TryStart(Wall, new GridPos(3, 3));

            _clock.Advance(9_999L);
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0));

            _clock.Advance(1L);
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(1));

            BuildingInstance b;
            _store.State.TryGetBuilding(1, out b);
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Complete));
            Assert.That(b.Level, Is.EqualTo(1));
            Assert.That(b.ConstructionEndsAtUnixMs, Is.EqualTo(0L));
        }

        [Test]
        public void 한꺼번에_지난_여러_건물이_한_번에_완성된다()
        {
            // 앱이 백그라운드에 오래 있었던 경우다. 로컬 타이머 만료로 세지 않고
            // "끝나는 시각 <= 지금"으로 판정하는 덕에 프레임을 몇 개 놓쳐도 결과가 같다.
            _svc.TryStart(Wall, new GridPos(0, 0));
            _svc.TryStart(Wall, new GridPos(1, 0));
            _svc.TryStart(Barracks, new GridPos(4, 4));

            _clock.Advance(600_000L);

            Assert.That(_svc.CompleteFinished(), Is.EqualTo(3));
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0), "두 번째 호출은 할 일이 없어야 한다");
        }

        [Test]
        public void 건설시간이_0이면_바로_완성으로_들어간다()
        {
            BuildResult r = _svc.TryStart(Instant, new GridPos(7, 7));

            BuildingInstance b;
            _store.State.TryGetBuilding(r.BuildingId, out b);

            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Complete));
            Assert.That(b.Level, Is.EqualTo(1));
            Assert.That(_svc.CompleteFinished(), Is.EqualTo(0));
        }

        [Test]
        public void 옮기면_새_자리로_가고_공사_진행은_유지된다()
        {
            _svc.TryStart(Barracks, new GridPos(0, 0));

            BuildResult r = _svc.TryRelocate(1, new GridPos(5, 5));

            Assert.That(r.Started, Is.True);

            BuildingInstance b;
            _store.State.TryGetBuilding(1, out b);
            Assert.That(b.Anchor, Is.EqualTo(new GridPos(5, 5)));
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Constructing));
            Assert.That(b.ConstructionEndsAtUnixMs, Is.EqualTo(1_060_000L), "옮겨도 완료 시각은 그대로");
        }

        [Test]
        public void 한_칸_옆으로_옮기는_것이_자기_자신_때문에_막히지_않는다()
        {
            _svc.TryStart(Barracks, new GridPos(0, 0));

            BuildResult r = _svc.TryRelocate(1, new GridPos(1, 0));

            Assert.That(r.Started, Is.True);
            Assert.That(_store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(4));
        }

        [Test]
        public void 남의_자리로는_못_옮긴다()
        {
            _svc.TryStart(Barracks, new GridPos(0, 0));
            _svc.TryStart(Wall, new GridPos(5, 5));

            BuildResult r = _svc.TryRelocate(2, new GridPos(1, 1));

            Assert.That(r.Started, Is.False);
            Assert.That(r.Rejection, Is.EqualTo(BuildRejection.Overlapping));

            BuildingInstance wall;
            _store.State.TryGetBuilding(2, out wall);
            Assert.That(wall.Anchor, Is.EqualTo(new GridPos(5, 5)), "실패했으면 제자리");
        }
    }

    public class BuildingCatalogTests
    {
        [Test]
        public void DefId가_중복이면_등록에서_터진다()
        {
            var catalog = new BuildingCatalog();
            catalog.Add(new BuildingDef("a", "k", GridFootprint.Single, 0L, null));

            Assert.Throws<System.InvalidOperationException>(() =>
                catalog.Add(new BuildingDef("a", "k2", GridFootprint.Single, 0L, null)));
        }

        [Test]
        public void 없는_DefId는_false를_준다()
        {
            var catalog = new BuildingCatalog();

            BuildingDef def;
            Assert.That(catalog.TryGet("nope", out def), Is.False);
            Assert.That(catalog.TryGet(null, out def), Is.False);
        }
    }
}

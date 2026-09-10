using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 배선 시험 — 아무도 Recompute를 부르지 않아도 녹지가 따라오는지 본다.
    ///
    /// 이 시험들이 있는 이유는 실제 사고다(2026-09-10). 재계산을 타이머 만료 경로에만
    /// 붙여 놨더니 건설시간 0인 건물을 복구했을 때 녹지가 안 떴다. 화면으로만 확인하고
    /// 있었으므로 "땅 머티리얼이 안 붙었나?"로 보였다.
    /// </summary>
    public class TerritoryRuntimeTests
    {
        private const string Instant = "instant";   // 건설시간 0 — 클릭하면 즉시 완성
        private const string Timed = "timed";       // 10초

        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            var catalog = new BuildingCatalog();
            catalog.Add(new BuildingDef(Instant, "k", GridFootprint.Single, 0L, null));
            catalog.Add(new BuildingDef(Timed, "k", GridFootprint.Single, 10_000L, null));

            var state = new GameState(new SquareGrid(1f), GridRect.Centered(20));
            _clock = new ManualClock(0L);
            _rt = new TerritoryRuntime(state, catalog, _clock);

            var layout = new BaseLayout(GridRect.Centered(20));
            layout.Add(new BaseSlot(1, Instant, new GridPos(-6, 0)));
            layout.Add(new BaseSlot(2, Timed, new GridPos(6, 0)));
            _rt.Initialize(layout);
        }

        [Test]
        public void 시작에는_전부_폐허고_녹지가_없다()
        {
            Assert.That(_rt.State.BuildingCount, Is.EqualTo(2));
            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(0));
        }

        [Test]
        public void 즉시_완성_건물을_복구하면_녹지가_바로_따라온다()
        {
            // ★회귀 시험. 재계산을 타이머 경로에만 붙였을 때 이 시험이 0을 받는다 —
            // 즉시 완성은 CompleteFinished를 지나지 않기 때문이다.
            BuildResult r = _rt.Construction.TryStartRepair(1);

            Assert.That(r.Started, Is.True);

            BuildingInstance b;
            _rt.State.TryGetBuilding(1, out b);
            Assert.That(b.Phase, Is.EqualTo(BuildingPhase.Complete), "건설시간 0이면 바로 완성");

            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(13),
                "Tick도 Recompute도 부르지 않았는데 녹지가 반영돼야 한다");
        }

        [Test]
        public void 타이머_완성도_Tick만으로_녹지가_따라온다()
        {
            _rt.Construction.TryStartRepair(2);
            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(0), "공사 중에는 아직");

            _clock.Advance(10_000L);
            Assert.That(_rt.Tick(), Is.EqualTo(1));

            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(13));
        }

        [Test]
        public void 복구가_늘면_녹지도_는다()
        {
            _rt.Construction.TryStartRepair(1);
            int afterFirst = _rt.Restoration.RestoredCellCount;

            _rt.Construction.TryStartRepair(2);
            _clock.Advance(10_000L);
            _rt.Tick();

            Assert.That(_rt.Restoration.RestoredCellCount, Is.GreaterThan(afterFirst));
            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(26), "둘이 멀어 겹치지 않는다");
        }

        [Test]
        public void 자원_변경만으로는_녹지_버전이_오르지_않는다()
        {
            // 통지마다 재계산하지만, 건물이 안 바뀌었으면 결과가 같으므로
            // Version이 올라가지 않아야 한다 — 오르면 뷰가 메쉬를 헛되게 다시 만든다.
            _rt.Construction.TryStartRepair(1);
            int version = _rt.Restoration.Version;

            _rt.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 999)));

            Assert.That(_rt.Restoration.Version, Is.EqualTo(version));
        }

        [Test]
        public void 복구_범위는_기지_경계와_같다()
        {
            // 런타임이 만드는 RestorationMap의 범위가 GameState의 격자 범위와 어긋나면
            // 지면 메쉬가 없는 칸을 녹지로 칠하려 든다.
            GridRect bounds = GridRect.Centered(20);
            var catalog = new BuildingCatalog();
            catalog.Add(new BuildingDef(Instant, "k", GridFootprint.Single, 0L, null));

            var state = new GameState(new SquareGrid(1f), bounds);
            var rt = new TerritoryRuntime(state, catalog, new ManualClock(0L));

            var layout = new BaseLayout(bounds);
            layout.Add(new BaseSlot(1, Instant, new GridPos(9, 9)));   // 모서리
            rt.Initialize(layout);
            rt.Construction.TryStartRepair(1);

            foreach (GridPos c in bounds.AllCells())
            {
                if (rt.Restoration.IsRestored(c))
                    Assert.That(bounds.Contains(c), Is.True, "범위 밖 칸: " + c);
            }

            Assert.That(rt.Restoration.IsRestored(new GridPos(10, 9)), Is.False);
        }

        [Test]
        public void 반경을_바꾸면_즉시_다시_센다()
        {
            // 플레이 중에 인스펙터로 값을 돌려 눈으로 정하는 경로다.
            _rt.Construction.TryStartRepair(1);
            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(13), "반경 2");

            int version = _rt.Restoration.Version;
            _rt.SetRestorationRadius(4f);

            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(49), "반경 4");
            Assert.That(_rt.Restoration.Version, Is.GreaterThan(version), "뷰가 다시 그려야 한다");
        }

        [Test]
        public void 같은_반경으로_다시_세팅하면_아무_일도_없다()
        {
            // 매 프레임 부르는 경로다 — 여기서 버전이 오르면 메쉬를 매 프레임 재생성한다.
            _rt.Construction.TryStartRepair(1);
            int version = _rt.Restoration.Version;

            for (int i = 0; i < 10; i++)
            {
                _rt.SetRestorationRadius(_rt.Restoration.Radius);
            }

            Assert.That(_rt.Restoration.Version, Is.EqualTo(version));
        }

        [Test]
        public void 반경을_0으로_줄이면_풋프린트만_남는다()
        {
            _rt.Construction.TryStartRepair(1);
            _rt.SetRestorationRadius(0f);

            Assert.That(_rt.Restoration.RestoredCellCount, Is.EqualTo(1), "1x1 건물의 풋프린트");
        }

        [Test]
        public void Dispose_후에는_통지를_받지_않는다()
        {
            _rt.Dispose();
            int version = _rt.Restoration.Version;

            _rt.Construction.TryStartRepair(1);

            Assert.That(_rt.Restoration.Version, Is.EqualTo(version),
                "구독을 끊었으면 재계산되지 않는다");
        }
    }
}

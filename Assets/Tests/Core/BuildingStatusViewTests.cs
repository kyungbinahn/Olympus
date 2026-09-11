using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// HUD가 선택한 자리를 어떻게 요약해서 받는지 본다. BuildingInstance 생성자가
    /// internal이라 여기서도 직접 만들 수 없다 — 다른 시험들처럼 StateStore.Apply로
    /// 세운다(<see cref="TerritoryTestFactory"/> 참고).
    /// </summary>
    public class BuildingStatusViewTests
    {
        private BuildingCatalog _catalog;
        private GameState _state;
        private StateStore _store;

        [SetUp]
        public void SetUp()
        {
            _catalog = TerritoryTestFactory.NewCatalog();
            _state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            _store = new StateStore(_state);
        }

        private BuildingDef Def(string defId)
        {
            BuildingDef def;
            _catalog.TryGet(defId, out def);
            return def;
        }

        private BuildingInstance Add(string defId, BuildingPhase phase, int level, long endsAtUnixMs)
        {
            BuildingDef def = Def(defId);

            _store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, defId, new GridPos(0, 0), def.Footprint, phase, level, endsAtUnixMs)));

            BuildingInstance b;
            _state.TryGetBuilding(1, out b);
            return b;
        }

        [Test]
        public void 비용을_전부_감당하면_복구_가능이다()
        {
            // Temple: Stone 200, Wood 50
            _store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, 200))
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 50)));

            BuildingInstance b = Add(TerritoryTestFactory.Temple, BuildingPhase.Ruined, 0, 0L);
            BuildingStatusView view = BuildingStatusView.Describe(b, Def(TerritoryTestFactory.Temple), _state.Resources, 0L);

            Assert.That(view.CanRepair, Is.True);
            Assert.That(view.RepairCosts.Count, Is.EqualTo(2));

            foreach (CostLine c in view.RepairCosts)
            {
                Assert.That(c.Affordable, Is.True);
            }
        }

        [Test]
        public void 한_자원만_부족해도_복구_불가이고_그_줄만_표시된다()
        {
            // Stone은 충분, Wood는 부족.
            _store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, 200))
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 10)));

            BuildingInstance b = Add(TerritoryTestFactory.Temple, BuildingPhase.Ruined, 0, 0L);
            BuildingStatusView view = BuildingStatusView.Describe(b, Def(TerritoryTestFactory.Temple), _state.Resources, 0L);

            Assert.That(view.CanRepair, Is.False);

            var byKind = new Dictionary<ResourceKind, bool>();
            foreach (CostLine c in view.RepairCosts)
            {
                byKind[c.Kind] = c.Affordable;
            }

            Assert.That(byKind[ResourceKind.Stone], Is.True);
            Assert.That(byKind[ResourceKind.Wood], Is.False);
        }

        [Test]
        public void 공사_중이면_남은_시간이_있고_비용_줄은_없다()
        {
            BuildingInstance b = Add(TerritoryTestFactory.Barracks, BuildingPhase.Constructing, 0, 5_000L);
            BuildingStatusView view = BuildingStatusView.Describe(b, Def(TerritoryTestFactory.Barracks), _state.Resources, 2_000L);

            Assert.That(view.RemainingConstructionMs, Is.EqualTo(3_000L));
            Assert.That(view.RepairCosts, Is.Empty);
            Assert.That(view.CanRepair, Is.False);
        }

        [Test]
        public void 완성이면_레벨이_보이고_비용_줄은_없다()
        {
            BuildingInstance b = Add(TerritoryTestFactory.Barracks, BuildingPhase.Complete, 3, 0L);
            BuildingStatusView view = BuildingStatusView.Describe(b, Def(TerritoryTestFactory.Barracks), _state.Resources, 999_999L);

            Assert.That(view.Level, Is.EqualTo(3));
            Assert.That(view.RemainingConstructionMs, Is.EqualTo(0L));
            Assert.That(view.RepairCosts, Is.Empty);
            Assert.That(view.CanRepair, Is.False);
        }
    }
}

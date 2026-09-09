using System;
using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    public class ManualClockTests
    {
        [Test]
        public void 앞으로만_감고_뒤로는_못_감는다()
        {
            var clock = new ManualClock(1000);
            clock.Advance(500);

            Assert.That(clock.NowUnixMs, Is.EqualTo(1500));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        }
    }

    public class ResourcePoolTests
    {
        [Test]
        public void 없는_자원은_0이다()
        {
            var store = NewStore();

            Assert.That(store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(0));
        }

        [Test]
        public void 증감과_절대값_둘_다_적용된다()
        {
            StateStore store = NewStore();

            store.Apply(new StateDelta().WithResource(ResourceChange.Delta(ResourceKind.Wood, 120)));
            Assert.That(store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(120));

            store.Apply(new StateDelta().WithResource(ResourceChange.Delta(ResourceKind.Wood, -20)));
            Assert.That(store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(100));

            // 서버가 쓰는 형태 — 어긋남이 누적되지 않게 최종값을 박는다.
            store.Apply(new StateDelta().WithResource(ResourceChange.Absolute(ResourceKind.Wood, 7)));
            Assert.That(store.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(7));
        }

        [Test]
        public void 음수로_내려가면_조용히_넘기지_않고_터진다()
        {
            StateStore store = NewStore();
            store.Apply(new StateDelta().WithResource(ResourceChange.Delta(ResourceKind.Stone, 10)));

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithResource(ResourceChange.Delta(ResourceKind.Stone, -11))));
        }

        [Test]
        public void 비용_전체를_감당할_수_있는지_한_번에_본다()
        {
            StateStore store = NewStore();
            store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, 100))
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, 5)));

            var affordable = new List<ResourceCost>
            {
                new ResourceCost(ResourceKind.Wood, 50),
                new ResourceCost(ResourceKind.Stone, 5),
            };
            var tooMuch = new List<ResourceCost>
            {
                new ResourceCost(ResourceKind.Wood, 50),
                new ResourceCost(ResourceKind.Stone, 6),
            };

            Assert.That(store.State.Resources.CanAfford(affordable), Is.True);
            Assert.That(store.State.Resources.CanAfford(tooMuch), Is.False);
        }

        private static StateStore NewStore() => StateTestFactory.NewStore();
    }

    public class StateStoreTests
    {
        [Test]
        public void 빈_델타는_통지도_하지_않는다()
        {
            StateStore store = StateTestFactory.NewStore();
            int fired = 0;
            store.Changed += _ => fired++;

            store.Apply(new StateDelta());

            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public void 적용이_끝난_뒤에_통지가_온다()
        {
            StateStore store = StateTestFactory.NewStore();
            long seenAtNotify = -1;
            store.Changed += _ => seenAtNotify = store.State.Resources.Get(ResourceKind.Gold);

            store.Apply(new StateDelta().WithResource(ResourceChange.Delta(ResourceKind.Gold, 42)));

            // 통지 시점에 이미 상태가 갱신돼 있어야 한다 — 안 그러면 구독자가
            // 낡은 값을 그리고, 그걸 맞추려고 폴링이 끼어들기 시작한다.
            Assert.That(seenAtNotify, Is.EqualTo(42));
        }

        [Test]
        public void 건물을_추가하면_격자_점유도_함께_잡힌다()
        {
            StateStore store = StateTestFactory.NewStore();

            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "barracks", new GridPos(0, 0), GridFootprint.Square(2),
                BuildingPhase.Complete, 1, 0L)));

            Assert.That(store.State.BuildingCount, Is.EqualTo(1));
            Assert.That(store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(4));

            BuildingInstance found;
            Assert.That(store.State.TryGetBuildingAt(new GridPos(1, 1), out found), Is.True);
            Assert.That(found.DefId, Is.EqualTo("barracks"));
        }

        [Test]
        public void 건물을_치우면_점유도_풀린다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "farm", new GridPos(2, 2), GridFootprint.Square(3),
                BuildingPhase.Complete, 1, 0L)));

            store.Apply(new StateDelta().WithBuilding(BuildingChange.Remove(1)));

            Assert.That(store.State.BuildingCount, Is.EqualTo(0));
            Assert.That(store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(0));
        }

        [Test]
        public void 옮기면_옛_자리가_비고_새_자리가_찬다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "farm", new GridPos(0, 0), GridFootprint.Square(2),
                BuildingPhase.Complete, 1, 0L)));

            store.Apply(new StateDelta().WithBuilding(BuildingChange.Update(
                1, new GridPos(5, 5), BuildingPhase.Complete, 1, 0L)));

            Assert.That(store.State.BaseOccupancy.IsFree(new GridPos(0, 0)), Is.True);
            Assert.That(store.State.BaseOccupancy.IsFree(new GridPos(5, 5)), Is.False);
            Assert.That(store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(4));
        }

        [Test]
        public void 못_옮기는_자리로_보내면_원래_자리가_그대로_남는다()
        {
            // 되돌리기가 없으면 실패한 이동이 점유를 지워버려서, 건물은 남았는데
            // 그 칸이 비어 보이는 상태가 된다(다른 건물이 위에 겹쳐 지어진다).
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "a", new GridPos(0, 0), GridFootprint.Square(2), BuildingPhase.Complete, 1, 0L)));
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                2, "b", new GridPos(4, 4), GridFootprint.Square(2), BuildingPhase.Complete, 1, 0L)));

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithBuilding(BuildingChange.Update(
                    1, new GridPos(4, 4), BuildingPhase.Complete, 1, 0L))));

            BuildingInstance b1;
            store.State.TryGetBuilding(1, out b1);
            Assert.That(b1.Anchor, Is.EqualTo(new GridPos(0, 0)));
            Assert.That(store.State.BaseOccupancy.IsFree(new GridPos(0, 0)), Is.False);
            Assert.That(store.State.BaseOccupancy.OccupiedCellCount, Is.EqualTo(8));
        }

        [Test]
        public void 놓을_수_없는_자리에_추가하면_터진다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "a", new GridPos(0, 0), GridFootprint.Square(2), BuildingPhase.Complete, 1, 0L)));

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                    2, "b", new GridPos(1, 1), GridFootprint.Square(2), BuildingPhase.Complete, 1, 0L))));
        }

        [Test]
        public void 번호가_겹치는_추가는_터진다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "a", new GridPos(0, 0), GridFootprint.Single, BuildingPhase.Complete, 1, 0L)));

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                    1, "b", new GridPos(5, 5), GridFootprint.Single, BuildingPhase.Complete, 1, 0L))));
        }

        [Test]
        public void 없는_건물에_Update나_Remove가_오면_터진다()
        {
            StateStore store = StateTestFactory.NewStore();

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithBuilding(BuildingChange.Update(
                    99, GridPos.Zero, BuildingPhase.Complete, 1, 0L))));

            Assert.Throws<InvalidOperationException>(() =>
                store.Apply(new StateDelta().WithBuilding(BuildingChange.Remove(99))));
        }

        [Test]
        public void 추가한_번호보다_다음_번호가_커진다()
        {
            StateStore store = StateTestFactory.NewStore();

            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                10, "a", new GridPos(0, 0), GridFootprint.Single, BuildingPhase.Complete, 1, 0L)));

            Assert.That(store.State.NextBuildingId, Is.EqualTo(11));
        }
    }

    public class BuildingInstanceTests
    {
        [Test]
        public void 남은_시간은_끝나는_시각에서_지금을_뺀_값이다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "a", GridPos.Zero, GridFootprint.Single,
                BuildingPhase.Constructing, 0, 10_000L)));

            BuildingInstance b;
            store.State.TryGetBuilding(1, out b);

            Assert.That(b.RemainingConstructionMs(4_000L), Is.EqualTo(6_000L));
            Assert.That(b.IsConstructionFinished(4_000L), Is.False);

            // 시각을 지나면 완료로 판정되고, 남은 시간은 음수가 아니라 0이다.
            Assert.That(b.IsConstructionFinished(10_000L), Is.True);
            Assert.That(b.RemainingConstructionMs(12_000L), Is.EqualTo(0L));
        }

        [Test]
        public void 완성된_건물은_남은_시간이_0이고_다시_완료판정되지_않는다()
        {
            StateStore store = StateTestFactory.NewStore();
            store.Apply(new StateDelta().WithBuilding(BuildingChange.Add(
                1, "a", GridPos.Zero, GridFootprint.Single,
                BuildingPhase.Complete, 1, 0L)));

            BuildingInstance b;
            store.State.TryGetBuilding(1, out b);

            Assert.That(b.RemainingConstructionMs(0L), Is.EqualTo(0L));
            Assert.That(b.IsConstructionFinished(999_999L), Is.False);
        }
    }

    internal static class StateTestFactory
    {
        public static StateStore NewStore(int boardSize = 10)
        {
            var state = new GameState(
                new SquareGrid(1f),
                new GridRect(0, 0, boardSize, boardSize));

            return new StateStore(state);
        }
    }
}

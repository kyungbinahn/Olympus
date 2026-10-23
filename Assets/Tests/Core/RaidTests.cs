using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 도적 습격 — 본부 Lv.1이 되면 10초 뒤 첫 습격, 이후 30초마다. 도착 5초 전부터 보인다.
    /// 수비대 = soldier 2명, 기른 용사 최대 3명이 나간다. 도착 20초가 지나도 화면이 안 붙으면 Core가 결판낸다.
    /// </summary>
    public class RaidTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        private static ChapterConfig NewChapter(int bandits, double mul)
        {
            var training = new TrainingDef("soldier", ProgressionTestFactory.Lumber,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 2) }, 1000L, 5, 5, 0);
            var chapter = new ChapterConfig(training);

            chapter.SetRaids(new RaidDef(1, 10_000L, 30_000L, 5_000L, "soldier", 2, 1.0, 3, 10, 20_000L, 6, 60_000L,
                new List<RaidWave>
                {
                    new RaidWave(1, new List<EnemyGroup> { new EnemyGroup("brute", bandits, mul) },
                        new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 33) }),
                }));
            return chapter;
        }

        private void Setup(int bandits, double mul)
        {
            _clock = new ManualClock(1_000_000L);
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            _rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), NewChapter(bandits, mul));
            _rt.Initialize(ProgressionTestFactory.NewLayout());
        }

        private void RepairHq()
        {
            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot).Started, Is.True);
            _rt.Tick();
        }

        private void Arrive()
        {
            _clock.Advance(10_000L);
            _rt.Tick();
        }

        [Test]
        public void 본부가_열리기_전에는_도적이_오지_않는다()
        {
            Setup(1, 0.5);
            _clock.Advance(100_000L);
            _rt.Tick();
            Assert.That(_rt.Raids.Describe().Phase, Is.EqualTo(RaidPhase.Off));
        }

        [Test]
        public void 첫_습격은_기다렸다가_보이며_다가오고_도착한다()
        {
            Setup(1, 0.5);
            RepairHq();
            Assert.That(_rt.Raids.Describe().Phase, Is.EqualTo(RaidPhase.Waiting));

            _clock.Advance(7_500L);
            RaidView v = _rt.Raids.Describe();
            Assert.That(v.Phase, Is.EqualTo(RaidPhase.Approaching));
            Assert.That(v.Progress, Is.EqualTo(0.5f).Within(0.001f));

            _clock.Advance(2_500L);
            Assert.That(_rt.Raids.Describe().Phase, Is.EqualTo(RaidPhase.Arrived));
        }

        [Test]
        public void 이기면_전리품을_얻고_다음_습격이_잡힌다()
        {
            Setup(1, 0.5);
            RepairHq();
            Arrive();
            long stone = _rt.State.Resources.Get(ResourceKind.Stone);

            RaidOutcome o = _rt.Raids.Resolve();

            Assert.That(o.Won, Is.True);
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(stone + 33));
            Assert.That(_rt.Raids.Describe().ArrivesAtUnixMs, Is.EqualTo(_clock.NowUnixMs + 30_000L));
            Assert.That(_rt.State.Counters.Get(RaidService.CountKey), Is.EqualTo(1L));
        }

        [Test]
        public void 지면_목재와_석재를_빼앗기고_나간_용사만_잃는다()
        {
            Setup(6, 5.0);
            RepairHq();
            _rt.DebugGrantTroops(8);
            Arrive();
            long wood = _rt.State.Resources.Get(ResourceKind.Wood);

            RaidOutcome o = _rt.Raids.Resolve();

            Assert.That(o.Won, Is.False);
            Assert.That(o.TroopsSent, Is.EqualTo(3), "한 번에 최대 3명만 나간다");
            Assert.That(o.TroopsLost, Is.EqualTo(3));
            Assert.That(_rt.Training.Troops, Is.EqualTo(5L), "남은 용사는 병영에 있다");
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(wood - wood / 10));
        }

        [Test]
        public void 화면이_안_붙으면_도착_뒤_정해진_시간에_Core가_결판낸다()
        {
            Setup(1, 0.5);
            RepairHq();
            Arrive();
            bool fired = false;
            _rt.Raids.AutoResolved += _ => fired = true;

            _clock.Advance(19_000L);
            _rt.Tick();
            Assert.That(_rt.Raids.Describe().Phase, Is.EqualTo(RaidPhase.Arrived));

            _clock.Advance(1_000L);
            _rt.Tick();
            Assert.That(fired, Is.True);
            Assert.That(_rt.Raids.Describe().Phase, Is.Not.EqualTo(RaidPhase.Arrived));
        }

        [Test]
        public void 화면이_싸움을_잡으면_그동안_Core가_가로채지_않는다()
        {
            Setup(1, 0.5);
            RepairHq();
            Arrive();
            _clock.Advance(15_000L);
            Assert.That(_rt.Raids.Engage(), Is.True);

            _clock.Advance(10_000L);   // 도착으로부터는 25초, 잡은 뒤로는 10초
            _rt.Tick();
            Assert.That(_rt.Raids.Describe().Phase, Is.EqualTo(RaidPhase.Arrived));
            Assert.That(_rt.Raids.IsEngaged, Is.True);

            RaidOutcome o = _rt.Raids.Resolve();
            Assert.That(o.Resolved, Is.True);
            Assert.That(_rt.Raids.IsEngaged, Is.False);
        }

        [Test]
        public void 화면이_재생한_결과와_적용된_결과가_같다()
        {
            Setup(3, 1.2);
            RepairHq();
            _rt.DebugGrantTroops(3);
            Arrive();

            BattleSimulation sim = _rt.Raids.CreateSimulation();
            sim.RunToEnd();
            RaidOutcome o = _rt.Raids.Resolve();

            Assert.That(o.Won, Is.EqualTo(sim.PlayerWon));
        }

        [Test]
        public void 습격_규칙이_모르는_병사를_가리키면_검증이_잡는다()
        {
            var chapter = new ChapterConfig(null);
            chapter.SetRaids(new RaidDef(1, 0L, 1000L, 0L, "nobody", 1, 1.0, 0, 10, 1000L, 0, 1000L,
                new List<RaidWave> { new RaidWave(1, new List<EnemyGroup> { new EnemyGroup("ghost", 1, 1.0) }, null) }));

            List<string> problems = chapter.Validate(BattleTestFactory.NewConfig(), ProgressionTestFactory.NewCatalog());

            Assert.That(problems.Exists(p => p.Contains("nobody")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("ghost")), Is.True);
        }
    }
}

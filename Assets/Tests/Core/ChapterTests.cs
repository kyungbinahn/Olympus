using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    /// <summary>
    /// 1장 "300 용사" — 훈련과 다가오는 거인. 진척 시험의 축소판 기지에서 목재소(Lumber)를 병영으로 쓴다.
    /// 병사 한 명 = 목재 2, 1초. 목표 10명, 거인은 본부 Lv.1에 보이고 60초 뒤 도착.
    /// </summary>
    public class ChapterTests
    {
        private const string ThreatId = "giant_test";

        private ManualClock _clock;
        private TerritoryRuntime _rt;

        private static ChapterConfig NewChapter(long giantHp = 50)
        {
            var training = new TrainingDef("soldier", ProgressionTestFactory.Lumber,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 2) }, 1000L, 5, 5, 100);

            var chapter = new ChapterConfig(training);
            chapter.AddThreat(new ThreatDef(ThreatId, 1, 60_000L, 30_000L, 10, "soldier",
                new List<EnemyGroup> { new EnemyGroup("brute", 1, giantHp / 100.0) }, 0f, 5, 60_000L,
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 99) }, null, null, null, null));
            return chapter;
        }

        private TerritoryRuntime NewRuntime(ChapterConfig chapter)
        {
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), chapter);
            rt.Initialize(ProgressionTestFactory.NewLayout());
            return rt;
        }

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = NewRuntime(NewChapter());
        }

        private void RepairBarracks()
        {
            // 목재소는 본부 Lv.1에 열린다.
            if (ProgressionTestFactory.Get(_rt, ProgressionTestFactory.HqSlot).IsRuined)
                RepairHq();

            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot).Started, Is.True);
            _clock.Advance(10_000L);
            _rt.Tick();
        }

        private void RepairHq()
        {
            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot).Started, Is.True);
            _rt.Tick();
        }

        private void Train(int count)
        {
            Assert.That(_rt.Training.TryStart(count), Is.EqualTo(TrainingRejection.None));
            _clock.Advance(count * 1000L);
            _rt.Tick();
        }

        [Test]
        public void 병영이_없으면_훈련할_수_없다()
        {
            Assert.That(_rt.Training.CanStart(1), Is.EqualTo(TrainingRejection.NoBarracks));
        }

        [Test]
        public void 훈련은_자원을_먼저_떼고_끝나는_시각에_병사가_들어온다()
        {
            RepairBarracks();
            long wood = _rt.State.Resources.Get(ResourceKind.Wood);

            Assert.That(_rt.Training.TryStart(4), Is.EqualTo(TrainingRejection.None));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(wood - 8));

            _clock.Advance(2_000L);
            _rt.Tick();
            TrainingView mid = _rt.Training.Describe();
            Assert.That(mid.Running, Is.True);
            Assert.That(mid.DoneSoFar, Is.EqualTo(2), "절반 시간이면 절반이 다 자란 것으로 보인다");
            Assert.That(mid.Troops, Is.EqualTo(0L), "주문이 끝나기 전에는 들어오지 않는다");
            Assert.That(mid.VisibleTroops, Is.EqualTo(2L));

            _clock.Advance(2_000L);
            _rt.Tick();
            Assert.That(_rt.Training.Troops, Is.EqualTo(4L));
            Assert.That(_rt.State.Counters.Get(ChapterKeys.Trained), Is.EqualTo(4L));
            Assert.That(_rt.Training.Describe().Running, Is.False);
        }

        [Test]
        public void 한도를_넘거나_훈련_중이면_거부한다()
        {
            RepairBarracks();
            Assert.That(_rt.Training.CanStart(6), Is.EqualTo(TrainingRejection.OverCap));
            Assert.That(_rt.Training.CanStart(0), Is.EqualTo(TrainingRejection.ZeroCount));

            _rt.Training.TryStart(5);
            Assert.That(_rt.Training.CanStart(1), Is.EqualTo(TrainingRejection.Busy));
        }

        [Test]
        public void 자원이_모자라면_거부하고_최대치는_자원과_한도_중_작은_쪽이다()
        {
            RepairBarracks();
            _rt.Store.Apply(new StateDelta().WithResource(ResourceChange.Absolute(ResourceKind.Wood, 7)));

            Assert.That(_rt.Training.MaxAffordable(), Is.EqualTo(3));
            Assert.That(_rt.Training.CanStart(4), Is.EqualTo(TrainingRejection.NotEnoughResources));
        }

        [Test]
        public void 날개로_훈련을_당기면_지나간_만큼_바로_들어온다()
        {
            RepairBarracks();
            _rt.Store.Apply(new StateDelta().WithItem(CountChange.Delta(ProgressionTestFactory.Wing1m, 1)));

            _rt.Training.TryStart(5);
            Assert.That(_rt.Training.TrySpeedUp(ProgressionTestFactory.Wing1m), Is.EqualTo(TrainingRejection.None));
            Assert.That(_rt.Training.Troops, Is.EqualTo(5L), "1분 날개가 5초 주문을 끝냈다");
        }

        [Test]
        public void 거인은_본부가_열리면_보이고_도착_시각으로_걸어온다()
        {
            ThreatDef t = _rt.Threats.Current;
            Assert.That(_rt.Threats.Describe(t).Phase, Is.EqualTo(ThreatPhase.Hidden));

            RepairHq();
            ThreatView v = _rt.Threats.Describe(t);
            Assert.That(v.Phase, Is.EqualTo(ThreatPhase.Approaching));
            Assert.That(v.ArrivesAtUnixMs, Is.EqualTo(_clock.NowUnixMs + 60_000L));

            _clock.Advance(30_000L);
            Assert.That(_rt.Threats.Describe(t).Progress, Is.EqualTo(0.5f).Within(0.001f));

            _clock.Advance(30_000L);
            Assert.That(_rt.Threats.Describe(t).Phase, Is.EqualTo(ThreatPhase.Arrived));
        }

        [Test]
        public void 도착_전에는_목표_수를_채워야_출정한다()
        {
            RepairHq();
            RepairBarracks();
            Train(5);

            Assert.That(_rt.Threats.CanDispatch(ThreatId), Is.EqualTo(ThreatRejection.NotEnoughTroops));

            Train(5);
            Assert.That(_rt.Threats.CanDispatch(ThreatId), Is.EqualTo(ThreatRejection.None));
        }

        [Test]
        public void 거인이_도착하면_훈련이_막히고_가진_병사로_싸울_수_있다()
        {
            RepairHq();
            RepairBarracks();
            Train(2);

            _clock.Advance(60_000L);
            _rt.Tick();

            Assert.That(_rt.Training.CanStart(1), Is.EqualTo(TrainingRejection.GiantAtGate));
            Assert.That(_rt.Threats.CanDispatch(ThreatId), Is.EqualTo(ThreatRejection.None), "도착하면 목표 미달이어도 맞선다");
        }

        [Test]
        public void 지면_병사를_잃고_거인이_물러났다가_다시_온다()
        {
            _rt = NewRuntime(NewChapter(giantHp: 100_000));
            RepairHq();
            RepairBarracks();
            Train(3);
            _clock.Advance(60_000L);
            _rt.Tick();

            ThreatOutcome o = _rt.Threats.Resolve(ThreatId);

            Assert.That(o.Won, Is.False);
            Assert.That(o.Sent, Is.EqualTo(3));
            Assert.That(_rt.Training.Troops, Is.EqualTo(0L));
            ThreatView v = _rt.Threats.Describe(_rt.Threats.Current);
            Assert.That(v.Phase, Is.EqualTo(ThreatPhase.Approaching));
            Assert.That(v.ArrivesAtUnixMs, Is.EqualTo(_clock.NowUnixMs + 30_000L));
            Assert.That(v.Progress, Is.EqualTo(0f).Within(0.001f), "다시 오는 길은 처음부터 걷는다");
            Assert.That(_rt.Training.CanStart(1), Is.EqualTo(TrainingRejection.None), "물러나면 다시 기를 수 있다");
        }

        [Test]
        public void 이기면_보상과_이김_카운터가_들어오고_살아남은_병사가_돌아온다()
        {
            RepairHq();
            RepairBarracks();
            Train(5);
            Train(5);
            long stone = _rt.State.Resources.Get(ResourceKind.Stone);

            ThreatOutcome o = _rt.Threats.Resolve(ThreatId);

            Assert.That(o.Won, Is.True);
            Assert.That(_rt.State.Counters.Get(ChapterKeys.ThreatWon(ThreatId)), Is.EqualTo(1L));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(stone + 99));
            Assert.That(_rt.Training.Troops, Is.EqualTo((long)o.Survivors));
            Assert.That(_rt.Threats.Current, Is.Null, "남은 위협이 없다");
        }

        [Test]
        public void 화면이_재생한_결과와_적용된_결과가_같다()
        {
            RepairHq();
            RepairBarracks();
            Train(5);
            Train(5);

            BattleSimulation sim = _rt.Threats.CreateSimulation(ThreatId);
            sim.RunToEnd();
            ThreatOutcome o = _rt.Threats.Resolve(ThreatId);

            Assert.That(o.Won, Is.EqualTo(sim.PlayerWon));
            Assert.That(o.Survivors, Is.EqualTo(sim.AliveCount(BattleTeam.Player)));
        }

        [Test]
        public void 훈련_주문과_거인_도착_시각이_세이브에서_살아난다()
        {
            RepairHq();
            RepairBarracks();
            _rt.Training.TryStart(5);
            long arrives = _rt.Threats.Describe(_rt.Threats.Current).ArrivesAtUnixMs;

            var save = _rt.CaptureSave();
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var loaded = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), NewChapter());
            loaded.InitializeFromSave(ProgressionTestFactory.NewLayout(), save);

            Assert.That(loaded.Training.Describe().OrderCount, Is.EqualTo(5));
            Assert.That(loaded.Threats.Describe(loaded.Threats.Current).ArrivesAtUnixMs, Is.EqualTo(arrives));

            _clock.Advance(5_000L);
            loaded.Tick();
            Assert.That(loaded.Training.Troops, Is.EqualTo(5L));
        }

        [Test]
        public void 휩쓸기는_목표_둘레의_적을_함께_친다()
        {
            var giant = new UnitDef("giant", "unit.giant", 10_000, 500, 1000, 1.0f, 0f, 1.0f, 1.0f);
            var plain = new UnitDef("giant", "unit.giant", 10_000, 500, 1000, 1.0f, 0f, 1.0f);

            int Killed(UnitDef g)
            {
                var sim = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(), 12),
                    new List<UnitSpawn> { new UnitSpawn(g, 1.0, 1.0) }, 60_000L, 12);
                for (int i = 0; i < 200 && sim.AliveCount(BattleTeam.Player) == 12 && !sim.IsFinished; i++)
                    sim.Step();
                return 12 - sim.AliveCount(BattleTeam.Player);
            }

            Assert.That(Killed(plain), Is.EqualTo(1), "휩쓸기가 없으면 한 번에 한 명");
            Assert.That(Killed(giant), Is.GreaterThan(1), "휩쓸기는 한 번에 여럿");
        }

        [Test]
        public void 장_규칙이_모르는_병사나_건물을_가리키면_검증이_잡는다()
        {
            var training = new TrainingDef("nobody", "nowhere", null, 1000L, 1, 0, 0);
            var chapter = new ChapterConfig(training);
            chapter.AddThreat(new ThreatDef("t", 1, 1000L, 1000L, 1, "soldier",
                new List<EnemyGroup> { new EnemyGroup("ghost", 1, 1.0) }, 0f, 0, 1000L, null, null, null, null, null));

            List<string> problems = chapter.Validate(BattleTestFactory.NewConfig(), ProgressionTestFactory.NewCatalog());

            Assert.That(problems.Exists(p => p.Contains("nobody")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("nowhere")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("ghost")), Is.True);
            Assert.That(problems.Exists(p => p.Contains("훈련이 없다")), Is.True);
        }
    }
}

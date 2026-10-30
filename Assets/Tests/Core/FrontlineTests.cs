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
    /// 성문 밖 상시 전선. 축소판 기지의 군대 = 기본 수비대 2명(soldier, 공격 10/1초) + 목재소 레벨마다 1명,
    /// 목재소 레벨마다 공격 +50%. 본부 Lv.0이면 화력 = 2 × 10 = 20/초.
    /// 기지 1: 체력 1000, 버팀 5 / 기지 2: 체력 1000, 버팀 100(처음엔 못 깎는다). 피해 1000마다 목재 50.
    /// </summary>
    public class FrontlineTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        private static ChapterConfig NewChapter()
        {
            var chapter = new ChapterConfig(null);
            chapter.SetFrontline(new FrontlineDef(
                new List<OutpostDef>
                {
                    new OutpostDef("den", "outpost.den", 1000, 5, 0, "brute",
                        new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 77) }, null, "story.den"),
                    new OutpostDef("fort", "outpost.fort", 1000, 100, 0, "brute", null, null, null),
                },
                new List<ResourceCost> { new ResourceCost(ResourceKind.Wood, 50) }));
            return chapter;
        }

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            _rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), NewChapter());
            _rt.Initialize(ProgressionTestFactory.NewLayout());
            _rt.Tick();
        }

        [Test]
        public void 화력은_건물이_주는_군대의_초당_공격이다()
        {
            Assert.That(_rt.Frontline.Power(), Is.EqualTo(20L));
        }

        [Test]
        public void 화력에서_버팀을_뺀_만큼_시간에_따라_깎인다()
        {
            _clock.Advance(10_000L);
            FrontView v = _rt.Frontline.Describe();

            Assert.That(v.Rate, Is.EqualTo(15L));
            Assert.That(v.Damage, Is.EqualTo(150L));
            Assert.That(v.RemainingMs, Is.EqualTo((1000L - 150L) * 1000L / 15L));
        }

        [Test]
        public void 무너지면_보상과_전리품이_들어오고_다음_기지로_넘어간다()
        {
            long stone = _rt.State.Resources.Get(ResourceKind.Stone);
            long wood = _rt.State.Resources.Get(ResourceKind.Wood);
            OutpostDef fallen = null;
            _rt.Frontline.OutpostDestroyed += o => fallen = o;

            _clock.Advance(70_000L);   // 15/초 → 66.7초에 무너진다
            _rt.Tick();

            Assert.That(fallen, Is.Not.Null);
            Assert.That(fallen.OutpostId, Is.EqualTo("den"));
            Assert.That(_rt.State.Counters.Get(FrontlineService.IndexKey), Is.EqualTo(1L));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(stone + 77));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(wood + 50), "피해 1000 → 목재 50");
            Assert.That(_rt.Frontline.Describe().Outpost.OutpostId, Is.EqualTo("fort"));
        }

        [Test]
        public void 화력이_버팀보다_약하면_밀린다고_알린다()
        {
            _clock.Advance(70_000L);
            _rt.Tick();

            FrontView v = _rt.Frontline.Describe();
            Assert.That(v.Stalled, Is.True);
            Assert.That(v.RemainingMs, Is.EqualTo(-1L));

            _clock.Advance(600_000L);
            Assert.That(_rt.Frontline.Describe().Damage, Is.EqualTo(0L), "밀리는 동안은 안 깎인다");
        }

        [Test]
        public void 건물이_바뀌면_그때까지는_옛_화력으로_정산하고_새_화력으로_갈아탄다()
        {
            // 목재소 복구(레벨 1) → 병사 +1, 공격 +50% → 화력 3 × 15 = 45, 속도 40.
            _clock.Advance(10_000L);   // 여기까지 15/초 × 10초 = 150
            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot).Started, Is.True);
            _rt.Tick();
            Assert.That(_rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot).Started, Is.True);
            _rt.DebugCompleteAllConstruction();

            FrontView v = _rt.Frontline.Describe();
            Assert.That(v.Damage, Is.EqualTo(150L));
            Assert.That(v.Power, Is.GreaterThan(20L));

            long rate = v.Rate;
            _clock.Advance(10_000L);
            Assert.That(_rt.Frontline.Describe().Damage, Is.EqualTo(150L + rate * 10L));
        }

        [Test]
        public void 앱을_꺼_둔_사이에도_여러_기지가_무너지고_남은_시간은_다음으로_넘어간다()
        {
            var chapter = new ChapterConfig(null);
            chapter.SetFrontline(new FrontlineDef(new List<OutpostDef>
            {
                new OutpostDef("a", "a", 150, 5, 0, null, null, null, null),
                new OutpostDef("b", "b", 150, 5, 0, null, null, null, null),
                new OutpostDef("c", "c", 150000, 5, 0, null, null, null, null),
            }, null));
            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), chapter);
            rt.Initialize(ProgressionTestFactory.NewLayout());
            rt.Tick();

            _clock.Advance(30_000L);   // 15/초: a 10초, b 10초, c에 10초 = 150
            rt.Tick();

            Assert.That(rt.State.Counters.Get(FrontlineService.IndexKey), Is.EqualTo(2L));
            Assert.That(rt.Frontline.Describe().Damage, Is.EqualTo(150L));
        }

        [Test]
        public void 전선_진행이_세이브에서_살아난다()
        {
            _clock.Advance(20_000L);
            _rt.Tick();
            var save = _rt.CaptureSave();
            long damage = _rt.Frontline.Describe().Damage;

            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var loaded = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), _clock, 2f,
                ProgressionTestFactory.NewConfig(), BattleTestFactory.NewConfig(), NewChapter());
            loaded.InitializeFromSave(ProgressionTestFactory.NewLayout(), save);

            Assert.That(loaded.Frontline.Describe().Damage, Is.EqualTo(damage));

            _clock.Advance(10_000L);
            loaded.Tick();
            Assert.That(loaded.Frontline.Describe().Damage, Is.EqualTo(damage + 150L), "꺼 둔 사이에도 싸웠다");
        }

        [Test]
        public void 모르는_적을_가리키면_검증이_잡는다()
        {
            var chapter = new ChapterConfig(null);
            chapter.SetFrontline(new FrontlineDef(new List<OutpostDef>
            {
                new OutpostDef("x", "x", 10, 0, 0, "ghost", null, null, null),
            }, null));

            List<string> problems = chapter.Validate(BattleTestFactory.NewConfig(), ProgressionTestFactory.NewCatalog());
            Assert.That(problems.Exists(p => p.Contains("ghost")), Is.True);
        }
    }
}

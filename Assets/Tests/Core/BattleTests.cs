using System.Collections.Generic;
using NUnit.Framework;
using Olympus.Core.Battle;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Core.Tests
{
    internal static class BattleTestFactory
    {
        public static UnitDef Soldier(long hp = 100, long attack = 10) =>
            new UnitDef("soldier", "unit.soldier", hp, attack, 1000, 0.8f, 2f, 0.4f);

        public static UnitDef Brute(long hp = 100, long attack = 10) =>
            new UnitDef("brute", "unit.brute", hp, attack, 1000, 0.8f, 2f, 0.4f);

        public static List<UnitSpawn> Many(UnitDef def, int count, double mul = 1.0)
        {
            var list = new List<UnitSpawn>();
            for (int i = 0; i < count; i++)
                list.Add(new UnitSpawn(def, mul, mul));
            return list;
        }

        /// <summary>
        /// 진척 시험의 축소판 기지 위에 전투 규칙을 얹는다. 기본 수비대 2명 + 목재소(Lumber) 레벨마다 1명,
        /// 목재소 레벨마다 공격 +50%. 스테이지 2개.
        /// </summary>
        public static BattleConfig NewConfig()
        {
            var config = new BattleConfig(60_000L);
            config.AddUnit(Soldier());
            config.AddUnit(Brute());
            config.AddSource(new ArmySource(null, "soldier", 2, 0));
            config.AddSource(new ArmySource(ProgressionTestFactory.Lumber, "soldier", 0, 1));
            config.AddBuff(new ArmyBuff(ProgressionTestFactory.Lumber, null, BattleStat.Attack, 50));

            config.AddStage(new StageDef(1, "stage.s01",
                new List<EnemyGroup> { new EnemyGroup("brute", 1, 1.0) },
                new List<ResourceCost> { new ResourceCost(ResourceKind.Stone, 70) },
                new List<ItemGrant> { new ItemGrant(ProgressionTestFactory.Wing1m, 1) }));

            config.AddStage(new StageDef(2, "stage.s02",
                new List<EnemyGroup> { new EnemyGroup("brute", 6, 2.0) }, null, null));

            return config;
        }

        public static TerritoryRuntime NewRuntime(ManualClock clock)
        {
            ProgressionConfig progression = ProgressionTestFactory.NewConfig();
            progression.Features.Add(BattleService.Feature, 1);

            var state = new GameState(new SquareGrid(1f), new GridRect(0, 0, 20, 20));
            var rt = new TerritoryRuntime(state, ProgressionTestFactory.NewCatalog(), clock, 2f, progression, NewConfig());
            rt.Initialize(ProgressionTestFactory.NewLayout());
            return rt;
        }
    }

    public class BattleSimulationTests
    {
        [Test]
        public void 같은_군대면_결과가_늘_같다()
        {
            var a = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(), 5),
                BattleTestFactory.Many(BattleTestFactory.Brute(), 5, 1.05), 60_000L);
            var b = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(), 5),
                BattleTestFactory.Many(BattleTestFactory.Brute(), 5, 1.05), 60_000L);

            a.RunToEnd();
            b.RunToEnd();

            Assert.That(a.PlayerWon, Is.EqualTo(b.PlayerWon));
            Assert.That(a.ElapsedMs, Is.EqualTo(b.ElapsedMs));
            Assert.That(a.AliveCount(BattleTeam.Player), Is.EqualTo(b.AliveCount(BattleTeam.Player)));
        }

        [Test]
        public void 수가_많은_쪽이_이긴다()
        {
            var sim = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(), 8),
                BattleTestFactory.Many(BattleTestFactory.Brute(), 3), 60_000L);
            sim.RunToEnd();

            Assert.That(sim.PlayerWon, Is.True);
            Assert.That(sim.AliveCount(BattleTeam.Enemy), Is.EqualTo(0));
        }

        [Test]
        public void 훨씬_센_적에게는_진다()
        {
            var sim = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(), 3),
                BattleTestFactory.Many(BattleTestFactory.Brute(), 3, 3.0), 60_000L);
            sim.RunToEnd();

            Assert.That(sim.PlayerWon, Is.False);
        }

        [Test]
        public void 제한_시간_안에_못_끝내면_진다()
        {
            // 공격력 0끼리 — 영원히 안 끝난다.
            var sim = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(attack: 0), 2),
                BattleTestFactory.Many(BattleTestFactory.Brute(attack: 0), 2), 5_000L);
            sim.RunToEnd();

            Assert.That(sim.IsFinished, Is.True);
            Assert.That(sim.PlayerWon, Is.False);
            Assert.That(sim.ElapsedMs, Is.EqualTo(5_000L));
        }

        [Test]
        public void 걸어가서_치고_죽으면_사건이_남는다()
        {
            var sim = new BattleSimulation(BattleTestFactory.Many(BattleTestFactory.Soldier(attack: 100), 1),
                BattleTestFactory.Many(BattleTestFactory.Brute(hp: 50), 1), 60_000L);

            bool sawMove = false, sawAttack = false, sawDeath = false;
            while (!sim.IsFinished)
            {
                sim.Step();
                sawMove |= sim.Units[0].IsMoving;
                foreach (BattleEvent e in sim.LastEvents)
                {
                    sawAttack |= e.Kind == BattleEventKind.Attack;
                    sawDeath |= e.Kind == BattleEventKind.Death;
                }
            }

            Assert.That(sawMove && sawAttack && sawDeath, Is.True);
            Assert.That(sim.PlayerWon, Is.True);
        }

        [Test]
        public void 활은_뒷줄에_선다()
        {
            var archer = new UnitDef("archer", "unit.archer", 50, 5, 1000, 6f, 2f, 0.35f);
            var army = BattleTestFactory.Many(BattleTestFactory.Soldier(), 2);
            army.Add(new UnitSpawn(archer, 1, 1));

            var sim = new BattleSimulation(army, BattleTestFactory.Many(BattleTestFactory.Brute(), 1), 60_000L);

            BattleUnit bow = null, front = null;
            foreach (BattleUnit u in sim.Units)
            {
                if (u.Team != BattleTeam.Player) continue;
                if (u.Def.IsRanged) bow = u; else front = u;
            }

            Assert.That(bow.Z, Is.LessThan(front.Z), "아군은 -Z 쪽에 서고 적을 향해 +Z로 간다 — 활이 더 뒤다.");
        }
    }

    public class BattleServiceTests
    {
        private ManualClock _clock;
        private TerritoryRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock(1_000_000L);
            _rt = BattleTestFactory.NewRuntime(_clock);
        }

        [Test]
        public void 본부가_폐허면_전선이_잠겨_있다()
        {
            Assert.That(_rt.Battles.Preview(1).Rejection, Is.EqualTo(BattleRejection.Locked));
            Assert.That(_rt.Battles.Fight(1).Rejection, Is.EqualTo(BattleRejection.Locked));
        }

        [Test]
        public void 군대는_기본_수비대와_건물_레벨에서_나온다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            Assert.That(_rt.Battles.BuildPlayerArmy().Count, Is.EqualTo(2));

            _rt.Construction.TryStartRepair(ProgressionTestFactory.LumberSlot);
            _rt.DebugCompleteAllConstruction();

            List<UnitSpawn> army = _rt.Battles.BuildPlayerArmy();
            Assert.That(army.Count, Is.EqualTo(3));
            Assert.That(army[0].AttackMultiplier, Is.EqualTo(1.5).Within(1e-9), "목재소 Lv.1 → 공격 +50%");
        }

        [Test]
        public void 이기면_보상과_진도가_들어오고_다음_스테이지로_넘어간다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            long stone = _rt.State.Resources.Get(ResourceKind.Stone);

            BattleOutcome outcome = _rt.Battles.Fight(1);

            Assert.That(outcome.Won, Is.True);
            Assert.That(_rt.State.Resources.Get(ResourceKind.Stone), Is.EqualTo(stone + 70));
            Assert.That(_rt.Battles.HighestCleared, Is.EqualTo(1));
            Assert.That(_rt.Battles.NextStage.StageId, Is.EqualTo(2));
        }

        [Test]
        public void 한_번_깬_스테이지는_다시_못_싸운다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Battles.Fight(1);

            Assert.That(_rt.Battles.Fight(1).Rejection, Is.EqualTo(BattleRejection.NotNextStage));
        }

        [Test]
        public void 지면_아무것도_바뀌지_않는다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            _rt.Battles.Fight(1);
            long wood = _rt.State.Resources.Get(ResourceKind.Wood);

            BattleOutcome outcome = _rt.Battles.Fight(2);

            Assert.That(outcome.Rejection, Is.EqualTo(BattleRejection.None));
            Assert.That(outcome.Won, Is.False);
            Assert.That(_rt.Battles.HighestCleared, Is.EqualTo(1));
            Assert.That(_rt.State.Resources.Get(ResourceKind.Wood), Is.EqualTo(wood));
        }

        [Test]
        public void 화면이_재생하는_시뮬레이션과_적용하는_결과가_같다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);

            BattleSimulation shown = _rt.Battles.CreateSimulation(1);
            shown.RunToEnd();

            BattleOutcome applied = _rt.Battles.Fight(1);

            Assert.That(applied.Won, Is.EqualTo(shown.PlayerWon));
            Assert.That(applied.DurationMs, Is.EqualTo(shown.ElapsedMs));
        }

        [Test]
        public void 깬_스테이지는_본부_조건과_임무에_쓰인다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            var r = Requirement.Counter(BattleService.StageCounter, 1);

            Assert.That(ProgressionRules.Evaluate(r, _rt.State, _rt.Progression).IsMet, Is.False);
            _rt.Battles.Fight(1);
            Assert.That(ProgressionRules.Evaluate(r, _rt.State, _rt.Progression).IsMet, Is.True);
        }

        [Test]
        public void 전투력은_양쪽_다_보여준다()
        {
            _rt.Construction.TryStartRepair(ProgressionTestFactory.HqSlot);
            BattlePreview p = _rt.Battles.Preview(1);

            Assert.That(p.CanFight, Is.True);
            Assert.That(p.PlayerPower, Is.GreaterThan(0));
            Assert.That(p.EnemyPower, Is.GreaterThan(0));
            Assert.That(p.PlayerCount, Is.EqualTo(2));
            Assert.That(p.EnemyCount, Is.EqualTo(1));
        }
    }
}

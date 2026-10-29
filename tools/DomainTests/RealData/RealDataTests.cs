using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.DomainTests.RealData
{
    /// <summary>
    /// 실제 데이터 파일로 본부 Lv.1→5를 끝까지 밟아 본다.
    ///
    /// 왜 필요한가 — 진척 숫자는 사람이 JSON을 고치며 맞춘다. 어느 날 본부 Lv.5 조건을
    /// 번영도 60으로 올렸는데 건물 상한상 최대 번영도가 55라면, 게임은 안 터지고 Lv.4에서
    /// 영원히 막힌다. 그건 한 시간을 플레이해야 발견된다. 이 시험은 그걸 1초에 발견한다.
    ///
    /// 걸린 시간(분)도 찍는다 — 탐욕적으로(할 수 있는 건 즉시) 두는 봇 기준이라 사람보다 빠르다.
    /// 곡선을 고친 뒤 이 숫자가 어떻게 움직이는지 보면 된다.
    /// </summary>
    public class RealDataTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        private static string DataDir
        {
            get
            {
                // bin/Debug/net8.0 에서 레포 뿌리까지 올라간다.
                DirectoryInfo dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "Data")))
                    dir = dir.Parent;

                Assert.That(dir, Is.Not.Null, "Assets/Data를 찾지 못했다.");
                return Path.Combine(dir.FullName, "Assets", "Data");
            }
        }

        private static T Load<T>(string file)
        {
            string text = File.ReadAllText(Path.Combine(DataDir, file));
            return JsonSerializer.Deserialize<T>(text, Options);
        }

        private sealed class LayoutFile
        {
            public int baseSize;
            public List<SlotRecord> slots;
        }

        private sealed class SlotRecord
        {
            public int slotId;
            public string defId;
            public int x;
            public int y;
            public int zoneId;
        }

        private sealed class StringsFile
        {
            public List<StringRecord> strings;
        }

        private sealed class StringRecord
        {
            public string key;
            public string text;
        }

        private static BuildingCatalog Catalog() => Load<BuildingDefFile>("building-defs.json").ToCatalog("building-defs.json");

        private static BattleConfig Battle() => Load<BattleFile>("battle.json").ToConfig();

        private static ChapterConfig Chapter() => Load<ChapterFile>("chapter1.json").ToConfig();

        private static ProgressionConfig Config() => ProgressionFileMapper.ToConfig(
            Load<ProgressionFile>("progression.json"),
            Load<QuestFile>("quests.json"),
            Load<FeatureUnlockFile>("function-unlock.json"));

        [Test]
        public void 진척_데이터가_건물_정의와_맞는다()
        {
            List<string> problems = Config().Validate(Catalog());
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void 전투_데이터가_건물_정의와_맞는다()
        {
            List<string> problems = Battle().Validate(Catalog(), Config());
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void 장_데이터가_전투_건물_정의와_맞는다()
        {
            List<string> problems = Chapter().Validate(Battle(), Catalog());
            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// 300 vs 1의 손맛 — 300이면 이기고, 절반이면 져야 "300을 채우는" 목표가 의미를 가진다.
        /// 본부 Lv.3(공격 +15%) 무렵 군대로 잰다. 숫자를 고치면 이 표가 바뀐다.
        /// </summary>
        [Test]
        public void 거인_하나는_300이면_이기고_절반이면_진다()
        {
            BattleConfig battle = Battle();
            ThreatDef t = Chapter().Threats[0];
            UnitDef hoplite, giant;
            battle.TryGetUnit(t.TroopUnitId, out hoplite);
            battle.TryGetUnit(t.Enemies[0].UnitId, out giant);

            var lines = new List<string>();
            bool won300 = false, won150 = true, won200 = true;
            foreach (int n in new[] { 100, 150, 200, 250, 300 })
            {
                var player = new List<UnitSpawn>();
                for (int i = 0; i < n; i++) player.Add(new UnitSpawn(hoplite, 1.0, 1.15));
                var enemy = new List<UnitSpawn> { new UnitSpawn(giant, t.Enemies[0].Multiplier, t.Enemies[0].Multiplier, t.EnemyScale) };

                var sim = new BattleSimulation(player, enemy, t.TimeLimitMs, t.RowWidth, t.Spacing, t.StartDistance);
                sim.RunToEnd();

                BattleUnit g = sim.Units.First(u => u.Team == BattleTeam.Enemy);
                lines.Add(n + "명: " + (sim.PlayerWon ? "승" : "패") + " " + (sim.ElapsedMs / 1000.0).ToString("0") + "초, 생존 " +
                    sim.AliveCount(BattleTeam.Player) + ", 거인 체력 " + g.Hp + "/" + g.MaxHp);
                if (n == 300) won300 = sim.PlayerWon;
                if (n == 150) won150 = sim.PlayerWon;
                if (n == 200) won200 = sim.PlayerWon;
            }

            TestContext.WriteLine(string.Join(Environment.NewLine, lines));
            Assert.That(won300, Is.True, "300명이 진다: " + string.Join(Environment.NewLine, lines));
            Assert.That(won150, Is.False, "150명으로도 이긴다: " + string.Join(Environment.NewLine, lines));
            Assert.That(won200, Is.False, "설계서 03 3절 부록 표(200명 패)와 다르다 — 200명으로도 이긴다: " + string.Join(Environment.NewLine, lines));
        }

        /// <summary>
        /// 거인 2·3 — 같은 300이 무장(본부·제철소·성문·치유의 집·투기장 강화)을 갖춰야 이긴다(docs/village-growth.md 4-3).
        /// 무장은 그 거인과 맞붙을 무렵 봇의 강화 배수(봇 시험 출력의 "무장")에 맞춘 값이다. 숫자를 고치면 이 표가 바뀐다.
        /// </summary>
        [TestCase("giant_2", 1.4, 1.15)]
        [TestCase("giant_3", 1.55, 1.7)]
        public void 다음_거인은_무장한_300이면_이기고_200이면_진다(string threatId, double hp, double attack)
        {
            BattleConfig battle = Battle();
            ThreatDef t;
            Assert.That(Chapter().TryGetThreat(threatId, out t), Is.True, threatId + "가 chapter1.json에 없다");
            UnitDef hoplite;
            battle.TryGetUnit(t.TroopUnitId, out hoplite);

            var lines = new List<string>();
            bool won300 = false, won200 = true;
            int survivors300 = 0;
            foreach (int n in new[] { 150, 200, 250, 300, 350 })
            {
                var player = new List<UnitSpawn>();
                for (int i = 0; i < n; i++) player.Add(new UnitSpawn(hoplite, hp, attack));
                var enemy = new List<UnitSpawn>();
                foreach (EnemyGroup g in t.Enemies)
                {
                    UnitDef def;
                    battle.TryGetUnit(g.UnitId, out def);
                    for (int k = 0; k < g.Count; k++) enemy.Add(new UnitSpawn(def, g.Multiplier, g.Multiplier, t.EnemyScale));
                }

                var sim = new BattleSimulation(player, enemy, t.TimeLimitMs, t.RowWidth, t.Spacing, t.StartDistance);
                sim.RunToEnd();
                lines.Add(n + "명: " + (sim.PlayerWon ? "승" : "패") + " " + (sim.ElapsedMs / 1000.0).ToString("0") + "초, 생존 " +
                          sim.AliveCount(BattleTeam.Player) + ", 남은 적 " + sim.AliveCount(BattleTeam.Enemy));
                if (n == 300) { won300 = sim.PlayerWon; survivors300 = sim.AliveCount(BattleTeam.Player); }
                if (n == 200) won200 = sim.PlayerWon;
            }

            string table = string.Join(Environment.NewLine, lines);
            TestContext.WriteLine(threatId + " (체력 ×" + hp + ", 공격 ×" + attack + ")" + Environment.NewLine + table);
            Assert.That(won300, Is.True, "무장한 300명이 진다: " + table);
            Assert.That(won200, Is.False, "200명으로도 이긴다: " + table);
            Assert.That(survivors300, Is.InRange(30, 150), "300명 생존이 30~90 목표에서 많이 벗어남: " + table);
        }

        /// <summary>본부가 땅보다 앞서면 새로 열린 건물이 성 밖에 선다 — 본부 Lv.N 조건에 영토 N 조건이 있어야 한다.</summary>
        [Test]
        public void 본부_조건에_영토_조건이_들어_있다()
        {
            List<string> problems = Load<WallFile>("walls.json").ToConfig().CheckAgainst(Config());
            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        [Test]
        public void 화면에_뜨는_문구_키가_전부_문자열_표에_있다()
        {
            HashSet<string> keys = new HashSet<string>(Load<StringsFile>("strings-ko.json").strings.Select(s => s.key));
            ProgressionConfig config = Config();
            var missing = new List<string>();

            for (int i = 0; i < config.Quests.Count; i++)
                if (!keys.Contains(config.Quests[i].TitleKey)) missing.Add(config.Quests[i].TitleKey);

            for (int level = 1; level <= config.MaxHqLevel; level++)
            {
                HqLevelDef def;
                config.TryGetHqLevel(level, out def);
                if (!string.IsNullOrEmpty(def.StoryKey) && !keys.Contains(def.StoryKey)) missing.Add(def.StoryKey);
            }

            foreach (SpeedUpItemDef s in config.SpeedUps)
                if (!keys.Contains(s.DisplayNameKey)) missing.Add(s.DisplayNameKey);

            BattleConfig battle = Battle();
            foreach (UnitDef u in battle.Units)
                if (!keys.Contains(u.DisplayNameKey)) missing.Add(u.DisplayNameKey);
            for (int i = 1; i <= battle.StageCount; i++)
            {
                StageDef st;
                battle.TryGetStage(i, out st);
                if (!keys.Contains(st.NameKey)) missing.Add(st.NameKey);
            }

            foreach (BuildingDef d in Catalog().All)
                if (!keys.Contains(d.DisplayNameKey)) missing.Add(d.DisplayNameKey);

            foreach (ThreatDef t in Chapter().Threats)
                foreach (string k in new[] { t.StartStoryKey, t.WinStoryKey, t.LoseStoryKey })
                    if (!string.IsNullOrEmpty(k) && !keys.Contains(k)) missing.Add(k);

            Assert.That(missing, Is.Empty, "문자열 표에 없는 키: " + string.Join(", ", missing));
        }

        [Test]
        public void 성벽은_그_레벨까지_열리는_건물을_모두_안에_두고_레벨마다_넓어진다()
        {
            WallConfig walls = Load<WallFile>("walls.json").ToConfig();
            Assert.That(walls.Validate(), Is.Empty, string.Join(" / ", walls.Validate()));

            BuildingCatalog catalog = Catalog();
            LayoutFile layoutFile = Load<LayoutFile>("base-layout.json");
            var grid = new SquareGrid(1f);
            var slots = layoutFile.slots.Select(s => (s.defId, new GridPos(s.x, s.y))).ToList();

            var problems = new List<string>();
            var log = new List<string>();
            float lastArea = 0f;
            foreach (WallTier tier in walls.Tiers)
            {
                // TerritoryWallView와 같은 함수·같은 전선 방향.
                List<GroundPatch> village = WallRing.VillagePatches(slots, catalog, grid, tier.HqLevel);
                WallPlan plan = WallRing.Plan(village, walls, tier.GateLength, new WorldXZ(1f, 1f));

                float room = WallRing.MinRoom(plan, village);
                if (room < walls.Clearance)
                    problems.Add("본부 Lv." + tier.HqLevel + " 성벽이 건물에 " + room.ToString("0.0") + "m만 남김");

                float area = Area(plan.Outline);
                if (area <= lastArea)
                    problems.Add("본부 Lv." + tier.HqLevel + " 성벽이 앞 단계보다 넓지 않음");
                lastArea = area;

                log.Add("Lv" + tier.HqLevel + ": 건물 " + village.Count + " · 기둥 사이 " + (plan.Outline.Count - 1) + "칸 · 넓이 " +
                        area.ToString("0") + "m² · 여유 " + room.ToString("0.0") + "m · margin " + plan.MarginUsed);
            }

            TestContext.WriteLine(string.Join(" / ", log));
            Assert.That(problems, Is.Empty, string.Join(" / ", problems));
        }

        private static float Area(List<WorldXZ> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].X * poly[i].Z - poly[i].X * poly[j].Z;
            return Math.Abs(a) * 0.5f;
        }

        [Test]
        public void 봇이_본부_Lv5까지_가고_임무를_전부_끝낸다()
        {
            BuildingCatalog catalog = Catalog();
            ProgressionConfig config = Config();
            LayoutFile layoutFile = Load<LayoutFile>("base-layout.json");

            GridRect bounds = GridRect.Centered(layoutFile.baseSize);
            var layout = new BaseLayout(bounds);
            foreach (SlotRecord s in layoutFile.slots)
                layout.Add(new BaseSlot(s.slotId, s.defId, new GridPos(s.x, s.y), s.zoneId));

            var clock = new ManualClock(1_000_000_000L);
            var runtime = new TerritoryRuntime(new GameState(new SquareGrid(1f), bounds), catalog, clock, 4f, config, Battle(), Chapter());
            runtime.Initialize(layout);

            // 봇은 도적 싸움을 화면으로 붙이지 않는다 — Core가 도착 뒤 저절로 결판낸 것을 센다(앱을 켜 둔 사람과 결과는 같다).
            long start = clock.NowUnixMs;
            var frontLog = new List<string>();
            runtime.Frontline.OutpostDestroyed += o => frontLog.Add(o.OutpostId + "@Lv" + runtime.HqLevel + " " +
                ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분");
            int raidsWon = 0, raidsLost = 0, raidTroopsLost = 0;
            runtime.Raids.AutoResolved += o => { if (o.Won) raidsWon++; else raidsLost++; raidTroopsLost += o.TroopsLost; };

            WallConfig walls = Load<WallFile>("walls.json").ToConfig();
            var territoryAt = new Dictionary<int, double>();
            var reachedAt = new Dictionary<int, double>();
            var stageLog = new List<string>();
            const long Step = 10_000L;     // 10초마다 한 번 화면을 보는 사람
            const long Limit = 4L * 60 * 60 * 1000;

            while (clock.NowUnixMs - start < Limit)
            {
                runtime.Tick();
                runtime.Production.CollectAll();

                while (runtime.Quests.TryClaim() == QuestClaimRejection.None) { }

                // 거인이 다가오는 동안은 훈련이 먼저다 — 남는 자원으로 짓는다. (사람도 목표가 보이면 그렇게 한다.)
                ThreatDef threat = runtime.Threats.Current;
                bool threatActive = threat != null && runtime.Threats.Describe(threat).Phase != ThreatPhase.Hidden;
                if (threatActive)
                    TrainMax(runtime);

                ActGreedily(runtime, threatActive);

                if (threat != null && runtime.Threats.CanDispatch(threat.ThreatId) == ThreatRejection.None)
                {
                    double hpBuff = runtime.Battles.BuffMultiplier(threat.TroopUnitId, BattleStat.Hp);
                    double attackBuff = runtime.Battles.BuffMultiplier(threat.TroopUnitId, BattleStat.Attack);
                    ThreatOutcome o = runtime.Threats.Resolve(threat.ThreatId);
                    stageLog.Add(threat.ThreatId + " " + (o.Won ? "승" : "패") + " " + o.Sent + "명(생존 " + o.Survivors + ") @Lv" +
                        runtime.HqLevel + " " + ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분 무장 체력×" +
                        hpBuff.ToString("0.00") + " 공격×" + attackBuff.ToString("0.00"));
                    continue;
                }

                if (threatActive && threat != null && !stageLog.Any(l => l.StartsWith("등장 " + threat.ThreatId)))
                    stageLog.Add("등장 " + threat.ThreatId + " " + ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분");

                int hq = runtime.HqLevel;
                if (!reachedAt.ContainsKey(hq))
                    reachedAt[hq] = (clock.NowUnixMs - start) / 60000.0;

                // 영토가 본부보다 뒤처지면 열린 건물이 성 밖에 선다. 게임의 Current()는 본부 레벨 아래로 안 내려가게 받쳐 주므로
                // 그 받침 없이 "무찌른 적"만으로 잰 단계가 본부를 따라오는지 본다.
                int bySignal = 0;
                foreach (WallTier t in walls.Tiers)
                {
                    if (!ProgressionRules.AllMet(t.Unlock, runtime.State, runtime.Progression))
                        break;
                    bySignal = t.HqLevel;
                }
                if (hq >= 1 && !territoryAt.ContainsKey(bySignal))
                    territoryAt[bySignal] = (clock.NowUnixMs - start) / 60000.0;
                Assert.That(bySignal, Is.GreaterThanOrEqualTo(Math.Min(hq, walls.Tiers[walls.Tiers.Count - 1].HqLevel)),
                    "본부 Lv." + hq + "인데 무찌른 적으로 연 영토는 " + bySignal);

                if (hq >= config.MaxHqLevel && !runtime.Quests.Current.HasQuest)
                    break;

                clock.Advance(Step);
            }

            string report = string.Join(", ", reachedAt.OrderBy(p => p.Key).Select(p => "Lv." + p.Key + " " + p.Value.ToString("0.0") + "분"));
            TestContext.WriteLine("봇 진행: " + report + " / 임무 " + runtime.State.QuestIndex + "/" + config.Quests.Count);
            TestContext.WriteLine("영토: " + string.Join(", ", territoryAt.OrderBy(p => p.Key).Select(p => p.Key + "단계 " + p.Value.ToString("0.0") + "분")));
            TestContext.WriteLine("거인: " + string.Join(", ", stageLog));
            TestContext.WriteLine("전선: " + string.Join(", ", frontLog) + " / 화력 " + runtime.Frontline.Power());
            TestContext.WriteLine("도적: 승 " + raidsWon + " / 패 " + raidsLost + ", 잃은 용사 " + raidTroopsLost);

            Assert.That(runtime.HqLevel, Is.EqualTo(config.MaxHqLevel), "본부 최고 레벨에 못 갔다 — " + report +
                " / 지금 임무: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : "없음"));
            Assert.That(runtime.Quests.Current.HasQuest, Is.False,
                "임무가 남았다: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : ""));

            // 봇 시간은 곡선을 고치면 조금씩 움직인다 — 정확한 값이 아니라 범위로 묶는다.
            // 범위를 벗어나면 곡선이 설계와 다른 게임이 됐다는 뜻이니 표를 다시 재서 고친다.
            var drift = new List<string>();
            CheckBotTime(drift, "02 7절", "봇 본부 Lv.", reachedAt, new Dictionary<int, double> { { 2, 4.7 }, { 3, 18.7 }, { 4, 27.5 }, { 5, 38.7 } });
            CheckBotTime(drift, "04 9절", "봇 영토 ", territoryAt, new Dictionary<int, double> { { 2, 4.7 }, { 3, 18.5 }, { 4, 27.5 }, { 5, 35.2 } });
            Assert.That(drift, Is.Empty, string.Join(Environment.NewLine, drift));
        }

        private const double BotTolerance = 0.15;

        private static void CheckBotTime(List<string> drift, string section, string label,
            Dictionary<int, double> actual, Dictionary<int, double> table)
        {
            foreach (KeyValuePair<int, double> row in table)
            {
                double got;
                if (!actual.TryGetValue(row.Key, out got))
                    drift.Add("설계서 " + section + " 부록 표: " + label + row.Key + " — 봇이 도달하지 못했다(표 " + row.Value + "분)");
                else if (Math.Abs(got - row.Value) > row.Value * BotTolerance)
                    drift.Add("설계서 " + section + " 부록 표: " + label + row.Key + " — 표 " + row.Value + "분, 봇 " +
                              got.ToString("0.0") + "분 (±" + (BotTolerance * 100) + "% 밖)");
            }
        }

        // ── 설계서 부록 상수표 ────────────────────────────────────────────────
        // 숫자의 원본은 docs/design/02~05 끝의 "부록 — 핵심 상수" 표다. 데이터만 고치고 표를 안 고치면
        // 여기서 실패한다(재편 때 그렇게 어긋난 곳이 네 군데 나왔다). 둘 중 어느 쪽이 맞는지는 사람이 정한다 —
        // 데이터가 맞으면 같은 커밋에서 표와 이 기대값을 함께 고친다.
        // 봇 시간은 위 봇 시험이, 300 vs 1 손맛은 거인 시험이 묶는다.

        private static void Same(List<string> diffs, string section, string what, object table, object data)
        {
            if (!Equals(table, data))
                diffs.Add("설계서 " + section + " 부록 표와 다르다 — " + what + ": 표 " + table + ", 데이터 " + data);
        }

        private static void AssertNoDiffs(List<string> diffs) =>
            Assert.That(diffs, Is.Empty, Environment.NewLine + string.Join(Environment.NewLine, diffs));

        private static long Requires(HqLevelDef def, RequirementKind kind, string target) =>
            def.Requirements.Where(r => r.Kind == kind && (target == null || r.Target == target)).Select(r => r.Amount).DefaultIfEmpty(0L).Max();

        private static long Amount(IEnumerable<ResourceCost> costs, ResourceKind kind) =>
            costs.Where(c => c.Kind == kind).Sum(c => c.Amount);

        private static long Items(IEnumerable<ItemGrant> items, string itemId) =>
            items.Where(i => i.ItemId == itemId).Sum(i => i.Amount);

        private static ThreatDef Threat(string threatId)
        {
            ThreatDef t;
            Assert.That(Chapter().TryGetThreat(threatId, out t), Is.True, threatId + "가 chapter1.json에 없다");
            return t;
        }

        private static UnitDef Unit(string unitId)
        {
            UnitDef u;
            Assert.That(Battle().TryGetUnit(unitId, out u), Is.True, unitId + "가 battle.json에 없다");
            return u;
        }

        private static string Enemies(ThreatDef t) =>
            string.Join(" + ", t.Enemies.Select(e => e.UnitId + "×" + e.Count + "(배수 " + e.Multiplier + ")"));

        [Test]
        public void 설계서_02_본부_부록표와_데이터가_같다()
        {
            ProgressionConfig config = Config();
            var d = new List<string>();

            Same(d, "02 1절", "시작 목재", 300L, Amount(config.StartingResources, ResourceKind.Wood));
            Same(d, "02 1절", "시작 석재", 200L, Amount(config.StartingResources, ResourceKind.Stone));

            // 레벨: 번영도, 적 기지(front.outpost), 이긴 거인, 공사 시간(ms), 대기열(0 = 무제한)
            var hq = new[]
            {
                new { Lv = 2, Prosperity = 3L,  Outposts = 1L, Giant = "",        Ms = 0L,      Queue = 0 },
                new { Lv = 3, Prosperity = 11L, Outposts = 2L, Giant = "giant_1", Ms = 5000L,   Queue = 0 },
                new { Lv = 4, Prosperity = 20L, Outposts = 0L, Giant = "giant_2", Ms = 120000L, Queue = 2 },
                new { Lv = 5, Prosperity = 30L, Outposts = 3L, Giant = "giant_3", Ms = 180000L, Queue = 2 },
            };
            foreach (var row in hq)
            {
                HqLevelDef def;
                Assert.That(config.TryGetHqLevel(row.Lv, out def), Is.True, "progression.json에 본부 Lv." + row.Lv + "가 없다");
                string at = "본부 Lv." + row.Lv + " ";
                Same(d, "02 3절", at + "조건 번영도", row.Prosperity, Requires(def, RequirementKind.Prosperity, null));
                Same(d, "02 3절", at + "조건 적 기지", row.Outposts, Requires(def, RequirementKind.Counter, FrontlineService.IndexKey));
                foreach (string g in new[] { "giant_1", "giant_2", "giant_3" })
                    Same(d, "02 3절", at + "조건 " + g, g == row.Giant ? 1L : 0L, Requires(def, RequirementKind.Counter, ChapterKeys.ThreatWon(g)));
                Same(d, "02 3절", at + "공사 시간(ms)", row.Ms, def.DurationMs);
                Same(d, "02 3절", at + "건설 대기열(0=무제한)", row.Queue, def.BuildQueueSlots);
            }

            HqLevelDef lv1;
            config.TryGetHqLevel(1, out lv1);
            Same(d, "02 3절", "본부 Lv.1 건설 대기열(0=무제한)", 0, lv1.BuildQueueSlots);

            HqLevelDef lv4, lv5;
            config.TryGetHqLevel(4, out lv4);
            config.TryGetHqLevel(5, out lv5);
            Same(d, "02 3절", "본부 Lv.4 선행 병영 레벨", 1L, Requires(lv4, RequirementKind.BuildingLevel, "barracks"));
            Same(d, "02 3절", "본부 Lv.5 선행 건설 작업장 레벨", 3L, Requires(lv5, RequirementKind.BuildingLevel, "mason_yard"));

            var steps = new[] { new { Lv = 2, X = 1.5, Ms = 10000L }, new { Lv = 3, X = 3.0, Ms = 30000L },
                                new { Lv = 4, X = 5.0, Ms = 60000L }, new { Lv = 5, X = 8.0, Ms = 120000L } };
            foreach (var s in steps)
            {
                UpgradeStep step;
                Assert.That(config.TryGetUpgradeStep(s.Lv, out step), Is.True, "upgradeSteps에 Lv." + s.Lv + "가 없다");
                Same(d, "02 4절", "업그레이드 Lv." + s.Lv + " 배수", s.X, step.CostMultiplier);
                Same(d, "02 4절", "업그레이드 Lv." + s.Lv + " 시간(ms)", s.Ms, step.DurationMs);
            }

            Same(d, "02 1절", "날개(가속) 줄이는 시간(ms)", "60000, 300000, 900000",
                string.Join(", ", config.SpeedUps.Select(s => s.ReduceMs).OrderBy(x => x)));

            foreach (BuildingDef b in Catalog().All.Where(b => b.Production != null))
                Same(d, "02 5절", b.DefId + " 생산 보관(분)", 60L, b.Production.CapMinutes);

            Same(d, "02 8절", "임무 수", 43, config.Quests.Count);

            AssertNoDiffs(d);
        }

        [Test]
        public void 설계서_03_300용사_부록표와_데이터가_같다()
        {
            ChapterConfig chapter = Chapter();
            var d = new List<string>();

            TrainingDef tr = chapter.Training;
            Same(d, "03 3절", "용사 1명 식량", 3L, Amount(tr.CostPerUnit, ResourceKind.Food));
            Same(d, "03 3절", "용사 1명 목재", 2L, Amount(tr.CostPerUnit, ResourceKind.Wood));
            Same(d, "03 3절", "용사 1명 시간(ms)", 3000L, tr.MsPerUnit);
            Same(d, "03 3절", "훈련 빨라짐 %/병영 레벨", 25.0, tr.SpeedPercentPerLevel);
            Same(d, "03 3절", "한 번에 맡길 수 기본", 30, tr.BatchBase);
            Same(d, "03 3절", "한 번에 맡길 수 병영 레벨당", 20, tr.BatchPerLevel);

            var giants = new[]
            {
                new { Id = "giant_1", Hq = 2, Min = 25L },
                new { Id = "giant_2", Hq = 3, Min = 30L },
                new { Id = "giant_3", Hq = 4, Min = 35L },
            };
            foreach (var g in giants)
            {
                ThreatDef t = Threat(g.Id);
                Same(d, "03 3절 · 04 4-3", g.Id + " 나타나는 본부 레벨", g.Hq, t.StartHqLevel);
                Same(d, "03 3절 · 04 4-3", g.Id + " 도착까지(ms)", g.Min * 60000L, t.ApproachMs);
                Same(d, "01 부록", g.Id + " 용사 목표", 300, t.GoalTroops);
            }
            Same(d, "03 3절", "giant_1 지면 다시 오기까지(ms)", 15L * 60000L, Threat("giant_1").RetryMs);

            UnitDef chief = Unit("giant_chieftain");
            Same(d, "03 3절", "족장 거인 체력", 9000L, chief.Hp);
            Same(d, "03 3절", "족장 거인 공격", 400L, chief.Attack);
            Same(d, "03 3절", "족장 거인 공격 간격(ms)", 1500L, chief.AttackIntervalMs);
            Same(d, "03 3절", "족장 거인 휩쓸기(m)", 0.3f, chief.SplashRadius);
            Same(d, "03 3절", "족장 거인 반지름", 1.6f, chief.Radius);

            ThreatDef g1 = Threat("giant_1");
            Same(d, "03 3절", "거인 1 보상 목재", 800L, Amount(g1.RewardResources, ResourceKind.Wood));
            Same(d, "03 3절", "거인 1 보상 석재", 600L, Amount(g1.RewardResources, ResourceKind.Stone));
            Same(d, "03 3절", "거인 1 보상 식량", 300L, Amount(g1.RewardResources, ResourceKind.Food));
            Same(d, "03 3절", "거인 1 보상 날개 5분", 3L, Items(g1.RewardItems, "hermes_wing_5m"));

            Same(d, "03 4-2", "적 기지 체력", "15000, 30000, 100000, 200000, 400000",
                string.Join(", ", chapter.Frontline.Outposts.Select(o => o.Hp)));
            Same(d, "03 4-2", "적 기지 버팀", "20, 50, 300, 400, 700",
                string.Join(", ", chapter.Frontline.Outposts.Select(o => o.Resist)));
            Same(d, "03 4-1", "전선 전리품 목재/피해 1000", 4L, Amount(chapter.Frontline.LootPerThousand, ResourceKind.Wood));
            Same(d, "03 4-1", "전선 전리품 석재/피해 1000", 3L, Amount(chapter.Frontline.LootPerThousand, ResourceKind.Stone));

            Same(d, "03 5절", "도적 습격 시작 본부 레벨(99 = 닫힘)", 99, chapter.Raids.StartHqLevel);

            AssertNoDiffs(d);
        }

        [Test]
        public void 설계서_04_영토_부록표와_데이터가_같다()
        {
            WallConfig walls = Load<WallFile>("walls.json").ToConfig();
            var d = new List<string>();

            // 영토 단계 신호 — 1 처음부터 · 2 도적 소굴(첫 적 기지) · 3~5 거인 1~3
            var signal = new Dictionary<int, string>
            {
                { 1, "" },
                { 2, "Counter " + FrontlineService.IndexKey + " 1" },
                { 3, "Counter " + ChapterKeys.ThreatWon("giant_1") + " 1" },
                { 4, "Counter " + ChapterKeys.ThreatWon("giant_2") + " 1" },
                { 5, "Counter " + ChapterKeys.ThreatWon("giant_3") + " 1" },
            };
            Same(d, "04 3절", "영토 단계 수", signal.Count, walls.Tiers.Count);
            foreach (WallTier tier in walls.Tiers)
            {
                string expected;
                if (signal.TryGetValue(tier.HqLevel, out expected))
                    Same(d, "04 3절 · 4-3", "영토 " + tier.HqLevel + " 신호", expected, string.Join(" · ", tier.Unlock.Select(r => r.ToString())));
            }

            Same(d, "04 4-3", "거인 2 적", "giant_chieftain×1(배수 1.3) + giant_brute×3(배수 1)", Enemies(Threat("giant_2")));
            Same(d, "04 4-3", "거인 3 적", "giant_chieftain×1(배수 1.7) + giant_slinger×6(배수 1)", Enemies(Threat("giant_3")));

            Same(d, "04 10-2", "성벽 여유(m)", 1.5f, walls.Clearance);
            Same(d, "04 10-2", "성벽 조각 길이(m)", 4f, walls.Unit);
            Same(d, "04 10-2", "기둥 사이 조각 수", 3, walls.PiecesPerSpan);

            AssertNoDiffs(d);
        }

        [Test]
        public void 설계서_05_전투_부록표와_데이터가_같다()
        {
            BattleConfig battle = Battle();
            var d = new List<string>();

            Same(d, "05 3절", "시뮬레이션 한 걸음(ms)", 50L, BattleSimulation.StepMs);
            Same(d, "05 3절", "스테이지 제한 시간(ms)", 90000L, battle.TimeLimitMs);
            Same(d, "05 3절", "거인 1 제한 시간(ms)", 150000L, Threat("giant_1").TimeLimitMs);
            Same(d, "05 3절", "거인 2 제한 시간(ms)", 180000L, Threat("giant_2").TimeLimitMs);
            Same(d, "05 3절", "거인 3 제한 시간(ms)", 180000L, Threat("giant_3").TimeLimitMs);

            ArmySource barracks = battle.Sources.FirstOrDefault(s => s.DefId == "barracks");
            Assert.That(barracks, Is.Not.Null, "battle.json army에 barracks가 없다");
            Same(d, "05 2절", "병영 첫 병력 병종", "hoplite", barracks.UnitId);
            Same(d, "05 2절", "병영 첫 병력", 4, barracks.FixedCount);
            Same(d, "05 2절", "병영 레벨마다 병력", 2, barracks.CountPerLevel);

            // 건물, 병종(빈칸 = 전부), 능력치, %/레벨
            var buffs = new[]
            {
                new { Def = "main_hall",     Unit = "",        Stat = BattleStat.Attack, Pct = 5.0 },
                new { Def = "ironworks",     Unit = "",        Stat = BattleStat.Attack, Pct = 8.0 },
                new { Def = "arena",         Unit = "hoplite", Stat = BattleStat.Attack, Pct = 5.0 },
                new { Def = "gatehouse",     Unit = "",        Stat = BattleStat.Hp,     Pct = 8.0 },
                new { Def = "healing_house", Unit = "",        Stat = BattleStat.Hp,     Pct = 6.0 },
            };
            Same(d, "05 2절", "강화 줄 수", buffs.Length, battle.Buffs.Count);
            foreach (var b in buffs)
            {
                ArmyBuff got = battle.Buffs.FirstOrDefault(x => x.DefId == b.Def && x.Stat == b.Stat);
                if (got == null)
                {
                    d.Add("설계서 05 2절 부록 표와 다르다 — " + b.Def + " " + b.Stat + " 강화가 데이터에 없다");
                    continue;
                }
                Same(d, "05 2절", b.Def + " " + b.Stat + " 병종", b.Unit, got.UnitId ?? "");
                Same(d, "05 2절", b.Def + " " + b.Stat + " %/레벨", b.Pct, got.PercentPerLevel);
            }

            UnitDef hoplite = Unit("hoplite");
            Same(d, "05 4절", "호플리테스 체력", 120L, hoplite.Hp);
            Same(d, "05 4절", "호플리테스 공격", 12L, hoplite.Attack);
            Same(d, "05 4절", "호플리테스 공격 간격(ms)", 1000L, hoplite.AttackIntervalMs);

            UnitDef chief = Unit("giant_chieftain");
            Same(d, "05 4절", "족장 거인 체력", 9000L, chief.Hp);
            Same(d, "05 4절", "족장 거인 공격", 400L, chief.Attack);
            Same(d, "05 4절", "족장 거인 공격 간격(ms)", 1500L, chief.AttackIntervalMs);
            Same(d, "05 4절", "족장 거인 휩쓸기(m)", 0.3f, chief.SplashRadius);

            AssertNoDiffs(d);
        }

        private static bool IsChapterKey(string defId) =>
            defId == "farm" || defId == "barracks" || defId == "carpentry" || defId == "timber_store";

        private static void TrainMax(TerritoryRuntime rt)
        {
            int n = rt.Training.MaxAffordable();
            if (n > 0)
                rt.Training.TryStart(n);
        }

        /// <summary>
        /// 할 수 있는 것을 전부 한다 — 본부 먼저, 그다음 싼 복구·업그레이드. 본부 공사가 길면 날개를 쓴다.
        /// 거인이 오는 중이면 목재를 훈련 몫(다음 한 번 맡길 양)만큼 남겨 둔다.
        /// </summary>
        private static void ActGreedily(TerritoryRuntime rt, bool saveForTraining)
        {
            bool acted = true;
            while (acted)
            {
                acted = false;

                BuildingInstance hq = rt.State.Buildings.First(b => b.DefId == rt.Progression.HqDefId);

                if (hq.IsRuined && rt.Construction.TryStartRepair(hq.Id).Started) { acted = true; continue; }
                if (rt.Upgrades.TryStart(hq.Id).Started) { acted = true; continue; }

                if (hq.Phase == BuildingPhase.Constructing)
                {
                    foreach (SpeedUpItemDef s in rt.Progression.SpeedUps)
                    {
                        if (hq.RemainingConstructionMs(rt.Clock.NowUnixMs) >= s.ReduceMs
                            && rt.SpeedUps.TryUse(hq.Id, s.ItemId) == SpeedUpRejection.None)
                        {
                            acted = true;
                            break;
                        }
                    }
                    if (acted) continue;
                }

                long reserveWood = 0L;
                if (saveForTraining && rt.Training.Def != null)
                {
                    TrainingView tv = rt.Training.Describe();
                    long woodPer = rt.Training.Def.CostPerUnit.Where(c => c.Kind == ResourceKind.Wood).Sum(c => c.Amount);
                    reserveWood = woodPer * tv.BatchCap;
                }

                // 생산·훈련 건물(목공소·농장·병영)은 아끼지 않고 올린다 — 300을 빨리 채우는 길이다.
                foreach (BuildingInstance b in rt.State.Buildings
                    .OrderBy(b => IsChapterKey(b.DefId) ? 0 : 1).ThenBy(b => b.Level).ThenBy(b => b.Id).ToList())
                {
                    if (b.Id == hq.Id)
                        continue;

                    if (saveForTraining && !IsChapterKey(b.DefId) && rt.State.Resources.Get(ResourceKind.Wood) < reserveWood * 2)
                        continue;

                    if ((b.IsRuined && rt.Construction.TryStartRepair(b.Id).Started)
                        || (!b.IsRuined && rt.Upgrades.TryStart(b.Id).Started))
                    {
                        acted = true;
                        break;
                    }
                }
            }
        }
    }
}

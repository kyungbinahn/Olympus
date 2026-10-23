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
            bool won300 = false, won150 = true;
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
            }

            TestContext.WriteLine(string.Join(Environment.NewLine, lines));
            Assert.That(won300, Is.True, "300명이 진다: " + string.Join(Environment.NewLine, lines));
            Assert.That(won150, Is.False, "150명으로도 이긴다: " + string.Join(Environment.NewLine, lines));
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
                    ThreatOutcome o = runtime.Threats.Resolve(threat.ThreatId);
                    stageLog.Add(threat.ThreatId + " " + (o.Won ? "승" : "패") + " " + o.Sent + "명(생존 " + o.Survivors + ") @Lv" +
                        runtime.HqLevel + " " + ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분");
                    continue;
                }

                if (threatActive && threat != null && !stageLog.Any(l => l.StartsWith("등장")))
                    stageLog.Add("등장 " + ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분");

                int hq = runtime.HqLevel;
                if (!reachedAt.ContainsKey(hq))
                    reachedAt[hq] = (clock.NowUnixMs - start) / 60000.0;

                if (hq >= config.MaxHqLevel && !runtime.Quests.Current.HasQuest)
                    break;

                clock.Advance(Step);
            }

            string report = string.Join(", ", reachedAt.OrderBy(p => p.Key).Select(p => "Lv." + p.Key + " " + p.Value.ToString("0.0") + "분"));
            TestContext.WriteLine("봇 진행: " + report + " / 임무 " + runtime.State.QuestIndex + "/" + config.Quests.Count);
            TestContext.WriteLine("거인: " + string.Join(", ", stageLog));
            TestContext.WriteLine("전선: " + string.Join(", ", frontLog) + " / 화력 " + runtime.Frontline.Power());
            TestContext.WriteLine("도적: 승 " + raidsWon + " / 패 " + raidsLost + ", 잃은 용사 " + raidTroopsLost);

            Assert.That(runtime.HqLevel, Is.EqualTo(config.MaxHqLevel), "본부 최고 레벨에 못 갔다 — " + report +
                " / 지금 임무: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : "없음"));
            Assert.That(runtime.Quests.Current.HasQuest, Is.False,
                "임무가 남았다: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : ""));
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

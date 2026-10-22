using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Olympus.Core.Battle;
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
            var runtime = new TerritoryRuntime(new GameState(new SquareGrid(1f), bounds), catalog, clock, 4f, config, Battle());
            runtime.Initialize(layout);

            long start = clock.NowUnixMs;
            var reachedAt = new Dictionary<int, double>();
            var stageLog = new List<string>();
            const long Step = 10_000L;     // 10초마다 한 번 화면을 보는 사람
            const long Limit = 4L * 60 * 60 * 1000;

            while (clock.NowUnixMs - start < Limit)
            {
                runtime.Tick();
                runtime.Production.CollectAll();

                while (runtime.Quests.TryClaim() == QuestClaimRejection.None) { }

                ActGreedily(runtime);

                // 다음 스테이지를 이길 수 있으면 싸운다. 지는 판은 바뀌는 게 없으니 매번 시도해도 된다.
                StageDef next = runtime.Battles.NextStage;
                if (next != null && runtime.Battles.Fight(next.StageId).Won)
                {
                    stageLog.Add("S" + next.StageId + "@Lv" + runtime.HqLevel + " " +
                        ((clock.NowUnixMs - start) / 60000.0).ToString("0.0") + "분");
                    continue;   // 보상으로 또 할 수 있는 게 생겼다 — 시계를 돌리기 전에 다시 본다.
                }

                int hq = runtime.HqLevel;
                if (!reachedAt.ContainsKey(hq))
                    reachedAt[hq] = (clock.NowUnixMs - start) / 60000.0;

                if (hq >= config.MaxHqLevel && !runtime.Quests.Current.HasQuest)
                    break;

                clock.Advance(Step);
            }

            string report = string.Join(", ", reachedAt.OrderBy(p => p.Key).Select(p => "Lv." + p.Key + " " + p.Value.ToString("0.0") + "분"));
            TestContext.WriteLine("봇 진행: " + report + " / 임무 " + runtime.State.QuestIndex + "/" + config.Quests.Count);
            TestContext.WriteLine("스테이지: " + string.Join(", ", stageLog));

            Assert.That(runtime.HqLevel, Is.EqualTo(config.MaxHqLevel), "본부 최고 레벨에 못 갔다 — " + report +
                " / 지금 임무: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : "없음"));
            Assert.That(runtime.Quests.Current.HasQuest, Is.False,
                "임무가 남았다: " + (runtime.Quests.Current.HasQuest ? runtime.Quests.Current.Quest.QuestId : ""));
        }

        /// <summary>할 수 있는 것을 전부 한다 — 본부 먼저, 그다음 싼 복구·업그레이드. 본부 공사가 길면 날개를 쓴다.</summary>
        private static void ActGreedily(TerritoryRuntime rt)
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

                foreach (BuildingInstance b in rt.State.Buildings.OrderBy(b => b.Level).ThenBy(b => b.Id).ToList())
                {
                    if (b.Id == hq.Id)
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

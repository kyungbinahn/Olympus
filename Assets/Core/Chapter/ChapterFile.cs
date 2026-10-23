using System;
using System.Collections.Generic;
using Olympus.Core.Battle;
using Olympus.Core.Progression;
using Olympus.Core.Territory;

namespace Olympus.Core.Chapter
{
    /// <summary><c>Assets/Data/chapter1.json</c>의 모양. JsonUtility(Game)와 System.Text.Json(시험)이 같이 읽는다.</summary>
    [Serializable]
    public sealed class ChapterFile
    {
        public TrainingRecord training;
        public List<ThreatRecord> threats = new List<ThreatRecord>();
        public RaidRecord raids;

        public ChapterConfig ToConfig()
        {
            TrainingDef training = null;
            if (this.training != null && !string.IsNullOrEmpty(this.training.unitId))
            {
                TrainingRecord t = this.training;
                training = new TrainingDef(t.unitId, t.barracksDefId, DataParse.Costs(t.costPerUnit, "훈련"),
                    t.msPerUnit, t.batchBase, t.batchPerLevel, t.speedPercentPerLevel);
            }

            var config = new ChapterConfig(training);

            if (threats != null)
            {
                for (int i = 0; i < threats.Count; i++)
                {
                    ThreatRecord r = threats[i];
                    var enemies = new List<EnemyGroup>();
                    if (r.enemies != null)
                    {
                        for (int k = 0; k < r.enemies.Count; k++)
                            enemies.Add(new EnemyGroup(r.enemies[k].unitId, r.enemies[k].count, r.enemies[k].multiplier));
                    }

                    var items = new List<ItemGrant>();
                    if (r.rewardItems != null)
                    {
                        for (int k = 0; k < r.rewardItems.Count; k++)
                            items.Add(new ItemGrant(r.rewardItems[k].itemId, r.rewardItems[k].amount));
                    }

                    config.AddThreat(new ThreatDef(r.threatId, r.startHqLevel, r.approachMs, r.retryMs, r.goalTroops,
                        r.troopUnitId, enemies, r.enemyScale, r.rowWidth, r.timeLimitMs,
                        DataParse.Costs(r.rewardResources, r.threatId), items,
                        r.startStoryKey, r.winStoryKey, r.loseStoryKey, r.spacing, r.startDistance));
                }
            }

            if (raids != null && raids.waves != null && raids.waves.Count > 0)
            {
                var waves = new List<RaidWave>();
                for (int i = 0; i < raids.waves.Count; i++)
                {
                    RaidWaveRecord w = raids.waves[i];
                    var enemies = new List<EnemyGroup>();
                    if (w.enemies != null)
                    {
                        for (int k = 0; k < w.enemies.Count; k++)
                            enemies.Add(new EnemyGroup(w.enemies[k].unitId, w.enemies[k].count, w.enemies[k].multiplier));
                    }

                    waves.Add(new RaidWave(w.hqLevel, enemies, DataParse.Costs(w.rewardResources, "도적단 Lv." + w.hqLevel)));
                }

                config.SetRaids(new RaidDef(raids.startHqLevel, raids.firstDelayMs, raids.intervalMs, raids.approachMs,
                    raids.militiaUnitId, raids.militiaCount, raids.militiaMultiplier, raids.defenderCap, raids.lossPercent,
                    raids.autoResolveMs, raids.rowWidth, raids.timeLimitMs, waves));
            }

            return config;
        }
    }

    [Serializable]
    public sealed class RaidRecord
    {
        public int startHqLevel = 1;
        public long firstDelayMs = 45000;
        public long intervalMs = 150000;
        public long approachMs = 20000;
        public string militiaUnitId;
        public int militiaCount;
        public double militiaMultiplier = 1.0;
        public int defenderCap;
        public int lossPercent = 10;
        public long autoResolveMs = 120000;
        public int rowWidth;
        public long timeLimitMs = 60000;
        public List<RaidWaveRecord> waves = new List<RaidWaveRecord>();
    }

    [Serializable]
    public sealed class RaidWaveRecord
    {
        public int hqLevel;
        public List<EnemyGroupRecord> enemies = new List<EnemyGroupRecord>();
        public List<ResourceCostRecord> rewardResources = new List<ResourceCostRecord>();
    }

    [Serializable]
    public sealed class TrainingRecord
    {
        public string unitId;
        public string barracksDefId;
        public List<ResourceCostRecord> costPerUnit = new List<ResourceCostRecord>();
        public long msPerUnit = 1000;
        public int batchBase = 10;
        public int batchPerLevel;
        public double speedPercentPerLevel;
    }

    [Serializable]
    public sealed class ThreatRecord
    {
        public string threatId;
        public int startHqLevel;
        public long approachMs;
        public long retryMs;
        public int goalTroops;
        public string troopUnitId;
        public List<EnemyGroupRecord> enemies = new List<EnemyGroupRecord>();
        public float enemyScale;
        public int rowWidth;
        public float spacing;
        public float startDistance;
        public long timeLimitMs = 120000;
        public List<ResourceCostRecord> rewardResources = new List<ResourceCostRecord>();
        public List<ItemRecord> rewardItems = new List<ItemRecord>();
        public string startStoryKey;
        public string winStoryKey;
        public string loseStoryKey;
    }
}

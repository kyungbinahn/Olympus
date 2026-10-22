using System;
using System.Collections.Generic;
using Olympus.Core.Progression;
using Olympus.Core.Territory;

namespace Olympus.Core.Battle
{
    /// <summary>
    /// <c>battle.json</c>의 모양. Core에 두는 이유는 <see cref="BuildingDefFile"/>과 같다 —
    /// 글자를 읽는 한 줄만 Unity에 두고, 변환과 검증은 시험이 걸리는 곳에 둔다.
    /// </summary>
    [Serializable]
    public sealed class BattleFile
    {
        public long timeLimitMs = 90000;
        public List<UnitRecord> units = new List<UnitRecord>();
        public List<ArmySourceRecord> army = new List<ArmySourceRecord>();
        public List<ArmyBuffRecord> buffs = new List<ArmyBuffRecord>();
        public List<StageRecord> stages = new List<StageRecord>();

        public BattleConfig ToConfig()
        {
            var config = new BattleConfig(timeLimitMs);

            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    UnitRecord u = units[i];
                    config.AddUnit(new UnitDef(u.unitId, u.displayNameKey, u.hp, u.attack, u.attackIntervalMs,
                        u.range, u.moveSpeed, u.radius));
                }
            }

            if (army != null)
            {
                for (int i = 0; i < army.Count; i++)
                {
                    ArmySourceRecord a = army[i];
                    config.AddSource(new ArmySource(a.defId, a.unitId, a.fixedCount, a.countPerLevel));
                }
            }

            if (buffs != null)
            {
                for (int i = 0; i < buffs.Count; i++)
                {
                    ArmyBuffRecord b = buffs[i];

                    BattleStat stat;
                    if (!Enum.TryParse(b.stat, true, out stat) || !Enum.IsDefined(typeof(BattleStat), stat))
                        throw new InvalidOperationException(
                            "강화 " + b.defId + ": 모르는 능력치 '" + b.stat + "'. 쓸 수 있는 값: " +
                            string.Join(", ", Enum.GetNames(typeof(BattleStat))));

                    config.AddBuff(new ArmyBuff(b.defId, b.unitId, stat, b.percentPerLevel));
                }
            }

            if (stages != null)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    StageRecord s = stages[i];
                    string where = "스테이지 " + s.stageId;

                    var enemies = new List<EnemyGroup>();
                    if (s.enemies != null)
                    {
                        for (int k = 0; k < s.enemies.Count; k++)
                            enemies.Add(new EnemyGroup(s.enemies[k].unitId, s.enemies[k].count, s.enemies[k].multiplier));
                    }

                    var items = new List<ItemGrant>();
                    if (s.rewardItems != null)
                    {
                        for (int k = 0; k < s.rewardItems.Count; k++)
                            items.Add(new ItemGrant(s.rewardItems[k].itemId, s.rewardItems[k].amount));
                    }

                    config.AddStage(new StageDef(s.stageId, s.nameKey, enemies,
                        DataParse.Costs(s.rewardResources, where), items));
                }
            }

            return config;
        }
    }

    [Serializable]
    public sealed class UnitRecord
    {
        public string unitId;
        public string displayNameKey;
        public long hp;
        public long attack;
        public long attackIntervalMs = 1000;
        public float range = 1f;
        public float moveSpeed = 2f;
        public float radius = 0.4f;
    }

    [Serializable]
    public sealed class ArmySourceRecord
    {
        /// <summary>비우면 건물과 무관한 기본 수비대.</summary>
        public string defId;
        public string unitId;
        public int fixedCount;
        public int countPerLevel;
    }

    [Serializable]
    public sealed class ArmyBuffRecord
    {
        public string defId;

        /// <summary>비우면 아군 전원.</summary>
        public string unitId;

        /// <summary><see cref="BattleStat"/> 이름 — Attack / Hp.</summary>
        public string stat;
        public double percentPerLevel;
    }

    [Serializable]
    public sealed class StageRecord
    {
        public int stageId;
        public string nameKey;
        public List<EnemyGroupRecord> enemies = new List<EnemyGroupRecord>();
        public List<ResourceCostRecord> rewardResources = new List<ResourceCostRecord>();
        public List<ItemRecord> rewardItems = new List<ItemRecord>();
    }

    [Serializable]
    public sealed class EnemyGroupRecord
    {
        public string unitId;
        public int count = 1;
        public double multiplier = 1.0;
    }
}

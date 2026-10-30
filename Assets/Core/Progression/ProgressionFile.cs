using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// <c>progression.json</c>의 모양. Core에 두는 이유는 <see cref="BuildingDefFile"/>과 같다 —
    /// 글자를 읽는 한 줄만 Unity(JsonUtility)에 두고, 규칙 객체로 바꾸는 판단은 시험이 걸리는 곳에 둔다.
    /// </summary>
    [Serializable]
    public sealed class ProgressionFile
    {
        public string hqDefId;
        public List<ResourceCostRecord> startingResources = new List<ResourceCostRecord>();
        public List<ItemRecord> startingItems = new List<ItemRecord>();
        public List<HqLevelRecord> hqLevels = new List<HqLevelRecord>();
        public List<UpgradeStepRecord> upgradeSteps = new List<UpgradeStepRecord>();
        public List<SpeedUpRecord> speedUps = new List<SpeedUpRecord>();
    }

    [Serializable]
    public sealed class HqLevelRecord
    {
        public int level;
        public List<RequirementRecord> requirements = new List<RequirementRecord>();
        public List<ResourceCostRecord> costs = new List<ResourceCostRecord>();
        public long durationMs;
        public int buildQueueSlots;
        public List<ResourceCostRecord> rewardResources = new List<ResourceCostRecord>();
        public List<ItemRecord> rewardItems = new List<ItemRecord>();
        public string storyKey;
    }

    [Serializable]
    public sealed class RequirementRecord
    {
        /// <summary><see cref="RequirementKind"/> 이름.</summary>
        public string kind;
        public string target;
        public long amount;
    }

    [Serializable]
    public sealed class ItemRecord
    {
        public string itemId;
        public long amount;
    }

    [Serializable]
    public sealed class UpgradeStepRecord
    {
        public int level;
        public double costMultiplier = 1.0;
        public long durationMs;
    }

    [Serializable]
    public sealed class SpeedUpRecord
    {
        public string itemId;
        public string displayNameKey;
        public long reduceMs;
    }

    /// <summary><c>quests.json</c>의 모양.</summary>
    [Serializable]
    public sealed class QuestFile
    {
        public List<QuestRecord> quests = new List<QuestRecord>();
    }

    [Serializable]
    public sealed class QuestRecord
    {
        public string questId;
        public string titleKey;
        public RequirementRecord objective;
        public List<ResourceCostRecord> rewardResources = new List<ResourceCostRecord>();
        public List<ItemRecord> rewardItems = new List<ItemRecord>();
        public string focusDefId;
    }

    /// <summary><c>function-unlock.json</c>의 모양.</summary>
    [Serializable]
    public sealed class FeatureUnlockFile
    {
        public List<FeatureUnlockRecord> features = new List<FeatureUnlockRecord>();
    }

    [Serializable]
    public sealed class FeatureUnlockRecord
    {
        public string featureId;
        public int hqLevel;
    }

    /// <summary>세 파일을 합쳐 <see cref="ProgressionConfig"/> 한 벌을 만든다.</summary>
    public static class ProgressionFileMapper
    {
        public static ProgressionConfig ToConfig(ProgressionFile progression, QuestFile quests, FeatureUnlockFile features)
        {
            if (progression == null)
                throw new ArgumentNullException(nameof(progression), "progression.json을 읽지 못했다.");

            var chain = new QuestChain();
            if (quests != null && quests.quests != null)
            {
                for (int i = 0; i < quests.quests.Count; i++)
                {
                    QuestRecord q = quests.quests[i];
                    string where = "임무 " + q.questId;

                    chain.Add(new QuestDef(
                        q.questId,
                        q.titleKey,
                        ToRequirement(q.objective, where),
                        DataParse.Costs(q.rewardResources, where),
                        ToItems(q.rewardItems),
                        q.focusDefId));
                }
            }

            var table = new FeatureUnlockTable();
            if (features != null && features.features != null)
            {
                for (int i = 0; i < features.features.Count; i++)
                {
                    table.Add(features.features[i].featureId, features.features[i].hqLevel);
                }
            }

            var config = new ProgressionConfig(
                progression.hqDefId,
                DataParse.Costs(progression.startingResources, "시작 자원"),
                ToItems(progression.startingItems),
                chain,
                table);

            if (progression.hqLevels != null)
            {
                for (int i = 0; i < progression.hqLevels.Count; i++)
                {
                    HqLevelRecord r = progression.hqLevels[i];
                    string where = "본부 Lv." + r.level;

                    var requirements = new List<Requirement>();
                    if (r.requirements != null)
                    {
                        for (int k = 0; k < r.requirements.Count; k++)
                        {
                            requirements.Add(ToRequirement(r.requirements[k], where));
                        }
                    }

                    config.AddHqLevel(new HqLevelDef(
                        r.level,
                        requirements,
                        DataParse.Costs(r.costs, where),
                        r.durationMs,
                        r.buildQueueSlots,
                        DataParse.Costs(r.rewardResources, where + " 보상"),
                        ToItems(r.rewardItems),
                        r.storyKey));
                }
            }

            if (progression.upgradeSteps != null)
            {
                for (int i = 0; i < progression.upgradeSteps.Count; i++)
                {
                    UpgradeStepRecord s = progression.upgradeSteps[i];
                    config.AddUpgradeStep(new UpgradeStep(s.level, s.costMultiplier, s.durationMs));
                }
            }

            if (progression.speedUps != null)
            {
                for (int i = 0; i < progression.speedUps.Count; i++)
                {
                    SpeedUpRecord s = progression.speedUps[i];
                    config.AddSpeedUp(new SpeedUpItemDef(s.itemId, s.displayNameKey, s.reduceMs));
                }
            }

            return config;
        }

        internal static Requirement ToRequirement(RequirementRecord r, string where)
        {
            if (r == null)
                throw new InvalidOperationException(where + ": 조건이 비어 있다.");

            RequirementKind kind;
            if (!Enum.TryParse(r.kind, true, out kind) || !Enum.IsDefined(typeof(RequirementKind), kind))
            {
                throw new InvalidOperationException(
                    where + ": 모르는 조건 종류 '" + r.kind + "'. 쓸 수 있는 값: " +
                    string.Join(", ", Enum.GetNames(typeof(RequirementKind))));
            }

            // 번영도는 대상이 없다 — JSON에 빈 글자로 적혀 있어도 null로 맞춘다.
            string target = kind == RequirementKind.Prosperity ? null : r.target;
            return new Requirement(kind, target, r.amount);
        }

        private static List<ItemGrant> ToItems(List<ItemRecord> records)
        {
            var items = new List<ItemGrant>();

            if (records == null)
                return items;

            for (int i = 0; i < records.Count; i++)
            {
                items.Add(new ItemGrant(records[i].itemId, records[i].amount));
            }

            return items;
        }
    }
}

using System;
using System.Collections.Generic;
using Olympus.Core.State;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// 임무 하나. 목표는 <see cref="Requirement"/> 하나 — 본부 레벨업 조건과 같은 모양이다.
    /// </summary>
    public sealed class QuestDef
    {
        public string QuestId { get; }

        /// <summary>임무 바에 뜨는 문구(로컬라이즈 키).</summary>
        public string TitleKey { get; }

        public Requirement Objective { get; }

        public IReadOnlyList<ResourceCost> RewardResources { get; }
        public IReadOnlyList<ItemGrant> RewardItems { get; }

        /// <summary>
        /// 임무 바를 눌렀을 때 카메라가 날아갈 건물 종류. 비우면 날아가지 않는다.
        /// 탑포스는 퀘스트를 누르면 해당 시설로 카메라가 이동하고 화살표가 뜬다 —
        /// "무엇을 하라"만 말하고 "어디서"를 안 알려 주면 초반에 길을 잃는다.
        /// </summary>
        public string FocusDefId { get; }

        public QuestDef(
            string questId,
            string titleKey,
            Requirement objective,
            IReadOnlyList<ResourceCost> rewardResources,
            IReadOnlyList<ItemGrant> rewardItems,
            string focusDefId)
        {
            if (string.IsNullOrEmpty(questId))
                throw new ArgumentException("임무 이름은 비울 수 없다.", nameof(questId));

            QuestId = questId;
            TitleKey = titleKey;
            Objective = objective ?? throw new ArgumentNullException(nameof(objective));
            RewardResources = rewardResources ?? Array.Empty<ResourceCost>();
            RewardItems = rewardItems ?? Array.Empty<ItemGrant>();
            FocusDefId = focusDefId;
        }
    }

    /// <summary>
    /// 한 줄로 이어지는 임무 목록. 탑포스 하단 퀘스트 바처럼 <b>딱 하나</b>만 보이고,
    /// 받으면 바로 다음이 뜬다.
    ///
    /// 목록이 아니라 한 줄인 이유 — 초반에 선택지를 여러 개 주면 무엇이 중요한지 플레이어가
    /// 판단해야 한다. 한 줄이면 "다음 할 일"이 항상 정해져 있고, 체인 순서로 건물 복구와
    /// 업그레이드와 본부 레벨업을 번갈아 시킬 수 있다(한쪽만 앞서 나가면 다른 쪽이 막힌다).
    /// </summary>
    public sealed class QuestChain
    {
        private readonly List<QuestDef> _quests = new List<QuestDef>();
        private readonly HashSet<string> _ids = new HashSet<string>();

        public void Add(QuestDef quest)
        {
            if (quest == null)
                throw new ArgumentNullException(nameof(quest));
            if (!_ids.Add(quest.QuestId))
                throw new InvalidOperationException("임무 이름이 중복이다: " + quest.QuestId);

            _quests.Add(quest);
        }

        public int Count => _quests.Count;

        public QuestDef this[int index] => _quests[index];
    }

    public enum QuestClaimRejection
    {
        None = 0,

        /// <summary>체인을 다 끝냈다.</summary>
        NoQuest,

        /// <summary>목표를 아직 못 채웠다.</summary>
        NotComplete,
    }

    /// <summary>지금 임무와 그 진행. 임무 바가 그대로 띄운다.</summary>
    public readonly struct QuestView
    {
        /// <summary>null이면 체인을 다 끝냈다.</summary>
        public readonly QuestDef Quest;

        public readonly RequirementStatus Progress;

        /// <summary>몇 번째 임무인가(1부터) / 전체 몇 개.</summary>
        public readonly int Number;
        public readonly int Total;

        public QuestView(QuestDef quest, RequirementStatus progress, int number, int total)
        {
            Quest = quest;
            Progress = progress;
            Number = number;
            Total = total;
        }

        public bool HasQuest => Quest != null;

        public bool CanClaim => Quest != null && Progress.IsMet;
    }

    /// <summary>
    /// 임무 진행. 목표 달성 여부는 상태에서 매번 계산하고 저장하지 않는다 —
    /// 저장하는 것은 "몇 번째 임무까지 보상을 받았나"(<see cref="GameState.QuestIndex"/>) 하나다.
    ///
    /// 보상은 자동으로 주지 않고 플레이어가 누를 때 준다. 탑포스도 그렇게 한다 —
    /// 누르는 순간이 보상 연출의 자리이고, 받는 행동이 다음 임무로 넘어가는 박자를 만든다.
    /// </summary>
    public sealed class QuestService
    {
        private readonly StateStore _store;
        private readonly ProgressionConfig _config;

        public QuestService(StateStore store, ProgressionConfig config)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _config = config ?? ProgressionConfig.Empty;
        }

        public QuestView Current
        {
            get
            {
                QuestChain chain = _config.Quests;
                int index = _store.State.QuestIndex;

                if (index >= chain.Count)
                    return new QuestView(null, default, chain.Count, chain.Count);

                QuestDef quest = chain[index];
                RequirementStatus progress = ProgressionRules.Evaluate(quest.Objective, _store.State, _config);
                return new QuestView(quest, progress, index + 1, chain.Count);
            }
        }

        public QuestClaimRejection TryClaim()
        {
            QuestView view = Current;

            if (!view.HasQuest)
                return QuestClaimRejection.NoQuest;

            if (!view.Progress.IsMet)
                return QuestClaimRejection.NotComplete;

            var delta = new StateDelta().WithResources(view.Quest.RewardResources);

            for (int i = 0; i < view.Quest.RewardItems.Count; i++)
            {
                ItemGrant g = view.Quest.RewardItems[i];
                delta.WithItem(CountChange.Delta(g.ItemId, g.Amount));
            }

            delta.QuestIndex = _store.State.QuestIndex + 1;

            _store.Apply(delta);
            return QuestClaimRejection.None;
        }
    }
}

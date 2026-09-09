using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Territory
{
    public enum BuildRejection
    {
        None = 0,

        /// <summary>그런 자리 번호가 없다.</summary>
        UnknownSlot,

        /// <summary>자리가 가리키는 DefId가 카탈로그에 없다 — 데이터 실수.</summary>
        UnknownDef,

        /// <summary>폐허가 아니다. 이미 공사 중이거나 이미 복구됐다.</summary>
        NotRuined,

        InsufficientResources,
    }

    public readonly struct BuildResult
    {
        public readonly bool Started;
        public readonly BuildRejection Rejection;

        private BuildResult(bool started, BuildRejection rejection)
        {
            Started = started;
            Rejection = rejection;
        }

        public static BuildResult Ok => new BuildResult(true, BuildRejection.None);

        public static BuildResult Fail(BuildRejection rejection) =>
            new BuildResult(false, rejection);
    }

    /// <summary>
    /// 폐허 복구와 공사 완료를 처리한다. 델타를 만들어 스토어에 넘기는 쪽이다.
    ///
    /// 자리는 플레이어가 고르지 않는다 — <see cref="BaseLayout"/>이 정한 자리에 폐허가
    /// 서 있고, 플레이어는 그 자리를 탭해서 복구한다. 그래서 배치 좌표를 인자로 받지 않고
    /// 자리 번호만 받는다. 뉴포리아가 자유 배치 파이프라인에서 겪은 함정들
    /// ("드래그가 안 잡히는데 로그가 없다", "배치 경계 캐시가 세션 단위로 굳는다")이
    /// 이 설계에서는 존재할 자리가 없다.
    ///
    /// 여기에 UnityEngine이 한 줄도 없다 — 그래서 Unity를 열지 않고 dotnet test로
    /// 복구 조건·비용 차감·타이머 완료를 전부 검증할 수 있다.
    /// </summary>
    public sealed class ConstructionService
    {
        private readonly StateStore _store;
        private readonly BuildingCatalog _catalog;
        private readonly IClock _clock;

        public ConstructionService(StateStore store, BuildingCatalog catalog, IClock clock)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// 레이아웃의 모든 자리를 폐허 상태로 세운다. 기지 진입 시 한 번 부른다.
        ///
        /// 레이아웃을 먼저 검증한다 — 자리가 겹치거나 범위를 벗어난 것은 데이터 실수인데,
        /// 검증 없이 넘기면 스토어가 "놓을 수 없는 자리에 Add가 왔다"로 터지고 어느
        /// 자리가 문제인지 알기 어렵다. 여기서 잡으면 자리 번호와 상대를 이름 대고 말해준다.
        /// </summary>
        public void InitializeFromLayout(BaseLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            IReadOnlyList<LayoutProblem> problems = layout.Validate(_catalog);

            if (problems.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("기지 레이아웃이 유효하지 않다 (").Append(problems.Count).Append("건):");

                for (int i = 0; i < problems.Count; i++)
                {
                    sb.Append("\n  - ").Append(problems[i]);
                }

                throw new InvalidOperationException(sb.ToString());
            }

            var delta = new StateDelta();

            for (int i = 0; i < layout.Slots.Count; i++)
            {
                BaseSlot slot = layout.Slots[i];

                BuildingDef def;
                _catalog.TryGet(slot.DefId, out def);

                delta.WithBuilding(BuildingChange.Add(
                    slot.SlotId,
                    slot.DefId,
                    slot.Anchor,
                    def.Footprint,
                    BuildingPhase.Ruined,
                    0,
                    0L));
            }

            _store.Apply(delta);
        }

        /// <summary>
        /// 복구를 시작할 수 있는지만 본다. 상태를 바꾸지 않는다 —
        /// 폐허를 탭했을 때 띄우는 정보 창이 매 프레임 불러도 안전해야 한다.
        /// </summary>
        public BuildResult CanStartRepair(int slotId)
        {
            BuildingInstance building;
            if (!_store.State.TryGetBuilding(slotId, out building))
                return BuildResult.Fail(BuildRejection.UnknownSlot);

            if (!building.IsRuined)
                return BuildResult.Fail(BuildRejection.NotRuined);

            BuildingDef def;
            if (!_catalog.TryGet(building.DefId, out def))
                return BuildResult.Fail(BuildRejection.UnknownDef);

            if (!_store.State.Resources.CanAfford(def.BuildCosts))
                return BuildResult.Fail(BuildRejection.InsufficientResources);

            return BuildResult.Ok;
        }

        /// <summary>
        /// 폐허 복구를 시작한다. 비용 차감과 상태 전환이 한 델타로 함께 적용되므로
        /// "자원은 빠졌는데 공사가 안 시작된" 중간 상태가 생기지 않는다.
        /// (뉴포리아는 위치 저장과 건설 요청이 서버 2콜로 나뉘고 두 번째에 전송실패
        ///  콜백이 없어 통신이 죽으면 잠금이 남는 틈이 코드 주석에 적혀 있다.)
        /// </summary>
        public BuildResult TryStartRepair(int slotId)
        {
            BuildResult check = CanStartRepair(slotId);
            if (!check.Started)
                return check;

            BuildingInstance building;
            _store.State.TryGetBuilding(slotId, out building);

            BuildingDef def;
            _catalog.TryGet(building.DefId, out def);

            long now = _clock.NowUnixMs;
            bool instant = def.BuildDurationMs <= 0L;

            _store.Apply(new StateDelta()
                .WithResourceCosts(def.BuildCosts)
                .WithBuilding(BuildingChange.Update(
                    slotId,
                    building.Anchor,
                    instant ? BuildingPhase.Complete : BuildingPhase.Constructing,
                    instant ? 1 : 0,
                    instant ? 0L : now + def.BuildDurationMs)));

            return BuildResult.Ok;
        }

        /// <summary>
        /// 공사 시간이 지난 자리를 완성으로 넘긴다. 넘긴 개수를 돌려준다.
        ///
        /// 완료를 로컬 타이머 만료가 아니라 "끝나는 시각 &lt;= 지금"으로 판정하는 이유 —
        /// 앱이 백그라운드에 있었거나 프레임이 끊겨도 결과가 같고, 서버가 붙으면
        /// 같은 비교식이 서버 시각으로 그대로 동작한다.
        /// </summary>
        public int CompleteFinished()
        {
            long now = _clock.NowUnixMs;
            List<BuildingChange> finished = null;

            foreach (BuildingInstance b in _store.State.Buildings)
            {
                if (!b.IsConstructionFinished(now))
                    continue;

                if (finished == null)
                    finished = new List<BuildingChange>();

                finished.Add(BuildingChange.Update(
                    b.Id, b.Anchor, BuildingPhase.Complete, b.Level + 1, 0L));
            }

            if (finished == null)
                return 0;

            var delta = new StateDelta();
            for (int i = 0; i < finished.Count; i++)
            {
                delta.WithBuilding(finished[i]);
            }

            _store.Apply(delta);
            return finished.Count;
        }
    }
}

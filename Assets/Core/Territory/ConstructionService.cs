using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Territory
{
    public enum BuildRejection
    {
        None = 0,
        UnknownDef,
        OutOfBounds,
        Overlapping,
        InsufficientResources,
    }

    public readonly struct BuildResult
    {
        public readonly bool Started;
        public readonly BuildRejection Rejection;

        /// <summary>성공했을 때 새로 생긴 건물 번호. 실패면 -1.</summary>
        public readonly int BuildingId;

        private BuildResult(bool started, BuildRejection rejection, int buildingId)
        {
            Started = started;
            Rejection = rejection;
            BuildingId = buildingId;
        }

        public static BuildResult Ok(int buildingId) =>
            new BuildResult(true, BuildRejection.None, buildingId);

        public static BuildResult Fail(BuildRejection rejection) =>
            new BuildResult(false, rejection, -1);
    }

    /// <summary>
    /// 건설 흐름의 로직. 델타를 만들어 스토어에 넘기는 쪽이다.
    ///
    /// 여기에 UnityEngine이 한 줄도 없다는 점이 중요하다 — 그래서 Unity를 열지 않고
    /// <c>dotnet test</c>로 배치 규칙·비용 차감·타이머 완료를 전부 검증할 수 있다.
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
        /// 그 자리에 그 건물을 지을 수 있는지만 본다. 상태를 바꾸지 않는다 —
        /// 배치 미리보기(따라다니는 유령 건물)가 매 프레임 부를 수 있어야 한다.
        /// </summary>
        public BuildResult CanStart(string defId, GridPos anchor)
        {
            BuildingDef def;
            if (!_catalog.TryGet(defId, out def))
                return BuildResult.Fail(BuildRejection.UnknownDef);

            PlacementCheck check = _store.State.BaseOccupancy.Check(anchor, def.Footprint);

            if (!check.Allowed)
            {
                return BuildResult.Fail(
                    check.Rejection == PlacementRejection.OutOfBounds
                        ? BuildRejection.OutOfBounds
                        : BuildRejection.Overlapping);
            }

            if (!_store.State.Resources.CanAfford(def.BuildCosts))
                return BuildResult.Fail(BuildRejection.InsufficientResources);

            return BuildResult.Ok(-1);
        }

        /// <summary>
        /// 건설을 시작한다. 비용 차감과 건물 추가가 한 델타로 함께 적용되므로
        /// "자원은 빠졌는데 건물이 없다"는 중간 상태가 생기지 않는다.
        /// (뉴포리아는 위치 저장과 건설 요청이 서버 2콜로 나뉘어 있고, 두 번째 콜에
        ///  전송 실패 콜백이 없어서 통신이 죽으면 잠금이 남는 틈이 코드 주석에 적혀 있다.)
        /// </summary>
        public BuildResult TryStart(string defId, GridPos anchor)
        {
            BuildResult check = CanStart(defId, anchor);
            if (!check.Started)
                return check;

            BuildingDef def;
            _catalog.TryGet(defId, out def);

            int id = _store.State.NextBuildingId;
            long now = _clock.NowUnixMs;

            bool instant = def.BuildDurationMs <= 0L;

            var delta = new StateDelta()
                .WithResourceCosts(def.BuildCosts)
                .WithBuilding(BuildingChange.Add(
                    id,
                    def.DefId,
                    anchor,
                    def.Footprint,
                    instant ? BuildingPhase.Complete : BuildingPhase.Constructing,
                    instant ? 1 : 0,
                    instant ? 0L : now + def.BuildDurationMs));

            _store.Apply(delta);

            return BuildResult.Ok(id);
        }

        /// <summary>
        /// 공사 시간이 지난 건물을 완성으로 넘긴다. 넘긴 개수를 돌려준다.
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

        /// <summary>건물을 옮긴다. 자기 자신은 겹침 판정에서 빠진다.</summary>
        public BuildResult TryRelocate(int buildingId, GridPos newAnchor)
        {
            BuildingInstance building;
            if (!_store.State.TryGetBuilding(buildingId, out building))
                return BuildResult.Fail(BuildRejection.UnknownDef);

            PlacementCheck check = _store.State.BaseOccupancy.Check(
                newAnchor, building.Footprint, buildingId);

            if (!check.Allowed)
            {
                return BuildResult.Fail(
                    check.Rejection == PlacementRejection.OutOfBounds
                        ? BuildRejection.OutOfBounds
                        : BuildRejection.Overlapping);
            }

            _store.Apply(new StateDelta().WithBuilding(BuildingChange.Update(
                buildingId,
                newAnchor,
                building.Phase,
                building.Level,
                building.ConstructionEndsAtUnixMs)));

            return BuildResult.Ok(buildingId);
        }
    }
}

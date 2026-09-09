using System;
using System.Collections.Generic;

namespace Olympus.Core.State
{
    /// <summary>
    /// 상태를 바꾸는 유일한 관문.
    ///
    /// 규약은 하나다 — <see cref="Apply"/> 밖에서는 <see cref="GameState"/>가 바뀌지 않는다.
    /// 그래서 (a) 격자 점유 같은 불변식을 여기 한 곳에서만 지키면 되고,
    /// (b) 나중에 델타 생산자를 로컬 로직에서 서버 응답으로 갈아도 적용 경로는 그대로다.
    /// </summary>
    public sealed class StateStore
    {
        public GameState State { get; }

        /// <summary>
        /// 적용이 끝난 뒤 무엇이 바뀌었는지 알린다. HUD·격자 뷰가 이걸 듣고 갱신한다.
        /// 상태를 폴링하지 말고 이걸 구독할 것 — 폴링은 프레임마다 전 건물을 훑게 된다.
        /// </summary>
        public event Action<StateDelta> Changed;

        public StateStore(GameState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// 델타를 적용한다. 도중에 실패하면 예외를 던진다 — 부분 적용된 상태로 조용히
        /// 굴러가는 것보다 즉시 터지는 편이 낫다(자원이 음수가 되는 경로가 대표적).
        /// </summary>
        public void Apply(StateDelta delta)
        {
            if (delta == null)
                throw new ArgumentNullException(nameof(delta));

            if (delta.IsEmpty)
                return;

            ApplyResources(delta.Resources);
            ApplyBuildings(delta.Buildings);

            if (delta.ServerTimeUnixMs.HasValue)
                State.LastKnownServerTimeUnixMs = delta.ServerTimeUnixMs.Value;

            Changed?.Invoke(delta);
        }

        private void ApplyResources(List<ResourceChange> changes)
        {
            if (changes == null)
                return;

            for (int i = 0; i < changes.Count; i++)
            {
                ResourceChange c = changes[i];

                if (c.IsAbsolute)
                    State.Resources.SetAbsolute(c.Kind, c.Value);
                else
                    State.Resources.Add(c.Kind, c.Value);
            }
        }

        private void ApplyBuildings(List<BuildingChange> changes)
        {
            if (changes == null)
                return;

            for (int i = 0; i < changes.Count; i++)
            {
                BuildingChange c = changes[i];

                switch (c.Op)
                {
                    case ChangeOp.Add:
                        ApplyBuildingAdd(c);
                        break;

                    case ChangeOp.Update:
                        ApplyBuildingUpdate(c);
                        break;

                    case ChangeOp.Remove:
                        ApplyBuildingRemove(c);
                        break;

                    default:
                        throw new InvalidOperationException("모르는 변경 종류: " + c.Op);
                }
            }
        }

        private void ApplyBuildingAdd(BuildingChange c)
        {
            if (State.MutableBuildings.ContainsKey(c.Id))
                throw new InvalidOperationException("이미 있는 건물 번호로 Add가 왔다: " + c.Id);

            if (!State.BaseOccupancy.TryPlace(c.Id, c.Anchor, c.Footprint))
                throw new InvalidOperationException(
                    "놓을 수 없는 자리에 Add가 왔다: #" + c.Id + " @" + c.Anchor + " " + c.Footprint +
                    ". 델타를 만들기 전에 GridOccupancy.Check로 확인해야 한다.");

            var building = new BuildingInstance(
                c.Id, c.DefId, c.Anchor, c.Footprint, c.Phase, c.Level, c.ConstructionEndsAtUnixMs);

            State.MutableBuildings.Add(c.Id, building);

            if (c.Id >= State.NextBuildingId)
                State.NextBuildingId = c.Id + 1;
        }

        private void ApplyBuildingUpdate(BuildingChange c)
        {
            BuildingInstance building;
            if (!State.MutableBuildings.TryGetValue(c.Id, out building))
                throw new InvalidOperationException("없는 건물에 Update가 왔다: " + c.Id);

            // 자리가 옮겨졌으면 점유를 다시 깐다. 순서가 중요하다 —
            // 먼저 비우지 않으면 자기 자신과 겹친다고 판정된다.
            if (!building.Anchor.Equals(c.Anchor))
            {
                State.BaseOccupancy.RemoveOccupant(c.Id);

                if (!State.BaseOccupancy.TryPlace(c.Id, c.Anchor, building.Footprint))
                {
                    // 되돌린다 — 옮기지 못했으면 원래 자리를 유지해야 한다.
                    State.BaseOccupancy.TryPlace(c.Id, building.Anchor, building.Footprint);

                    throw new InvalidOperationException(
                        "놓을 수 없는 자리로 Update가 왔다: #" + c.Id + " → " + c.Anchor);
                }

                building.Anchor = c.Anchor;
            }

            building.Phase = c.Phase;
            building.Level = c.Level;
            building.ConstructionEndsAtUnixMs = c.ConstructionEndsAtUnixMs;
        }

        private void ApplyBuildingRemove(BuildingChange c)
        {
            if (!State.MutableBuildings.ContainsKey(c.Id))
                throw new InvalidOperationException("없는 건물에 Remove가 왔다: " + c.Id);

            State.BaseOccupancy.RemoveOccupant(c.Id);
            State.MutableBuildings.Remove(c.Id);
        }
    }
}

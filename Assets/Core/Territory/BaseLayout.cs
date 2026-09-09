using System;
using System.Collections.Generic;
using Olympus.Core.Grid;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// 기지에 미리 정해진 건물 자리 하나.
    ///
    /// 플레이어가 자리를 고르지 않는다 — 세계가 이미 폐허로 서 있고, 플레이어는 그것을
    /// 복구한다. 그래서 자리는 저작 데이터이고 런타임에 생기지 않는다.
    /// </summary>
    public sealed class BaseSlot
    {
        /// <summary>기지 안에서 유일한 자리 번호. 건물 인스턴스 번호로 그대로 쓴다.</summary>
        public int SlotId { get; }

        /// <summary>이 자리에 서는 건물 종류.</summary>
        public string DefId { get; }

        /// <summary>점유 영역의 최소 코너.</summary>
        public GridPos Anchor { get; }

        /// <summary>
        /// 어느 구역에 속하는가. 기지가 넓어질 때 구역째로 열기 위한 것 —
        /// 0은 처음부터 열려 있는 구역이다.
        /// </summary>
        public int ZoneId { get; }

        public BaseSlot(int slotId, string defId, GridPos anchor, int zoneId = 0)
        {
            if (slotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(slotId), "자리 번호는 1 이상이어야 한다.");
            if (string.IsNullOrEmpty(defId))
                throw new ArgumentException("DefId는 비울 수 없다.", nameof(defId));

            SlotId = slotId;
            DefId = defId;
            Anchor = anchor;
            ZoneId = zoneId;
        }

        public override string ToString() => "Slot#" + SlotId + " " + DefId + " @" + Anchor;
    }

    /// <summary>레이아웃 검증에서 걸린 문제 하나.</summary>
    public readonly struct LayoutProblem
    {
        public readonly int SlotId;
        public readonly string Message;

        public LayoutProblem(int slotId, string message)
        {
            SlotId = slotId;
            Message = message;
        }

        public override string ToString() => "Slot#" + SlotId + ": " + Message;
    }

    /// <summary>
    /// 기지의 자리 배치 전체. 저작물이다.
    ///
    /// <see cref="Validate"/>가 있는 이유 — 자리가 서로 겹치거나 범위를 벗어난 것은
    /// 데이터 실수인데, 검증 없이 굴리면 화면에서 건물 두 채가 겹쳐 보이는 것으로만
    /// 드러난다(그마저 각도에 따라 안 보인다). 로드 시점에 잡아서 어느 자리가 무엇과
    /// 겹치는지 이름을 대고 실패하는 편이 낫다.
    /// </summary>
    public sealed class BaseLayout
    {
        private readonly List<BaseSlot> _slots = new List<BaseSlot>();
        private readonly Dictionary<int, BaseSlot> _bySlotId = new Dictionary<int, BaseSlot>();

        public GridRect Bounds { get; }

        public BaseLayout(GridRect bounds)
        {
            Bounds = bounds;
        }

        public int SlotCount => _slots.Count;

        public IReadOnlyList<BaseSlot> Slots => _slots;

        public void Add(BaseSlot slot)
        {
            if (slot == null)
                throw new ArgumentNullException(nameof(slot));

            if (_bySlotId.ContainsKey(slot.SlotId))
                throw new InvalidOperationException("자리 번호가 중복이다: " + slot.SlotId);

            _slots.Add(slot);
            _bySlotId.Add(slot.SlotId, slot);
        }

        public bool TryGetSlot(int slotId, out BaseSlot slot)
        {
            return _bySlotId.TryGetValue(slotId, out slot);
        }

        /// <summary>
        /// 자리들이 범위 안에 들어오고 서로 겹치지 않는지 본다.
        /// 문제가 없으면 빈 목록을 돌려준다.
        /// </summary>
        public IReadOnlyList<LayoutProblem> Validate(BuildingCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var problems = new List<LayoutProblem>();
            var board = new GridOccupancy(Bounds);

            for (int i = 0; i < _slots.Count; i++)
            {
                BaseSlot slot = _slots[i];

                BuildingDef def;
                if (!catalog.TryGet(slot.DefId, out def))
                {
                    problems.Add(new LayoutProblem(
                        slot.SlotId, "카탈로그에 없는 DefId다: " + slot.DefId));
                    continue;
                }

                PlacementCheck check = board.Check(slot.Anchor, def.Footprint);

                if (!check.Allowed)
                {
                    problems.Add(new LayoutProblem(
                        slot.SlotId,
                        check.Rejection == PlacementRejection.OutOfBounds
                            ? "기지 범위를 벗어난다: " + slot.Anchor + " " + def.Footprint + " (범위 " + Bounds + ")"
                            : "Slot#" + check.BlockingOccupantId + " 와 겹친다: " + slot.Anchor + " " + def.Footprint));
                    continue;
                }

                board.TryPlace(slot.SlotId, slot.Anchor, def.Footprint);
            }

            return problems;
        }
    }
}

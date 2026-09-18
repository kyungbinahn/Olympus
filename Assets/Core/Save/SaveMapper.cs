using System;
using System.Collections.Generic;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Core.Save
{
    /// <summary>세이브를 읽으며 데이터와 어긋난 것들. 조용히 넘기지 않고 세어서 알린다.</summary>
    public readonly struct SaveLoadReport
    {
        /// <summary>저장된 진행을 그대로 물려받은 자리.</summary>
        public readonly int Restored;

        /// <summary>레이아웃에 새로 생겨서 폐허로 시작하는 자리.</summary>
        public readonly int Fresh;

        /// <summary>같은 자리에 다른 건물이 서게 되어 진행을 버린 자리.</summary>
        public readonly int Replaced;

        /// <summary>레이아웃에서 사라져 버린 자리.</summary>
        public readonly int Dropped;

        /// <summary>모르는 이름이라 건너뛴 자원 줄.</summary>
        public readonly int SkippedResources;

        public SaveLoadReport(int restored, int fresh, int replaced, int dropped, int skippedResources)
        {
            Restored = restored;
            Fresh = fresh;
            Replaced = replaced;
            Dropped = dropped;
            SkippedResources = skippedResources;
        }

        public bool HasMismatch => Fresh > 0 || Replaced > 0 || Dropped > 0 || SkippedResources > 0;

        public override string ToString()
        {
            return "복원 " + Restored + ", 새 자리 " + Fresh + ", 건물 바뀜 " + Replaced +
                   ", 사라진 자리 " + Dropped + ", 모르는 자원 " + SkippedResources;
        }
    }

    /// <summary>
    /// 게임 상태 ↔ 세이브 파일 변환.
    ///
    /// <b>레이아웃이 진실이고 세이브는 거기에 얹는 것이다.</b> 무엇이 어디 있는지는
    /// <see cref="BaseLayout"/>이 정하고, 세이브는 "그 자리가 어디까지 진행됐나"만 얹는다.
    /// 이 방향을 지키면 기획이 자리를 늘리거나 줄이거나 그 자리 건물을 바꿔도 예전 세이브가
    /// 그대로 읽힌다 — 데이터가 임시값인 동안 이게 제일 중요하다.
    ///
    /// 어긋나는 경우의 판단을 전부 여기 모아 뒀다. 어긋남을 각자 알아서 처리하게 두면
    /// 처리하는 쪽마다 규칙이 달라지고, 그게 "저장은 됐는데 이상하게 복원된다"가 된다.
    /// </summary>
    public static class SaveMapper
    {
        /// <summary>지금 상태를 저장 형태로 뜬다.</summary>
        public static SaveFile Capture(GameState state, long nowUnixMs)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var save = new SaveFile
            {
                version = SaveFile.CurrentVersion,
                savedAtUnixMs = nowUnixMs,
            };

            foreach (KeyValuePair<ResourceKind, long> pair in state.Resources.All())
            {
                save.resources.Add(new SavedResource
                {
                    kind = pair.Key.ToString(),
                    amount = pair.Value,
                });
            }

            foreach (BuildingInstance b in state.Buildings)
            {
                save.slots.Add(new SavedSlot
                {
                    slotId = b.Id,
                    defId = b.DefId,
                    phase = b.Phase.ToString(),
                    level = b.Level,
                    constructionEndsAtUnixMs = b.ConstructionEndsAtUnixMs,
                });
            }

            return save;
        }

        /// <summary>
        /// 세이브와 레이아웃을 합쳐 상태를 세울 델타를 만든다.
        /// <paramref name="save"/>가 null이면 전부 폐허로 시작하는 델타가 나온다.
        /// </summary>
        public static StateDelta ToDelta(
            SaveFile save, BaseLayout layout, BuildingCatalog catalog, out SaveLoadReport report)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var delta = new StateDelta();

            int restored = 0, fresh = 0, replaced = 0, skippedResources = 0;

            Dictionary<int, SavedSlot> savedById = IndexSlots(save);

            for (int i = 0; i < layout.Slots.Count; i++)
            {
                BaseSlot slot = layout.Slots[i];

                BuildingDef def;
                if (!catalog.TryGet(slot.DefId, out def))
                    continue;   // 데이터 오류다 — layout.Validate가 이미 잡는다.

                SavedSlot saved;
                bool hasSaved = savedById.TryGetValue(slot.SlotId, out saved);

                if (!hasSaved)
                {
                    fresh++;
                    delta.WithBuilding(FreshRuin(slot, def));
                    continue;
                }

                // 같은 자리에 다른 건물이 서게 됐다 — 진행을 물려받으면 조용히 이상해진다.
                if (saved.defId != slot.DefId)
                {
                    replaced++;
                    delta.WithBuilding(FreshRuin(slot, def));
                    continue;
                }

                BuildingPhase phase;
                if (!TryParsePhase(saved.phase, out phase))
                {
                    fresh++;
                    delta.WithBuilding(FreshRuin(slot, def));
                    continue;
                }

                restored++;
                delta.WithBuilding(BuildingChange.Add(
                    slot.SlotId,
                    slot.DefId,
                    slot.Anchor,
                    def.Footprint,
                    phase,
                    saved.level < 0 ? 0 : saved.level,
                    phase == BuildingPhase.Constructing ? saved.constructionEndsAtUnixMs : 0L));
            }

            int dropped = savedById.Count - (restored + replaced);
            if (dropped < 0)
                dropped = 0;

            if (save != null && save.resources != null)
            {
                for (int i = 0; i < save.resources.Count; i++)
                {
                    SavedResource r = save.resources[i];

                    ResourceKind kind;
                    if (r == null || !Enum.TryParse(r.kind, true, out kind))
                    {
                        skippedResources++;
                        continue;
                    }

                    delta.WithResource(ResourceChange.Absolute(kind, r.amount < 0 ? 0 : r.amount));
                }
            }

            report = new SaveLoadReport(restored, fresh, replaced, dropped, skippedResources);
            return delta;
        }

        private static BuildingChange FreshRuin(BaseSlot slot, BuildingDef def)
        {
            return BuildingChange.Add(
                slot.SlotId, slot.DefId, slot.Anchor, def.Footprint, BuildingPhase.Ruined, 0, 0L);
        }

        private static Dictionary<int, SavedSlot> IndexSlots(SaveFile save)
        {
            var byId = new Dictionary<int, SavedSlot>();

            if (save == null || save.slots == null)
                return byId;

            for (int i = 0; i < save.slots.Count; i++)
            {
                SavedSlot s = save.slots[i];
                if (s != null)
                    byId[s.slotId] = s;
            }

            return byId;
        }

        private static bool TryParsePhase(string name, out BuildingPhase phase)
        {
            return Enum.TryParse(name, true, out phase) && Enum.IsDefined(typeof(BuildingPhase), phase);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 건물 정의 파일 한 줄. JsonUtility가 읽는 형태라 필드가 평평하고 public이어야 한다.
    /// </summary>
    [Serializable]
    public sealed class BuildingDefRecord
    {
        public string defId;
        public string displayNameKey;
        public int width = 1;
        public int height = 1;
        public long buildDurationMs;
        public List<ResourceCostRecord> costs = new List<ResourceCostRecord>();
    }

    [Serializable]
    public sealed class ResourceCostRecord
    {
        /// <summary>ResourceKind 이름. 숫자가 아니라 이름으로 두는 이유 — 사람이 읽고 고칠 파일이다.</summary>
        public string kind;
        public long amount;
    }

    [Serializable]
    public sealed class BuildingDefFile
    {
        public List<BuildingDefRecord> defs = new List<BuildingDefRecord>();
    }

    [Serializable]
    public sealed class BaseSlotRecord
    {
        public int slotId;
        public string defId;

        /// <summary>점유 영역의 최소 코너 칸.</summary>
        public int x;
        public int y;

        /// <summary>확장 구역 번호. 0은 처음부터 열린 구역.</summary>
        public int zoneId;
    }

    [Serializable]
    public sealed class BaseLayoutFile
    {
        public int baseSize = 48;
        public List<BaseSlotRecord> slots = new List<BaseSlotRecord>();
    }

    /// <summary>
    /// JSON 파일을 Core 객체로 바꾼다.
    ///
    /// 파싱이 Core가 아니라 여기(Game)에 있는 이유 — Core는 UnityEngine을 참조하지 않아야
    /// <c>dotnet test</c>로 Unity 없이 돌 수 있다. JsonUtility는 UnityEngine이므로 Core에
    /// 들어갈 수 없다. 그래서 얇은 변환층만 이쪽에 두고, 검증·규칙은 전부 Core가 한다
    /// (<see cref="BaseLayout.Validate"/>).
    ///
    /// 형식을 JSON으로 고른 이유 — 사람이 diff로 검토할 수 있고, 나중에 만들 배치 툴이
    /// **같은 파일**을 쓰기 때문이다. 뉴포리아의 위치 저작 씬이 죽은 원인이 툴 산출물과
    /// 런타임이 읽는 원본이 갈라진 것이었다(툴은 없는 xlsx를 가리킨 채 주석 처리돼 있다).
    /// 툴이 런타임 원본을 직접 쓰면 그렇게 갈라질 수 없다.
    /// </summary>
    public static class TerritoryDataLoader
    {
        public static BuildingCatalog LoadCatalog(TextAsset json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json), "건물 정의 파일이 배선되지 않았다.");

            BuildingDefFile file = JsonUtility.FromJson<BuildingDefFile>(json.text);

            if (file == null || file.defs == null || file.defs.Count == 0)
                throw new InvalidOperationException("건물 정의가 비어 있다: " + json.name);

            var catalog = new BuildingCatalog();

            for (int i = 0; i < file.defs.Count; i++)
            {
                BuildingDefRecord r = file.defs[i];

                catalog.Add(new BuildingDef(
                    r.defId,
                    r.displayNameKey,
                    new GridFootprint(r.width, r.height),
                    r.buildDurationMs,
                    ParseCosts(r)));
            }

            return catalog;
        }

        private static List<ResourceCost> ParseCosts(BuildingDefRecord r)
        {
            var costs = new List<ResourceCost>();

            if (r.costs == null)
                return costs;

            for (int i = 0; i < r.costs.Count; i++)
            {
                ResourceCostRecord c = r.costs[i];

                ResourceKind kind;
                if (!Enum.TryParse(c.kind, true, out kind))
                {
                    throw new InvalidOperationException(
                        "모르는 자원 종류다: '" + c.kind + "' (건물 " + r.defId + "). " +
                        "쓸 수 있는 값: " + string.Join(", ", Enum.GetNames(typeof(ResourceKind))));
                }

                costs.Add(new ResourceCost(kind, c.amount));
            }

            return costs;
        }

        public static BaseLayoutFile LoadLayoutFile(TextAsset json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json), "기지 레이아웃 파일이 배선되지 않았다.");

            BaseLayoutFile file = JsonUtility.FromJson<BaseLayoutFile>(json.text);

            if (file == null || file.slots == null)
                throw new InvalidOperationException("기지 레이아웃을 읽을 수 없다: " + json.name);

            return file;
        }

        /// <summary>
        /// 레이아웃 파일을 Core <see cref="BaseLayout"/>으로 바꾼다.
        /// 유효성 검사는 하지 않는다 — 그건 Core의 <see cref="BaseLayout.Validate"/> 몫이고
        /// <see cref="ConstructionService.InitializeFromLayout"/>이 부른다.
        /// </summary>
        public static BaseLayout ToLayout(BaseLayoutFile file, GridRect bounds)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            var layout = new BaseLayout(bounds);

            for (int i = 0; i < file.slots.Count; i++)
            {
                BaseSlotRecord s = file.slots[i];
                layout.Add(new BaseSlot(s.slotId, s.defId, new GridPos(s.x, s.y), s.zoneId));
            }

            return layout;
        }
    }
}

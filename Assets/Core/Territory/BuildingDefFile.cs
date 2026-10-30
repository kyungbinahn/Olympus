using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// <c>building-defs.json</c>의 모양. 필드가 평평하고 public인 것은 Unity의 JsonUtility가
    /// 그 형태만 읽기 때문이다.
    ///
    /// 그래도 Core에 둔다(<c>SaveFile</c>과 같은 이유) — 파일 모양에서 규칙 객체로 바꾸는 판단
    /// (모르는 자원 이름을 어떻게 다루나 등)은 시험이 걸리는 곳에 있어야 한다. Unity에 묶이는 것은
    /// 글자를 이 클래스로 읽는 한 줄뿐이다(<c>TerritoryDataLoader</c>). 덕분에 Unity 밖 시험이
    /// 실제 데이터 파일을 같은 변환 코드로 읽어 볼 수 있다.
    /// </summary>
    [Serializable]
    public sealed class BuildingDefFile
    {
        public List<BuildingDefRecord> defs = new List<BuildingDefRecord>();

        public BuildingCatalog ToCatalog(string sourceName)
        {
            if (defs == null || defs.Count == 0)
                throw new InvalidOperationException("건물 정의가 비어 있다: " + sourceName);

            var catalog = new BuildingCatalog();

            for (int i = 0; i < defs.Count; i++)
            {
                BuildingDefRecord r = defs[i];

                ProductionSpec production = null;
                if (r.produces != null && r.produces.perMinute > 0)
                {
                    production = new ProductionSpec(
                        DataParse.Resource(r.produces.kind, "건물 " + r.defId + " 생산"),
                        r.produces.perMinute,
                        r.produces.capMinutes);
                }

                catalog.Add(new BuildingDef(
                    r.defId,
                    r.displayNameKey,
                    new GridFootprint(r.width, r.height),
                    r.buildDurationMs,
                    DataParse.Costs(r.costs, "건물 " + r.defId),
                    r.unlockHqLevel,
                    production));
            }

            return catalog;
        }
    }

    [Serializable]
    public sealed class BuildingDefRecord
    {
        public string defId;
        public string displayNameKey;
        public int width = 1;
        public int height = 1;
        public long buildDurationMs;
        public List<ResourceCostRecord> costs = new List<ResourceCostRecord>();

        /// <summary>본부가 이 레벨 이상이어야 복구할 수 있다.</summary>
        public int unlockHqLevel;

        /// <summary>
        /// 생산 건물이면 채운다. JsonUtility는 빠진 객체 필드를 null이 아니라 기본값 객체로
        /// 채우므로, "생산 안 함"은 perMinute가 0인 것으로 판정한다.
        /// </summary>
        public ProductionRecord produces;
    }

    [Serializable]
    public sealed class ProductionRecord
    {
        public string kind;
        public long perMinute;
        public long capMinutes = 60;
    }

    [Serializable]
    public sealed class ResourceCostRecord
    {
        /// <summary>ResourceKind 이름. 숫자가 아니라 이름으로 두는 이유 — 사람이 읽고 고칠 파일이다.</summary>
        public string kind;
        public long amount;
    }

    /// <summary>데이터 파일의 글자 값을 규칙 값으로 바꾼다. 모르는 값은 어디서 났는지 대며 터진다.</summary>
    public static class DataParse
    {
        public static ResourceKind Resource(string name, string where)
        {
            ResourceKind kind;
            if (!Enum.TryParse(name, true, out kind) || !Enum.IsDefined(typeof(ResourceKind), kind))
            {
                throw new InvalidOperationException(
                    "모르는 자원 종류다: '" + name + "' (" + where + "). " +
                    "쓸 수 있는 값: " + string.Join(", ", Enum.GetNames(typeof(ResourceKind))));
            }

            return kind;
        }

        public static List<ResourceCost> Costs(List<ResourceCostRecord> records, string where)
        {
            var costs = new List<ResourceCost>();

            if (records == null)
                return costs;

            for (int i = 0; i < records.Count; i++)
            {
                costs.Add(new ResourceCost(Resource(records[i].kind, where), records[i].amount));
            }

            return costs;
        }
    }
}

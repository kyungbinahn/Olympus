using System;
using System.Collections.Generic;
using Olympus.Core.Grid;
using Olympus.Core.State;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// 건물 한 종류의 정의. 지금은 코드에서 만들지만, 곧 파일(테이블)에서 읽는다.
    ///
    /// ⚠️ 이름을 테마 고유명사로 짓지 않는다. <c>DefId</c>는 데이터 키이므로 세계관 단어를
    /// 써도 되지만, 클래스·필드 이름은 메커니즘으로만 짓는다. 뉴포리아는 이 규칙이 생기기
    /// 전에 쓰인 코드가 남아 <c>FloatingIslandBuild</c>가 343개 파일에 박혀 있고,
    /// 그 이름이 GraphQL 스키마와 서버 DB까지 걸려 있어 이제 바꿀 수 없다.
    /// </summary>
    public sealed class BuildingDef
    {
        public string DefId { get; }

        /// <summary>화면에 뭐라고 쓸지 — 로컬라이즈 키. 문구 자체를 코드에 넣지 않는다.</summary>
        public string DisplayNameKey { get; }

        public GridFootprint Footprint { get; }

        /// <summary>건설에 걸리는 시간(밀리초).</summary>
        public long BuildDurationMs { get; }

        public IReadOnlyList<ResourceCost> BuildCosts { get; }

        /// <summary>
        /// 본부가 이 레벨 이상이어야 폐허를 복구할 수 있다. 0이면 처음부터 열려 있다
        /// (본부 자신이 0이어야 한다 — 본부 레벨은 본부를 복구해야 1이 된다).
        /// "본부 레벨이 오를 때마다 새 건물이 열린다"가 탑포스·뉴포리아 공통의 진척 문법이다.
        /// </summary>
        public int UnlockHqLevel { get; }

        /// <summary>완성되면 자원을 쌓는 건물이면 그 규칙, 아니면 null.</summary>
        public ProductionSpec Production { get; }

        public BuildingDef(
            string defId,
            string displayNameKey,
            GridFootprint footprint,
            long buildDurationMs,
            IReadOnlyList<ResourceCost> buildCosts,
            int unlockHqLevel = 0,
            ProductionSpec production = null)
        {
            if (string.IsNullOrEmpty(defId))
                throw new ArgumentException("DefId는 비울 수 없다.", nameof(defId));
            if (buildDurationMs < 0L)
                throw new ArgumentOutOfRangeException(nameof(buildDurationMs));
            if (unlockHqLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(unlockHqLevel));

            DefId = defId;
            DisplayNameKey = displayNameKey;
            Footprint = footprint;
            BuildDurationMs = buildDurationMs;
            BuildCosts = buildCosts ?? new List<ResourceCost>();
            UnlockHqLevel = unlockHqLevel;
            Production = production;
        }

        public override string ToString() => DefId + " " + Footprint;
    }

    /// <summary>
    /// 생산 건물의 규칙. 레벨 1 기준 분당 생산량이고, 레벨이 오르면 레벨 배로 늘어난다.
    ///
    /// 보관 한도(<see cref="CapMinutes"/>)가 있는 이유 — 한도가 없으면 오래 비울수록 무한히
    /// 쌓여서 "언제 다시 들어와야 하나"가 사라진다. 탑포스는 이 한도(금광산 8시간)로
    /// 접속 주기를 정한다. 초반에는 짧게 두고 본부 레벨이 오르면 데이터로 늘린다.
    /// </summary>
    public sealed class ProductionSpec
    {
        public ResourceKind Kind { get; }
        public long PerMinute { get; }
        public long CapMinutes { get; }

        public ProductionSpec(ResourceKind kind, long perMinute, long capMinutes)
        {
            if (perMinute <= 0L)
                throw new ArgumentOutOfRangeException(nameof(perMinute), "분당 생산량은 1 이상이어야 한다.");
            if (capMinutes <= 0L)
                throw new ArgumentOutOfRangeException(nameof(capMinutes), "보관 한도는 1분 이상이어야 한다.");

            Kind = kind;
            PerMinute = perMinute;
            CapMinutes = capMinutes;
        }
    }

    /// <summary>건물 정의 목록. DefId로 찾는다.</summary>
    public sealed class BuildingCatalog
    {
        private readonly Dictionary<string, BuildingDef> _byId = new Dictionary<string, BuildingDef>();

        public void Add(BuildingDef def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));

            if (_byId.ContainsKey(def.DefId))
                throw new InvalidOperationException("DefId가 중복이다: " + def.DefId);

            _byId.Add(def.DefId, def);
        }

        public bool TryGet(string defId, out BuildingDef def)
        {
            if (defId == null)
            {
                def = null;
                return false;
            }

            return _byId.TryGetValue(defId, out def);
        }

        public int Count => _byId.Count;

        public IEnumerable<BuildingDef> All => _byId.Values;
    }
}

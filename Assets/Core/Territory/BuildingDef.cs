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

        public BuildingDef(
            string defId,
            string displayNameKey,
            GridFootprint footprint,
            long buildDurationMs,
            IReadOnlyList<ResourceCost> buildCosts)
        {
            if (string.IsNullOrEmpty(defId))
                throw new ArgumentException("DefId는 비울 수 없다.", nameof(defId));
            if (buildDurationMs < 0L)
                throw new ArgumentOutOfRangeException(nameof(buildDurationMs));

            DefId = defId;
            DisplayNameKey = displayNameKey;
            Footprint = footprint;
            BuildDurationMs = buildDurationMs;
            BuildCosts = buildCosts ?? new List<ResourceCost>();
        }

        public override string ToString() => DefId + " " + Footprint;
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

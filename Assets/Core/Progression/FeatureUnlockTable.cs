using System;
using System.Collections.Generic;
using Olympus.Core.Territory;

namespace Olympus.Core.Progression
{
    /// <summary>
    /// 기능(화면 버튼) → 열리는 본부 레벨. <c>Assets/Data/function-unlock.json</c>에서 온다.
    ///
    /// 탑포스는 "어떤 버튼이 언제 보이는가"를 코드가 아니라 표(<c>functionunlockconf</c>)로 관리한다.
    /// 버튼을 전부 보여 주고 "준비중"을 띄우는 것보다, 아직 안 열린 것은 숨겼다가 본부 레벨이
    /// 오르는 순간 생기게 하는 편이 낫다 — 새 버튼이 생기는 것 자체가 레벨업 보상이 된다.
    ///
    /// 표에 없는 기능은 <b>열린 것</b>으로 본다. 반대로 하면 표에 한 줄 빠뜨린 버튼이 조용히
    /// 사라지고, 사라진 버튼은 "왜 안 보이지"를 찾기가 보이는 버튼보다 훨씬 어렵다.
    /// </summary>
    public sealed class FeatureUnlockTable
    {
        private readonly Dictionary<string, int> _levels = new Dictionary<string, int>();
        private readonly List<string> _order = new List<string>();

        public void Add(string featureId, int hqLevel)
        {
            if (string.IsNullOrEmpty(featureId))
                throw new ArgumentException("기능 이름은 비울 수 없다.", nameof(featureId));
            if (hqLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(hqLevel));
            if (_levels.ContainsKey(featureId))
                throw new InvalidOperationException("기능이 중복이다: " + featureId);

            _levels.Add(featureId, hqLevel);
            _order.Add(featureId);
        }

        public bool IsUnlocked(string featureId, int hqLevel)
        {
            int required;
            return !_levels.TryGetValue(featureId, out required) || hqLevel >= required;
        }

        /// <summary>표에 없으면 0.</summary>
        public int RequiredLevel(string featureId)
        {
            int required;
            return _levels.TryGetValue(featureId, out required) ? required : 0;
        }

        /// <summary>정확히 그 레벨에서 열리는 기능들. 레벨업 알림에 쓴다.</summary>
        public List<string> UnlockedExactlyAt(int hqLevel)
        {
            var result = new List<string>();

            for (int i = 0; i < _order.Count; i++)
            {
                if (_levels[_order[i]] == hqLevel)
                    result.Add(_order[i]);
            }

            return result;
        }

        /// <summary>정확히 그 본부 레벨에서 복구가 열리는 건물들. 레벨업 알림에 쓴다.</summary>
        public static List<BuildingDef> BuildingsUnlockedAt(BuildingCatalog catalog, int hqLevel)
        {
            var result = new List<BuildingDef>();

            foreach (BuildingDef def in catalog.All)
            {
                if (def.UnlockHqLevel == hqLevel)
                    result.Add(def);
            }

            return result;
        }
    }
}

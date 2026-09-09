using System;
using System.Collections.Generic;

namespace Olympus.Core.State
{
    /// <summary>
    /// 자원 종류. 코드가 종류마다 분기하는 구조적 값이라 enum으로 둔다.
    ///
    /// ⚠️ 개수를 함부로 늘리지 않는다. 뉴포리아는 화폐 축이 네 갈래
    /// (<c>CurrencyType</c>·<c>ResourcesType</c>·<c>PointType</c>·<c>ShopCostType</c>)로
    /// 갈라진 뒤 상위에서 <c>BaseType</c> 29종으로 다시 묶는 구조가 됐다.
    /// 여기서는 한 축으로 유지하고, "보상으로 줄 수 있는 것" 전체는
    /// 나중에 지급 키 공간(BaseKey 같은 것) 하나로 따로 설계한다.
    /// </summary>
    public enum ResourceKind
    {
        Wood = 0,
        Stone = 1,
        Food = 2,
        Gold = 3,
    }

    /// <summary>자원 보유량. 음수가 될 수 없다.</summary>
    public sealed class ResourcePool
    {
        private readonly Dictionary<ResourceKind, long> _amounts = new Dictionary<ResourceKind, long>();

        public long Get(ResourceKind kind)
        {
            long value;
            return _amounts.TryGetValue(kind, out value) ? value : 0L;
        }

        public bool CanAfford(ResourceKind kind, long cost) => Get(kind) >= cost;

        public bool CanAfford(IReadOnlyList<ResourceCost> costs)
        {
            if (costs == null)
                return true;

            for (int i = 0; i < costs.Count; i++)
            {
                if (!CanAfford(costs[i].Kind, costs[i].Amount))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 증감을 적용한다. State 조립체 안에서만 부를 수 있다 —
        /// 자원 변경은 <see cref="StateStore.Apply"/> 한 경로만 지나야 한다.
        /// </summary>
        internal void Add(ResourceKind kind, long delta)
        {
            long next = Get(kind) + delta;

            if (next < 0L)
                throw new InvalidOperationException(
                    "자원이 음수가 됐다: " + kind + " (" + Get(kind) + " + " + delta + "). " +
                    "차감 전에 CanAfford로 확인해야 한다.");

            _amounts[kind] = next;
        }

        internal void SetAbsolute(ResourceKind kind, long value)
        {
            if (value < 0L)
                throw new ArgumentOutOfRangeException(nameof(value), "자원은 음수가 될 수 없다.");

            _amounts[kind] = value;
        }

        public IEnumerable<KeyValuePair<ResourceKind, long>> All() => _amounts;
    }

    /// <summary>비용 한 줄. 건설비·업그레이드비 등에 쓴다.</summary>
    public readonly struct ResourceCost
    {
        public readonly ResourceKind Kind;
        public readonly long Amount;

        public ResourceCost(ResourceKind kind, long amount)
        {
            if (amount < 0L)
                throw new ArgumentOutOfRangeException(nameof(amount), "비용은 음수가 될 수 없다.");

            Kind = kind;
            Amount = amount;
        }

        public override string ToString() => Kind + " " + Amount;
    }
}

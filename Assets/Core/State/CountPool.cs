using System;
using System.Collections.Generic;

namespace Olympus.Core.State
{
    /// <summary>
    /// 이름 → 개수. 음수가 될 수 없다. 아이템 가방과 누적 기록(카운터)이 같이 쓴다.
    ///
    /// <see cref="ResourcePool"/>과 모양이 같은데 합치지 않은 이유 — 자원은 코드가 종류마다
    /// 분기하는 고정 축(enum)이고, 이쪽은 데이터가 키를 늘려 가는 열린 공간이다.
    /// 둘을 한 키 공간에 섞으면 "자원 축은 넷으로 고정"이라는 규칙이 무너진다.
    /// </summary>
    public sealed class CountPool
    {
        private readonly Dictionary<string, long> _amounts = new Dictionary<string, long>();

        public long Get(string key)
        {
            long value;
            return key != null && _amounts.TryGetValue(key, out value) ? value : 0L;
        }

        internal void Apply(CountChange change)
        {
            if (string.IsNullOrEmpty(change.Key))
                throw new InvalidOperationException("이름 없는 개수 변경이 왔다.");

            long next = change.IsAbsolute ? change.Value : Get(change.Key) + change.Value;

            if (next < 0L)
                throw new InvalidOperationException(
                    "개수가 음수가 됐다: " + change.Key + " (" + Get(change.Key) + " → " + next + "). " +
                    "차감 전에 개수를 확인해야 한다.");

            _amounts[change.Key] = next;
        }

        public IEnumerable<KeyValuePair<string, long>> All() => _amounts;
    }
}

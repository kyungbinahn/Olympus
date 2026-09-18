namespace Olympus.Core.World
{
    /// <summary>
    /// 씨앗이 같으면 어디서든 같은 수열을 주는 난수. xorshift32다.
    ///
    /// ⚠️ <c>System.Random</c>을 쓰지 않는 이유 — 그 알고리즘은 .NET 버전마다 다르다
    /// (.NET 6에서 한 번 바뀌었고, Mono와 CoreCLR도 같지 않다). 월드 생성에 그걸 쓰면
    /// <c>dotnet test</c> 하네스(.NET 8)와 Unity(Mono)가 같은 씨앗에서 다른 월드를 만든다.
    /// 시험은 통과하는데 게임은 다른 맵이 나오는, 조용히 어긋나는 부류의 사고다.
    /// 나중에 서버가 같은 월드를 재현해야 할 때도 같은 이유로 이쪽이 필요하다.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(int seed)
        {
            // 0은 xorshift가 영원히 0을 뱉는 고정점이라 피한다.
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public uint NextUInt()
        {
            unchecked
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }
        }

        /// <summary>[0, maxExclusive) 범위의 정수.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                return 0;

            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>[minInclusive, maxExclusive) 범위의 정수.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>[0, 1) 실수.</summary>
        public float NextFloat()
        {
            // 상위 24비트만 쓴다 — float의 유효 비트가 24개라 그 아래는 어차피 버려진다.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }
    }
}

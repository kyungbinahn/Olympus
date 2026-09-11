namespace Olympus.Core.World
{
    /// <summary>
    /// 지형 높낮이를 만드는 값 잡음(value noise).
    ///
    /// <c>Mathf.PerlinNoise</c>를 안 쓰는 이유는 그게 UnityEngine이라서다 — Core는
    /// UnityEngine을 참조할 수 없고(asmdef가 막는다), 그래야 이 생성기가 Unity 없이
    /// 시험된다. 대신 정수 해시 기반이라 플랫폼·런타임이 달라도 결과가 같다
    /// (<see cref="DeterministicRandom"/>과 같은 이유).
    /// </summary>
    public sealed class ValueNoise
    {
        private readonly int _seed;

        public ValueNoise(int seed)
        {
            _seed = seed;
        }

        /// <summary>격자점 하나의 값. 0~1.</summary>
        private float ValueAt(int x, int y)
        {
            unchecked
            {
                uint h = (uint)_seed;
                h ^= (uint)x * 0x9E3779B1u;
                h ^= (uint)y * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2545F491u;
                h ^= h >> 13;
                h *= 0x9E3779B1u;
                h ^= h >> 16;

                return (h >> 8) * (1.0f / 16777216.0f);
            }
        }

        /// <summary>
        /// 한 옥타브. 격자점 네 개를 부드럽게 섞는다.
        /// smoothstep으로 보간해야 칸 경계가 각져 보이지 않는다.
        /// </summary>
        public float Sample(float x, float y)
        {
            int x0 = FloorToInt(x);
            int y0 = FloorToInt(y);

            float fx = x - x0;
            float fy = y - y0;

            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);

            float v00 = ValueAt(x0, y0);
            float v10 = ValueAt(x0 + 1, y0);
            float v01 = ValueAt(x0, y0 + 1);
            float v11 = ValueAt(x0 + 1, y0 + 1);

            float top = v00 + (v10 - v00) * sx;
            float bottom = v01 + (v11 - v01) * sx;

            return top + (bottom - top) * sy;
        }

        /// <summary>
        /// 여러 옥타브를 겹친 값. 0~1.
        ///
        /// 옥타브를 겹치는 이유 — 한 겹만 쓰면 대륙 덩어리는 생기는데 해안선이
        /// 밋밋한 원형이 된다. 잔주름을 얹어야 들쭉날쭉해 보인다.
        /// </summary>
        public float Fractal(float x, float y, int octaves, float persistence)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float total = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * frequency, y * frequency) * amplitude;
                total += amplitude;

                amplitude *= persistence;
                frequency *= 2f;
            }

            return total <= 0f ? 0f : sum / total;
        }

        /// <summary>Math.Floor를 정수로. 음수 좌표에서 0 쪽으로 잘리면 격자가 한 칸 밀린다.</summary>
        private static int FloorToInt(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }
    }
}

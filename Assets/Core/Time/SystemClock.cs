using System;

namespace Olympus.Core.Time
{
    /// <summary>기기 UTC 시계. 서버가 붙기 전까지의 기본 구현.</summary>
    public sealed class SystemClock : IClock
    {
        public long NowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 손으로 돌리는 시계. 시험과 에디터 치트(시간 가속)에서 쓴다.
    /// 시험이 실제 시간을 기다리지 않아도 되게 만드는 것이 주 목적이다.
    /// </summary>
    public sealed class ManualClock : IClock
    {
        public long NowUnixMs { get; private set; }

        public ManualClock(long startUnixMs = 0)
        {
            NowUnixMs = startUnixMs;
        }

        public void Advance(long deltaMs)
        {
            if (deltaMs < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaMs), "시계는 뒤로 가지 않는다.");

            NowUnixMs += deltaMs;
        }

        public void SetTo(long unixMs)
        {
            NowUnixMs = unixMs;
        }
    }
}

namespace Olympus.Core.Grid
{
    /// <summary>
    /// 지면 평면 위의 한 점. Core는 UnityEngine을 참조하지 않으므로 Vector3를 쓸 수 없고,
    /// 높이(Y)는 격자 계산에 필요하지 않으므로 X/Z만 든다.
    /// Unity 쪽에서 <c>new Vector3(p.X, height, p.Z)</c>로 올려 쓴다.
    /// </summary>
    public readonly struct WorldXZ
    {
        public readonly float X;
        public readonly float Z;

        public WorldXZ(float x, float z)
        {
            X = x;
            Z = z;
        }

        public static WorldXZ operator +(WorldXZ a, WorldXZ b) => new WorldXZ(a.X + b.X, a.Z + b.Z);

        public static WorldXZ operator -(WorldXZ a, WorldXZ b) => new WorldXZ(a.X - b.X, a.Z - b.Z);

        public override string ToString() => "(" + X + ", " + Z + ")";
    }
}

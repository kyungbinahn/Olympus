using System;
using System.Collections.Generic;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// 건물 한 종류의 아트 보정 — 모델이 나온 방향·크기를 격자에 맞추는 값.
    ///
    /// 기획 값(<see cref="BuildingDef"/>)과 파일을 나눠 두는 이유 — 이건 아트 값이다.
    /// 모델을 다시 뽑으면 바뀌고 비용·시간은 그대로다. 한 파일에 섞으면 아트를
    /// 갈아끼울 때마다 기획 테이블을 건드리게 된다.
    ///
    /// 씬이 아니라 파일에 두는 이유 — 이 값을 만드는 편집 툴과 게임이 **같은 파일**을
    /// 써야 갈라지지 않는다. 뉴포리아의 위치 저작 씬이 갈라져서 죽었다.
    /// </summary>
    public sealed class BuildingArtAdjustment
    {
        public string DefId { get; }

        /// <summary>이 건물만 더 돌리는 각도(도). 전체 공통 각도 위에 더해진다.</summary>
        public float YawDegrees { get; }

        /// <summary>칸에 맞춘 크기에 곱하는 값. 1이면 칸을 꽉 채운다.</summary>
        public float ScaleMultiplier { get; }

        /// <summary>
        /// 자리 중앙에서 밀어낼 거리(월드 단위). 기본은 0 — 건물이 자기 칸 중앙에 바닥을 대고 선다.
        ///
        /// 왜 필요한가 — 모델의 덩어리가 한쪽으로 치우쳐 나온 경우(예: 배가 밖으로 나온 조선소)
        /// 경계 중심에 맞추면 보기에 어긋난다. 그때 사람이 눈으로 밀어 놓는 값이다.
        /// </summary>
        public float OffsetX { get; }

        /// <summary>위아래로 밀어낼 거리. 바닥에 묻거나 띄울 때 쓴다.</summary>
        public float OffsetY { get; }

        public float OffsetZ { get; }

        public BuildingArtAdjustment(string defId, float yawDegrees, float scaleMultiplier)
            : this(defId, yawDegrees, scaleMultiplier, 0f, 0f, 0f)
        {
        }

        public BuildingArtAdjustment(
            string defId,
            float yawDegrees,
            float scaleMultiplier,
            float offsetX,
            float offsetY,
            float offsetZ)
        {
            if (defId == null)
                throw new ArgumentNullException(nameof(defId));

            // 0이면 모델이 사라지고 음수면 뒤집힌다. 둘 다 화면에서는 "아트가 안 들어왔다"로
            // 보여서 원인을 엉뚱한 데서 찾게 된다 — 손으로 고치는 파일이라 여기서 막는다.
            if (scaleMultiplier <= 0f || float.IsNaN(scaleMultiplier) || float.IsInfinity(scaleMultiplier))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scaleMultiplier),
                    "크기 배율은 0보다 큰 유한한 값이어야 한다: " + defId + " = " + scaleMultiplier);
            }

            DefId = defId;
            YawDegrees = NormalizeYaw(yawDegrees);
            ScaleMultiplier = scaleMultiplier;
            OffsetX = Finite(offsetX, nameof(offsetX), defId);
            OffsetY = Finite(offsetY, nameof(offsetY), defId);
            OffsetZ = Finite(offsetZ, nameof(offsetZ), defId);
        }

        private static float Finite(float value, string name, string defId)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "숫자가 아닌 값이다: " + defId);

            return value;
        }

        /// <summary>
        /// 각도를 (-180, 180]으로 접는다. 편집 툴이 씬에서 읽어 오는 각도는 0~360이고
        /// 사람이 파일에 적는 값은 음수일 수 있다 — 둘이 같은 방향이면 같은 값이어야 한다.
        /// </summary>
        public static float NormalizeYaw(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                throw new ArgumentOutOfRangeException(nameof(degrees), "각도가 유한한 값이 아니다.");

            float y = degrees % 360f;

            if (y > 180f)
                y -= 360f;
            else if (y <= -180f)
                y += 360f;

            return y;
        }

        public override string ToString()
        {
            return DefId + " yaw " + YawDegrees + "° x" + ScaleMultiplier +
                   " offset(" + OffsetX + ", " + OffsetY + ", " + OffsetZ + ")";
        }
    }

    /// <summary>
    /// 건물별 아트 보정 목록. 없는 건물은 <see cref="Neutral"/>(보정 없음)로 다룬다 —
    /// 아트가 아직 안 들어온 건물이 항상 있고, 그때 터지면 기지 전체가 안 뜬다.
    /// </summary>
    public sealed class BuildingArtTable
    {
        /// <summary>보정 없음. 돌리지도 키우지도 않는다.</summary>
        public static readonly BuildingArtAdjustment Neutral = new BuildingArtAdjustment(string.Empty, 0f, 1f);

        private readonly Dictionary<string, BuildingArtAdjustment> _byId =
            new Dictionary<string, BuildingArtAdjustment>();

        public void Add(BuildingArtAdjustment adjustment)
        {
            if (adjustment == null)
                throw new ArgumentNullException(nameof(adjustment));

            if (string.IsNullOrEmpty(adjustment.DefId))
                throw new ArgumentException("DefId는 비울 수 없다.", nameof(adjustment));

            if (_byId.ContainsKey(adjustment.DefId))
                throw new InvalidOperationException("DefId가 중복이다: " + adjustment.DefId);

            _byId.Add(adjustment.DefId, adjustment);
        }

        public bool TryGet(string defId, out BuildingArtAdjustment adjustment)
        {
            if (defId == null)
            {
                adjustment = null;
                return false;
            }

            return _byId.TryGetValue(defId, out adjustment);
        }

        /// <summary>그 건물의 보정. 없으면 <see cref="Neutral"/>.</summary>
        public BuildingArtAdjustment For(string defId)
        {
            BuildingArtAdjustment found;
            return TryGet(defId, out found) ? found : Neutral;
        }

        public int Count => _byId.Count;

        public IEnumerable<BuildingArtAdjustment> All => _byId.Values;
    }
}

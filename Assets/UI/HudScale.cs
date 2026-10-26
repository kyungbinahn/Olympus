using UnityEngine;
using UnityEngine.UI;

namespace Olympus.UI
{
    /// <summary>
    /// HUD 전체 크기 슬라이더 — 크기는 화면을 보고 사람이 정할 값이라 인스펙터에서 돌린다(플레이 중에도 바로 반영).
    ///
    /// 칸마다 숫자를 줄이지 않고 CanvasScaler의 기준 해상도를 바꾼다: 기준 폭을 1080/배율로 두면 모든 칸·글자·간격이
    /// 같은 비율로 줄어서 시안 비례가 그대로 남는다. 위치는 화면 모서리 앵커 기준이라 가장자리에 붙은 채 줄어든다.
    /// HUD를 다시 지어도(HudSceneBuilder) 이 값은 옛 HUD에서 읽어 이어 쓴다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class HudScale : MonoBehaviour
    {
        /// <summary>시안 기준 폭(1080×1920 세로).</summary>
        public const float DesignWidth = 1080f;
        public const float DesignHeight = 1920f;

        [Tooltip("1 = 시안 그대로. 0.9 = 10% 작게.")]
        [Range(0.6f, 1.2f)]
        [SerializeField] private float _scale = 0.88f;

        public float Scale
        {
            get => _scale;
            set { _scale = Mathf.Clamp(value, 0.6f, 1.2f); Apply(); }
        }

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        private void Apply()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null || _scale <= 0f)
                return;

            scaler.referenceResolution = new Vector2(DesignWidth / _scale, DesignHeight / _scale);
        }
    }
}

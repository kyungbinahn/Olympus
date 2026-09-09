using UnityEngine;
using UnityEngine.InputSystem;
using Olympus.Core.Grid;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 기지 카메라 — 지면을 내려다보며 팬·줌 한다.
    ///
    /// ⚠️ 광고에 쓴 레인 카메라(진행 방향 고정, 45° 부동)를 게임에 가져오지 않는다.
    /// 그건 영상 문법이고, 플레이어가 몇백 시간 들여다보며 직접 돌려야 하는 화면에는
    /// 팬·줌이 필요하다.
    ///
    /// 입력은 새 Input System API(<see cref="Mouse"/>·<see cref="Touchscreen"/>)를 직접 읽는다.
    /// 프로젝트가 activeInputHandler=1(새 시스템 전용)이라 레거시 <c>Input.*</c>은 동작하지 않는다.
    /// </summary>
    public sealed class TerritoryCamera : MonoBehaviour
    {
        [Header("바라보는 각")]
        [Tooltip("내려다보는 각도(도). 0이면 수평, 90이면 정수직.")]
        [SerializeField, Range(20f, 89f)] private float _pitch = 50f;

        [Tooltip("나침반 회전(도). SLG 기지는 보통 고정이다.")]
        [SerializeField] private float _yaw = 45f;

        [Header("줌")]
        [SerializeField] private float _distance = 40f;
        [SerializeField] private float _minDistance = 12f;
        [SerializeField] private float _maxDistance = 90f;
        [Tooltip("휠 한 칸당 거리 변화 비율.")]
        [SerializeField] private float _zoomStep = 0.12f;
        [SerializeField] private float _zoomSmoothing = 12f;

        [Header("범위")]
        [Tooltip("초점이 기지 밖으로 나가지 않게 잡아 줄 지면. 없으면 제한하지 않는다.")]
        [SerializeField] private TerritoryGround _ground;

        [Tooltip("기지 경계 밖으로 허용할 여유 칸 수.")]
        [SerializeField] private float _panMarginCells = 4f;

        private Vector3 _focus;
        private float _targetDistance;

        private bool _dragging;
        private Vector3 _dragStartGroundPoint;
        private Vector3 _dragStartFocus;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _targetDistance = _distance;
            ApplyTransform();
        }

        private void OnValidate()
        {
            _minDistance = Mathf.Max(1f, _minDistance);
            _maxDistance = Mathf.Max(_minDistance + 1f, _maxDistance);
            _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
            _targetDistance = _distance;

            if (_camera == null)
                _camera = GetComponent<Camera>();

            ApplyTransform();
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();

            _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-_zoomSmoothing * Time.deltaTime));
            ApplyTransform();
        }

        private void HandleZoom()
        {
            float scroll = 0f;

            Mouse mouse = Mouse.current;
            if (mouse != null)
                scroll = mouse.scroll.ReadValue().y;

            // 두 손가락 핀치 — 손가락 간 거리 변화를 휠처럼 쓴다.
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.touches.Count >= 2)
            {
                var t0 = touch.touches[0];
                var t1 = touch.touches[1];

                if (t0.press.isPressed && t1.press.isPressed)
                {
                    Vector2 p0 = t0.position.ReadValue();
                    Vector2 p1 = t1.position.ReadValue();
                    Vector2 d0 = t0.delta.ReadValue();
                    Vector2 d1 = t1.delta.ReadValue();

                    float now = Vector2.Distance(p0, p1);
                    float before = Vector2.Distance(p0 - d0, p1 - d1);
                    scroll += (now - before) * 4f;
                }
            }

            if (Mathf.Approximately(scroll, 0f))
                return;

            // 비율로 줄인다 — 멀리서는 크게, 가까이서는 작게 움직여야 손에 붙는다.
            _targetDistance = Mathf.Clamp(
                _targetDistance * (1f - Mathf.Sign(scroll) * _zoomStep),
                _minDistance, _maxDistance);
        }

        private void HandlePan()
        {
            Vector2 screen;
            bool pressed = ReadPointer(out screen);

            if (!pressed)
            {
                _dragging = false;
                return;
            }

            Vector3 groundPoint;
            if (!TryGroundPoint(screen, out groundPoint))
                return;

            if (!_dragging)
            {
                _dragging = true;
                _dragStartGroundPoint = groundPoint;
                _dragStartFocus = _focus;
                return;
            }

            // 잡은 지점이 손가락에 붙어 따라오게 — 초점을 시작 지점과의 차이만큼 되민다.
            // 화면 델타를 그냥 쓰면 줌 배율에 따라 감도가 달라진다.
            Vector3 delta = _dragStartGroundPoint - groundPoint;
            _focus = ClampToBounds(_dragStartFocus + delta);
        }

        private static bool ReadPointer(out Vector2 screen)
        {
            screen = Vector2.zero;

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.touches.Count > 0)
            {
                var primary = touch.touches[0];

                // 두 손가락은 핀치로 처리하므로 팬에서 뺀다.
                if (primary.press.isPressed && touch.touches.Count < 2)
                {
                    screen = primary.position.ReadValue();
                    return true;
                }

                return false;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                screen = mouse.position.ReadValue();
                return true;
            }

            return false;
        }

        private bool TryGroundPoint(Vector2 screen, out Vector3 point)
        {
            point = Vector3.zero;

            if (_camera == null)
                return false;

            Ray ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);

            float distance;
            if (!plane.Raycast(ray, out distance))
                return false;

            point = ray.GetPoint(distance);
            return true;
        }

        private Vector3 ClampToBounds(Vector3 focus)
        {
            if (_ground == null)
                return focus;

            GridRect b = _ground.Bounds;
            float cell = _ground.Grid.CellSize;
            float margin = _panMarginCells * cell;

            float minX = (b.MinX - 0.5f) * cell - margin;
            float maxX = (b.MaxXExclusive - 0.5f) * cell + margin;
            float minZ = (b.MinY - 0.5f) * cell - margin;
            float maxZ = (b.MaxYExclusive - 0.5f) * cell + margin;

            focus.x = Mathf.Clamp(focus.x, minX, maxX);
            focus.z = Mathf.Clamp(focus.z, minZ, maxZ);
            focus.y = 0f;
            return focus;
        }

        private void ApplyTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.rotation = rotation;
            transform.position = _focus - rotation * Vector3.forward * _distance;
        }

        /// <summary>카메라를 그 칸으로 옮긴다. 폐허를 탭했을 때 가운데로 오게 하는 데 쓴다.</summary>
        public void FocusOn(Vector3 worldPoint)
        {
            _focus = ClampToBounds(new Vector3(worldPoint.x, 0f, worldPoint.z));
            ApplyTransform();
        }
    }
}

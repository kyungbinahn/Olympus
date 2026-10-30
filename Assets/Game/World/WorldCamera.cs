using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Olympus.Core.Grid;
using Olympus.Game.Territory;

namespace Olympus.Game.World
{
    /// <summary>
    /// 월드맵 카메라 — 수직으로 내려다보며 팬·줌 한다.
    ///
    /// 기지 카메라(<see cref="TerritoryCamera"/>)와 달리 기울이지 않고 직교 투영이다.
    /// 이유는 용도가 달라서다 — 기지는 건물을 "구경하는" 화면이라 45° 기울여야 형태가
    /// 읽히지만, 월드맵은 좌표를 읽고 위치를 재는 화면이라 기울면 거리 감각이 왜곡된다.
    /// 실제 SLG들도 기지는 비스듬히, 월드맵은 위에서 내려다본다.
    /// </summary>
    public sealed class WorldCamera : MonoBehaviour
    {
        [Header("줌")]
        [Tooltip("화면 세로 절반이 몇 칸을 담는가. 작을수록 확대된다.")]
        [SerializeField] private float _viewCells = 60f;
        [SerializeField] private float _minViewCells = 12f;
        [SerializeField] private float _maxViewCells = 320f;
        [SerializeField] private float _zoomStep = 0.12f;
        [SerializeField] private float _zoomSmoothing = 12f;

        [Header("범위")]
        [SerializeField] private WorldMapView _world;

        private Camera _camera;
        private float _targetViewCells;

        // 기지 카메라와 같은 계산식을 쓴다 — 두 화면의 조작감이 어긋나지 않게,
        // 그리고 같은 버그를 각자 다시 내지 않게(DragPan 주석 참고).
        private readonly DragPan _pan = new DragPan();
        private Vector2 _dragStartScreen;
        private Vector3 _focus;

        private readonly TouchControl[] _activeTouches = new TouchControl[2];

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _targetViewCells = _viewCells;

            // 처음에는 플레이어 기지를 화면 한가운데 둔다 — 월드에 들어왔을 때
            // "내가 어디 있나"부터 보여야 한다.
            if (_world != null)
                _focus = _world.CellCenterWorld(_world.PlayerCityCell());

            ApplyTransform();
        }

        private void Update()
        {
            HandleZoom();
            HandlePan();

            _viewCells = Mathf.Lerp(_viewCells, _targetViewCells, 1f - Mathf.Exp(-_zoomSmoothing * Time.deltaTime));
            ApplyTransform();
        }

        private void HandleZoom()
        {
            float scroll = 0f;

            Mouse mouse = Mouse.current;
            if (mouse != null)
                scroll = mouse.scroll.ReadValue().y;

            if (PointerGuard.CollectActiveTouches(_activeTouches) >= 2)
            {
                TouchControl t0 = _activeTouches[0];
                TouchControl t1 = _activeTouches[1];

                Vector2 p0 = t0.position.ReadValue();
                Vector2 p1 = t1.position.ReadValue();
                Vector2 d0 = t0.delta.ReadValue();
                Vector2 d1 = t1.delta.ReadValue();

                float now = Vector2.Distance(p0, p1);
                float before = Vector2.Distance(p0 - d0, p1 - d1);
                scroll += (now - before) * 4f;
            }

            if (Mathf.Approximately(scroll, 0f))
                return;

            _targetViewCells = Mathf.Clamp(
                _targetViewCells * (1f + Mathf.Sign(scroll) * -_zoomStep),
                _minViewCells, _maxViewCells);
        }

        private void HandlePan()
        {
            Vector2 screen;
            bool pressed = ReadPointer(out screen);

            if (!pressed || PointerGuard.IsOverUI())
            {
                _pan.End();
                return;
            }

            if (!_pan.IsDragging)
            {
                _dragStartScreen = screen;
                _pan.Begin(new WorldXZ(_focus.x, _focus.z));
                return;
            }

            Vector3 grabPoint;
            Vector3 pointerPoint;

            if (!TryGroundPoint(_dragStartScreen, out grabPoint)
                || !TryGroundPoint(screen, out pointerPoint))
                return;

            WorldXZ next = _pan.FocusFor(
                new WorldXZ(grabPoint.x, grabPoint.z),
                new WorldXZ(pointerPoint.x, pointerPoint.z));

            _focus = ClampToBounds(new Vector3(next.X, 0f, next.Z));
        }

        private bool ReadPointer(out Vector2 screen)
        {
            screen = Vector2.zero;

            if (Touchscreen.current != null)
            {
                if (PointerGuard.CollectActiveTouches(_activeTouches) != 1)
                    return false;

                screen = _activeTouches[0].position.ReadValue();
                return true;
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
            if (_world == null || _world.Map == null)
                return focus;

            GridRect b = _world.Map.Bounds;
            float cell = WorldMapView.CellSize;

            focus.x = Mathf.Clamp(focus.x, b.MinX * cell, b.MaxXExclusive * cell);
            focus.z = Mathf.Clamp(focus.z, b.MinY * cell, b.MaxYExclusive * cell);
            focus.y = 0f;
            return focus;
        }

        private void ApplyTransform()
        {
            if (_camera == null)
                return;

            _camera.orthographicSize = _viewCells * WorldMapView.CellSize;

            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.position = new Vector3(_focus.x, 100f, _focus.z);
        }

        /// <summary>그 칸이 화면 한가운데 오도록 옮긴다.</summary>
        public void FocusOn(GridPos cell)
        {
            if (_world == null)
                return;

            _focus = ClampToBounds(_world.CellCenterWorld(cell));
            ApplyTransform();
        }
    }
}

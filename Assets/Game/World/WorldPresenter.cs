using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Olympus.Core.Grid;
using Olympus.Core.World;
using Olympus.Game.Territory;

namespace Olympus.Game.World
{
    /// <summary>
    /// 월드맵의 탭 판정. 어떤 칸을 골랐는지 알리기만 하고, 화면에 무엇을 띄울지는
    /// HUD가 정한다.
    ///
    /// 탭·드래그 구분은 기지 화면과 같은 규칙이다(<see cref="TerritoryPresenter"/>) —
    /// 지도를 스크롤하고 손을 뗐을 뿐인데 칸이 선택되면 안 된다.
    /// </summary>
    public sealed class WorldPresenter : MonoBehaviour
    {
        private const float TapDragThresholdPx = 24f;

        [SerializeField] private WorldMapView _world;
        [SerializeField] private WorldCamera _camera;

        private bool _isPressing;
        private Vector2 _pressStartScreen;
        private bool _hasSelection;
        private GridPos _selected;

        /// <summary>선택이 바뀌면 알린다. 고른 칸이 없어지면 <c>false</c>가 함께 온다.</summary>
        public event System.Action<GridPos, bool> SelectionChanged;

        public WorldMapView World => _world;

        public bool HasSelection => _hasSelection;

        public GridPos Selected => _selected;

        private void Start()
        {
            // 월드에 들어오면 내 기지를 보고 시작한다.
            if (_camera != null && _world != null)
                _camera.FocusOn(_world.PlayerCityCell());
        }

        private void Update()
        {
            HandleTap();
        }

        private void HandleTap()
        {
            Vector2 screen;
            bool hasPointer = TryGetPointerPosition(out screen);

            if (hasPointer && WasPressed())
            {
                _isPressing = true;
                _pressStartScreen = screen;
            }

            if (!WasReleased())
                return;

            bool wasDrag = _isPressing && hasPointer
                && Vector2.Distance(_pressStartScreen, screen) > TapDragThresholdPx;
            _isPressing = false;

            if (wasDrag || !hasPointer)
                return;

            if (PointerGuard.IsOverUI())
                return;

            GridPos cell;
            if (_world == null || !_world.TryPickCell(screen, out cell))
            {
                Clear();
                return;
            }

            _selected = cell;
            _hasSelection = true;

            var handler = SelectionChanged;
            if (handler != null)
                handler(_selected, true);
        }

        private void Clear()
        {
            if (!_hasSelection)
                return;

            _hasSelection = false;

            var handler = SelectionChanged;
            if (handler != null)
                handler(_selected, false);
        }

        /// <summary>고른 칸이 내 기지인가 — HUD가 "기지로 들어가기"를 띄울지 정할 때 쓴다.</summary>
        public bool SelectedIsPlayerCity()
        {
            if (!_hasSelection || _world == null || _world.Map == null)
                return false;

            WorldSite site;
            return _world.Map.TryGetSite(_selected, out site)
                && site.Kind == WorldSiteKind.PlayerCity;
        }

        private static bool WasPressed()
        {
            TouchControl active;
            if (PointerGuard.TryGetActiveTouch(out active))
                return active.press.wasPressedThisFrame;

            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private static bool WasReleased()
        {
            TouchControl active;
            if (PointerGuard.TryGetActiveTouch(out active))
                return active.press.wasReleasedThisFrame;

            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasReleasedThisFrame;
        }

        private static bool TryGetPointerPosition(out Vector2 screen)
        {
            screen = Vector2.zero;

            TouchControl active;
            if (PointerGuard.TryGetActiveTouch(out active))
            {
                screen = active.position.ReadValue();
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return false;

            screen = mouse.position.ReadValue();
            return true;
        }
    }
}

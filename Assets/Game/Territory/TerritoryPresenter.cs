using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 기지 화면을 조립하는 유일한 지점 — 데이터를 읽고, 로직을 세우고, 뷰를 붙인다.
    ///
    /// 로직(Core)은 여기서 만든 인스턴스를 쓰고, 뷰는 <see cref="StateStore.Changed"/>를
    /// 구독해 따라온다. 뷰가 상태를 직접 고치는 경로는 없다 — 건물·자원의 setter가
    /// internal이라 컴파일 단계에서 막힌다.
    /// </summary>
    public sealed class TerritoryPresenter : MonoBehaviour
    {
        [Header("데이터")]
        [Tooltip("Assets/Data/building-defs.json")]
        [SerializeField] private TextAsset _buildingDefs;

        [Tooltip("Assets/Data/base-layout.json")]
        [SerializeField] private TextAsset _baseLayout;

        [Tooltip("Assets/Data/strings-ko.json — HUD가 보여줄 화면 문구.")]
        [SerializeField] private TextAsset _strings;

        [Header("뷰")]
        [SerializeField] private TerritoryGround _ground;
        [SerializeField] private Material _ruinedMaterial;
        [SerializeField] private Material _constructingMaterial;
        [SerializeField] private Material _completeMaterial;
        [SerializeField] private Material _selectionMaterial;

        [Header("그레이박스 높이")]
        [SerializeField] private float _ruinedHeight = 0.4f;
        [SerializeField] private float _constructingHeight = 0.9f;
        [SerializeField] private float _completeHeight = 2.2f;

        [Tooltip("칸 대비 건물이 차지하는 비율. 1보다 작게 두면 사이에 틈이 보인다.")]
        [SerializeField, Range(0.5f, 1f)] private float _footprintInset = 0.9f;

        [Header("복구")]
        [Tooltip("복구된 건물에서 녹지가 번지는 반경(칸).\n\n" +
                 "플레이 중에 이 값을 돌리면 바로 반영된다 — 눈으로 정하는 값이다.\n" +
                 "반경이 작으면 녹지가 건물에 전부 가려져 틈만 초록으로 보인다 " +
                 "(건물 사이 여백이 1칸이다).")]
        [SerializeField, Range(0f, 12f)] private float _restorationRadius = 4f;

        [Header("시작 자원")]
        [SerializeField] private long _startingWood = 2000;
        [SerializeField] private long _startingStone = 2000;

        [Header("디버그")]
        [Tooltip("플레이 중 단축키를 켠다. 게임 규칙이 아니라 화면을 판단하기 위한 것이다.\n\n" +
                 "F — 진행 중인 공사를 전부 즉시 완료\n" +
                 "R — 모든 폐허를 비용 없이 즉시 복구 (최종 화면 확인용)")]
        [SerializeField] private bool _debugHotkeys = true;

        private BuildingCatalog _catalog;
        private TerritoryRuntime _runtime;
        private IClock _clock;
        private LocalizedStrings _stringsTable;

        private readonly Dictionary<int, Transform> _visuals = new Dictionary<int, Transform>();
        private readonly Dictionary<int, MeshRenderer> _renderers = new Dictionary<int, MeshRenderer>();

        private Camera _camera;
        private Transform _visualRoot;
        private MeshFilter _selectionFilter;
        private Mesh _selectionMesh;
        private int _selectedSlotId = -1;

        public TerritoryRuntime Runtime => _runtime;
        public int SelectedSlotId => _selectedSlotId;
        public LocalizedStrings Strings => _stringsTable;

        /// <summary>선택이 바뀔 때마다 알린다. HUD가 이걸 듣고 패널을 갱신한다.</summary>
        public event System.Action<int> SelectionChanged;

        private void Awake()
        {
            _camera = Camera.main;
            _clock = new SystemClock();

            _catalog = TerritoryDataLoader.LoadCatalog(_buildingDefs);
            _stringsTable = LocalizedStrings.Load(_strings);
            BaseLayoutFile layoutFile = TerritoryDataLoader.LoadLayoutFile(_baseLayout);

            VerifyBaseSizeMatches(layoutFile);

            var state = new GameState(_ground.Grid, _ground.Bounds);
            _runtime = new TerritoryRuntime(state, _catalog, _clock, _restorationRadius);

            // 지면은 로직의 복구도 지도를 받아서 그리기만 한다 — 뷰가 자기 것을 따로
            // 만들면 두 인스턴스가 갈라져 "로직은 복구했는데 화면은 황폐"가 된다.
            _ground.Restoration = _runtime.Restoration;

            _runtime.Store.Apply(new StateDelta()
                .WithResource(ResourceChange.Absolute(ResourceKind.Wood, _startingWood))
                .WithResource(ResourceChange.Absolute(ResourceKind.Stone, _startingStone)));

            BuildVisualRoot();
            _runtime.Store.Changed += OnStateChanged;

            // 자리를 전부 폐허로 세운다. 레이아웃이 잘못되면 어느 자리가 문제인지
            // 말하며 여기서 터진다 — 화면에서 겹쳐 보이는 것으로 나중에 알게 되는 것보다 낫다.
            _runtime.Initialize(TerritoryDataLoader.ToLayout(layoutFile, _ground.Bounds));

            RefreshAllVisuals();

            Debug.Log(
                "기지 준비 완료 — 자리 " + _runtime.State.BuildingCount + "개, " +
                "건물 정의 " + _catalog.Count + "종, 격자 " + _ground.Bounds +
                "\n폐허를 클릭하면 정보가 뜨고, 자원이 충분하면 복구가 시작됩니다.");
        }

        /// <summary>
        /// 지면이 그리는 범위와 레이아웃이 가정한 범위가 같은지 본다.
        /// 어긋나면 "보이는 땅과 놓이는 자리가 다르다"가 되고, 그건 조용히 틀린다.
        /// </summary>
        private void VerifyBaseSizeMatches(BaseLayoutFile layoutFile)
        {
            if (layoutFile.baseSize == _ground.BaseSize)
                return;

            throw new System.InvalidOperationException(
                "기지 크기가 어긋난다 — 레이아웃 파일은 " + layoutFile.baseSize +
                ", 지면 컴포넌트는 " + _ground.BaseSize + "이다. " +
                "둘 중 하나를 고쳐 맞춰야 한다(같은 값이어야 보이는 땅과 자리가 일치한다).");
        }

        private void BuildVisualRoot()
        {
            var root = new GameObject("Buildings");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;

            var selection = new GameObject("Selection");
            selection.transform.SetParent(transform, false);
            _selectionFilter = selection.AddComponent<MeshFilter>();

            MeshRenderer r = selection.AddComponent<MeshRenderer>();
            r.sharedMaterial = _selectionMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void Update()
        {
            // 인스펙터에서 반경을 돌리면 즉시 반영한다. float 비교 하나라 값이 싸다.
            _runtime.SetRestorationRadius(_restorationRadius);

            // 공사 완료 판정. "끝나는 시각 <= 지금" 비교라 프레임을 놓쳐도 결과가 같다.
            // 녹지 재계산은 런타임이 상태 통지에 묶어 두었으므로 여기서 부르지 않는다.
            _runtime.Tick();

            HandleDebugHotkeys();
            HandleTap();
        }

        private void HandleDebugHotkeys()
        {
            if (!_debugHotkeys)
                return;

            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.fKey.wasPressedThisFrame)
            {
                int done = _runtime.DebugCompleteAllConstruction();
                Debug.Log("[디버그] 공사 " + done + "건을 즉시 완료했습니다.");
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                int done = _runtime.DebugRestoreAll();
                Debug.Log("[디버그] 폐허 " + done + "곳을 즉시 복구했습니다. " +
                          "녹지 " + _runtime.Restoration.RestoredCellCount + "칸 (" +
                          (_runtime.Restoration.RestoredFraction * 100f).ToString("0.0") + "%).");
            }
        }

        private void HandleTap()
        {
            if (!WasTapped())
                return;

            if (PointerGuard.IsOverUI())
                return;

            Vector2 screen;
            if (!TryGetPointerPosition(out screen))
                return;

            GridPos cell;
            if (!_ground.TryPickCell(_camera, screen, out cell))
                return;

            BuildingInstance building;
            if (!_runtime.State.TryGetBuildingAt(cell, out building))
            {
                Select(-1);
                Debug.Log("빈 땅 " + cell);
                return;
            }

            Select(building.Id);
        }

        /// <summary>
        /// 선택된 자리를 HUD가 보여줄 형태로 요약한다. 상태를 바꾸지 않는다 —
        /// 패널이 매 프레임 다시 불러도 안전해야 한다.
        /// </summary>
        public bool TryGetStatus(int slotId, out BuildingStatusView view)
        {
            view = default;

            BuildingInstance b;
            if (slotId < 0 || !_runtime.State.TryGetBuilding(slotId, out b))
                return false;

            BuildingDef def;
            if (!_catalog.TryGet(b.DefId, out def))
                return false;

            view = BuildingStatusView.Describe(b, def, _runtime.State.Resources, _clock.NowUnixMs);
            return true;
        }

        /// <summary>
        /// 지금 선택된 자리의 복구를 시도한다. 복구 버튼이 이걸 부른다 —
        /// 탭만으로는 더 이상 복구가 시작되지 않는다(HUD가 비용을 보여주고 확인을 받는다).
        /// </summary>
        public BuildResult TryRepairSelected()
        {
            if (_selectedSlotId < 0)
                return BuildResult.Fail(BuildRejection.UnknownSlot);

            return _runtime.Construction.TryStartRepair(_selectedSlotId);
        }

        public static string DescribeRejection(BuildRejection r)
        {
            switch (r)
            {
                case BuildRejection.InsufficientResources: return "자원이 부족합니다";
                case BuildRejection.NotRuined: return "이미 복구된 자리입니다";
                case BuildRejection.UnknownSlot: return "없는 자리입니다";
                case BuildRejection.UnknownDef: return "건물 정의를 찾을 수 없습니다(데이터 오류)";
                default: return r.ToString();
            }
        }

        private static bool WasTapped()
        {
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.touches.Count > 0)
                return touch.touches[0].press.wasReleasedThisFrame;

            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasReleasedThisFrame;
        }

        private static bool TryGetPointerPosition(out Vector2 screen)
        {
            screen = Vector2.zero;

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.touches.Count > 0)
            {
                screen = touch.touches[0].position.ReadValue();
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return false;

            screen = mouse.position.ReadValue();
            return true;
        }

        private void OnStateChanged(StateDelta delta)
        {
            if (delta.Buildings == null)
                return;

            for (int i = 0; i < delta.Buildings.Count; i++)
            {
                BuildingChange c = delta.Buildings[i];

                if (c.Op == ChangeOp.Remove)
                {
                    RemoveVisual(c.Id);
                    continue;
                }

                BuildingInstance b;
                if (_runtime.State.TryGetBuilding(c.Id, out b))
                    RefreshVisual(b);
            }
        }

        private void RefreshAllVisuals()
        {
            foreach (BuildingInstance b in _runtime.State.Buildings)
            {
                RefreshVisual(b);
            }
        }

        private void RefreshVisual(BuildingInstance b)
        {
            BuildingDef def;
            if (!_catalog.TryGet(b.DefId, out def))
                return;

            Transform t;
            if (!_visuals.TryGetValue(b.Id, out t))
            {
                // 그레이박스는 원시 큐브로 세운다. 실제 모델(D:\그릭로만의 .glb)이
                // 들어오면 여기만 프리팹 인스턴스화로 바꾸면 된다.
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Slot" + b.Id + "_" + b.DefId;
                go.transform.SetParent(_visualRoot, false);

                // 콜라이더는 쓰지 않는다 — 탭 판정은 지면 평면 교차로 한다.
                Object.Destroy(go.GetComponent<Collider>());

                t = go.transform;
                _visuals[b.Id] = t;
                _renderers[b.Id] = go.GetComponent<MeshRenderer>();
            }

            float height = HeightFor(b.Phase);
            float cell = _ground.Grid.CellSize;

            t.localScale = new Vector3(
                def.Footprint.Width * cell * _footprintInset,
                height,
                def.Footprint.Height * cell * _footprintInset);

            // 큐브는 중심 기준이므로 절반 높이만큼 올려 지면에 얹는다.
            Vector3 center = _ground.FootprintCenterWorld(b.Anchor, def.Footprint, height * 0.5f);
            t.position = center;

            _renderers[b.Id].sharedMaterial = MaterialFor(b.Phase);
        }

        private float HeightFor(BuildingPhase phase)
        {
            switch (phase)
            {
                case BuildingPhase.Ruined: return _ruinedHeight;
                case BuildingPhase.Constructing: return _constructingHeight;
                default: return _completeHeight;
            }
        }

        private Material MaterialFor(BuildingPhase phase)
        {
            switch (phase)
            {
                case BuildingPhase.Ruined: return _ruinedMaterial;
                case BuildingPhase.Constructing: return _constructingMaterial;
                default: return _completeMaterial;
            }
        }

        private void RemoveVisual(int id)
        {
            Transform t;
            if (!_visuals.TryGetValue(id, out t))
                return;

            if (t != null)
                Object.Destroy(t.gameObject);

            _visuals.Remove(id);
            _renderers.Remove(id);
        }

        private void Select(int slotId)
        {
            if (_selectedSlotId == slotId)
                return;

            _selectedSlotId = slotId;

            if (_selectionMesh != null)
            {
                Destroy(_selectionMesh);
                _selectionMesh = null;
            }

            BuildingInstance b;
            BuildingDef def = null;

            if (slotId >= 0
                && _runtime.State.TryGetBuilding(slotId, out b)
                && _catalog.TryGet(b.DefId, out def))
            {
                var cells = new List<GridPos>(def.Footprint.CellsAt(b.Anchor));
                _selectionMesh = GroundMeshBuilder.BuildCellOverlay(cells, _ground.Grid, 0.03f, "Selection");
            }

            _selectionFilter.sharedMesh = _selectionMesh;

            SelectionChanged?.Invoke(slotId);
        }

        private void OnDestroy()
        {
            if (_runtime != null)
            {
                _runtime.Store.Changed -= OnStateChanged;
                _runtime.Dispose();
            }

            if (_selectionMesh != null)
                Destroy(_selectionMesh);
        }
    }
}

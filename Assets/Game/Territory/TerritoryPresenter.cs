using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Olympus.Core.Grid;
using Olympus.Core.Save;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Core.Time;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 건물 한 종류에 붙일 실제 모델. <c>defId</c>로 잇는다 —
    /// 아트 쪽 파일 이름(artCode)이 아니라 게임 데이터의 키로 묶어야
    /// 아트 파일을 갈아끼워도 배선이 안 끊긴다.
    /// </summary>
    [System.Serializable]
    public sealed class BuildingModelEntry
    {
        public string defId;
        public GameObject model;

        /// <summary>
        /// 이 건물에 입힐 머티리얼.
        ///
        /// FBX가 들고 오는 머티리얼을 쓰지 않고 따로 만들어 붙이는 이유 —
        /// Unity의 FBX 머티리얼 임포트에 두 번 기댔다가 두 번 다 텍스처가 안 붙었다
        /// (2026-09-14·16). 임포터가 무엇을 만들어 주느냐에 기대지 않고 우리가 만든
        /// 에셋을 직접 입히면 그 부류의 실패가 없다. 머티리얼은 눈에 보이는 .mat 파일이라
        /// 손으로 열어 볼 수도 있다.
        /// </summary>
        public Material material;

        [Tooltip("이 건물만 따로 돌려야 할 때 쓰는 각도(도). 아트마다 정면이 다를 수 있다.")]
        public float yawOffset;
    }

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

        [Header("건물 모델")]
        [Tooltip("실제 3D 모델. 여기 없는 건물은 그레이박스 큐브로 남는다 — 한 채씩 바꿔 갈 수 있다.\n\n" +
                 "완성 상태에서만 쓴다. 폐허·공사 중은 아직 전용 모델이 없어서 큐브 그대로다.")]
        [SerializeField] private List<BuildingModelEntry> _buildingModels = new List<BuildingModelEntry>();

        [Tooltip("모델 전체를 돌리는 각도(도). 플레이 중에 돌리면 바로 반영된다 — 눈으로 정하는 값이다.\n\n" +
                 "아트가 어느 쪽을 정면으로 만들었는지, 카메라(yaw 45°)에 어떻게 맞출지가 " +
                 "여기서 정해진다. 건물마다 다르면 각 항목의 yawOffset을 쓴다.")]
        [SerializeField, Range(-180f, 180f)] private float _modelYaw;

        private float _appliedModelYaw;

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
        [Tooltip("저장된 진행이 없을 때만 쓴다.")]
        [SerializeField] private long _startingWood = 2000;
        [SerializeField] private long _startingStone = 2000;

        [Header("저장")]
        [Tooltip("기기에 진행을 저장한다. 끄면 켤 때마다 처음부터 시작한다 — 밸런스를 " +
                 "초반부터 반복해서 볼 때 쓴다.\n\n" +
                 "저장 위치는 콘솔 로그에 찍힌다. 지우려면 메뉴의 Olympus/디버그/저장 파일 지우기.")]
        [SerializeField] private bool _useSaveFile = true;

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

        /// <summary>실제 모델로 세운 자리. 큐브와 갱신 방식이 달라 구분해 둔다.</summary>
        private readonly HashSet<int> _modelVisuals = new HashSet<int>();

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

            // 월드에 갔다 왔으면 진행이 남아 있다 — 상태는 세션이 들고 있고, 화면만 다시 붙인다.
            bool resumed = TerritorySession.HasRuntime;

            if (resumed)
            {
                _runtime = TerritorySession.Runtime;

                // 카탈로그·시계도 세션 것을 쓴다 — 같은 데이터로 두 인스턴스를 만들면
                // 언젠가 갈라지고, 갈라진 쪽이 먼저 죽는다.
                _catalog = _runtime.Catalog;
                _clock = _runtime.Clock;
                _runtime.SetRestorationRadius(_restorationRadius);
            }
            else
            {
                var state = new GameState(_ground.Grid, _ground.Bounds);
                _runtime = new TerritoryRuntime(state, _catalog, _clock, _restorationRadius);
                TerritorySession.Adopt(_runtime);
            }

            // 지면은 로직의 복구도 지도를 받아서 그리기만 한다 — 뷰가 자기 것을 따로
            // 만들면 두 인스턴스가 갈라져 "로직은 복구했는데 화면은 황폐"가 된다.
            _ground.Restoration = _runtime.Restoration;

            BuildVisualRoot();
            _runtime.Store.Changed += OnStateChanged;

            if (!resumed)
                LoadOrStartFresh(layoutFile);

            RefreshAllVisuals();

            Debug.Log(
                (resumed ? "기지로 돌아왔습니다 — 진행 유지" : "기지 준비 완료")
                + " — 자리 " + _runtime.State.BuildingCount + "개, "
                + "건물 정의 " + _catalog.Count + "종, 격자 " + _ground.Bounds);
        }

        /// <summary>
        /// 저장된 진행이 있으면 그걸로, 없으면 처음부터 세운다.
        ///
        /// 레이아웃이 잘못되면 어느 자리가 문제인지 말하며 여기서 터진다 —
        /// 화면에서 겹쳐 보이는 것으로 나중에 알게 되는 것보다 낫다.
        /// </summary>
        private void LoadOrStartFresh(BaseLayoutFile layoutFile)
        {
            BaseLayout layout = TerritoryDataLoader.ToLayout(layoutFile, _ground.Bounds);

            SaveFile save = null;
            bool hasSave = _useSaveFile && SaveStore.TryLoad(out save);

            if (!hasSave)
            {
                _runtime.Store.Apply(new StateDelta()
                    .WithResource(ResourceChange.Absolute(ResourceKind.Wood, _startingWood))
                    .WithResource(ResourceChange.Absolute(ResourceKind.Stone, _startingStone)));

                _runtime.Initialize(layout);
                return;
            }

            SaveLoadReport report = _runtime.InitializeFromSave(layout, save);

            // 어긋난 게 있으면 조용히 넘기지 않는다 — 데이터를 바꾼 직후라면 그게 이유다.
            if (report.HasMismatch)
                Debug.Log("세이브와 데이터가 일부 달랐습니다 — " + report);
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
            ApplyModelYawIfChanged();

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

        /// <summary>
        /// 탭과 드래그를 가르는 최대 이동 거리(픽셀). 이보다 많이 움직였으면 드래그 끝에
        /// 손을 뗀 것이지 탭이 아니다 — 없으면 지도를 스크롤할 때마다 놓은 자리의 건물이
        /// 마음대로 선택된다(2026-09-11, 스크롤 중 정보 패널이 계속 바뀌는 것으로 발견).
        /// </summary>
        private const float TapDragThresholdPx = 24f;

        private bool _isPressing;
        private Vector2 _pressStartScreen;

        private void HandleTap()
        {
            Vector2 screen;
            bool hasPointer = TryGetPointerPosition(out screen);

            if (hasPointer && WasPressed())
            {
                _isPressing = true;
                _pressStartScreen = screen;
            }

            if (!WasTapped())
                return;

            bool wasDrag = _isPressing && hasPointer
                && Vector2.Distance(_pressStartScreen, screen) > TapDragThresholdPx;
            _isPressing = false;

            // 드래그 끝에 놓은 것이다 — 탭으로 취급하지 않는다.
            if (wasDrag || !hasPointer)
                return;

            if (PointerGuard.IsOverUI())
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

        private static bool WasPressed()
        {
            TouchControl active;
            if (PointerGuard.TryGetActiveTouch(out active))
                return active.press.wasPressedThisFrame;

            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private static bool WasTapped()
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

            // 완성된 자리에만 실제 모델을 쓴다 — 폐허·공사 중은 전용 모델이 아직 없고,
            // 큐브의 높이·색이 그 단계를 읽어 주는 역할을 계속 해야 한다.
            BuildingModelEntry entry = b.Phase == BuildingPhase.Complete ? EntryFor(b.DefId) : null;
            bool wantsModel = entry != null;

            Transform t;
            bool has = _visuals.TryGetValue(b.Id, out t);

            // 큐브 ↔ 모델로 종류가 바뀌면 갈아엎는다. 안 그러면 완성 순간에 큐브가 남는다.
            if (has && _modelVisuals.Contains(b.Id) != wantsModel)
            {
                RemoveVisual(b.Id);
                has = false;
            }

            if (!has)
                t = wantsModel ? CreateModelVisual(b, entry) : CreateCubeVisual(b);

            if (wantsModel)
                PlaceModel(t, b, def);
            else
                PlaceCube(t, b, def);
        }

        private Transform CreateCubeVisual(BuildingInstance b)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Slot" + b.Id + "_" + b.DefId;
            go.transform.SetParent(_visualRoot, false);

            // 콜라이더는 쓰지 않는다 — 탭 판정은 지면 평면 교차로 한다.
            Object.Destroy(go.GetComponent<Collider>());

            _visuals[b.Id] = go.transform;
            _renderers[b.Id] = go.GetComponent<MeshRenderer>();
            return go.transform;
        }

        /// <summary>
        /// 모델을 래퍼 안에 넣는다. 위치·배율은 래퍼만 건드리고 모델 자신의 변환은 그대로 둔다.
        ///
        /// ⚠️ 모델의 회전을 초기화하면 안 된다. FBX 임포터가 축(Blender Z-up → Unity Y-up)을
        /// 맞추려고 루트에 -90° X 회전을 넣어 두는데, 그걸 지우면 건물이 뒤로 눕는다.
        /// 실제로 밟았다(2026-09-11 — 본부가 하늘을 보고 누웠다).
        /// </summary>
        private Transform CreateModelVisual(BuildingInstance b, BuildingModelEntry entry)
        {
            var wrapper = new GameObject("Slot" + b.Id + "_" + b.DefId);
            wrapper.transform.SetParent(_visualRoot, false);

            GameObject instance = Instantiate(entry.model, wrapper.transform);

            // 머티리얼을 직접 입힌다 — FBX가 들고 온 것을 쓰지 않는다(BuildingModelEntry 주석 참고).
            if (entry.material != null)
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].sharedMaterial = entry.material;
                }
            }

            _visuals[b.Id] = wrapper.transform;
            _modelVisuals.Add(b.Id);
            return wrapper.transform;
        }

        private void PlaceCube(Transform t, BuildingInstance b, BuildingDef def)
        {
            float height = HeightFor(b.Phase);
            float cell = _ground.Grid.CellSize;

            t.localScale = new Vector3(
                def.Footprint.Width * cell * _footprintInset,
                height,
                def.Footprint.Height * cell * _footprintInset);

            // 큐브는 중심 기준이므로 절반 높이만큼 올려 지면에 얹는다.
            t.position = _ground.FootprintCenterWorld(b.Anchor, def.Footprint, height * 0.5f);

            MeshRenderer r;
            if (_renderers.TryGetValue(b.Id, out r) && r != null)
                r.sharedMaterial = MaterialFor(b.Phase);
        }

        /// <summary>
        /// 모델을 자리 크기에 맞춰 앉힌다.
        ///
        /// 아트가 1x1x1로 정규화돼 나오지만 그걸 믿고 상수를 곱하지 않는다 — 실제 경계를
        /// 재서 맞춘다. 한 채라도 다른 스케일로 나오면 그 한 채만 조용히 어긋나기 때문이다.
        /// 비율은 유지한다(균등 배율) — 늘려 맞추면 실루엣이 망가진다.
        /// </summary>
        private void PlaceModel(Transform t, BuildingInstance b, BuildingDef def)
        {
            // 래퍼만 움직인다 — 모델 자신의 회전은 건드리지 않는다(CreateModelVisual 주석 참고).
            // 래퍼를 돌리면 모델의 축 보정은 그대로 둔 채 정면 방향만 맞출 수 있다.
            t.localScale = Vector3.one;
            t.position = Vector3.zero;
            t.rotation = Quaternion.Euler(0f, _modelYaw + YawOffsetFor(b.DefId), 0f);

            Bounds bounds;
            if (!TryGetRendererBounds(t, out bounds))
                return;

            float cell = _ground.Grid.CellSize;
            float targetX = def.Footprint.Width * cell * _footprintInset;
            float targetZ = def.Footprint.Height * cell * _footprintInset;

            float scaleX = bounds.size.x > 0.0001f ? targetX / bounds.size.x : 1f;
            float scaleZ = bounds.size.z > 0.0001f ? targetZ / bounds.size.z : 1f;
            float scale = Mathf.Min(scaleX, scaleZ);

            t.localScale = Vector3.one * scale;

            // 피벗이 바닥이 아니라 경계의 중심이다 — 잰 경계를 기준으로 바닥을 지면에 맞춘다.
            Vector3 center = _ground.FootprintCenterWorld(b.Anchor, def.Footprint, 0f);
            t.position = center - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        }

        /// <summary>스케일 1일 때의 경계. 모델 밑의 렌더러를 전부 합친다.</summary>
        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // 월드 경계를 루트 기준으로 되돌린다(위에서 위치·배율을 1로 초기화해 뒀다).
            bounds.center -= root.position;
            return true;
        }

        private BuildingModelEntry EntryFor(string defId)
        {
            for (int i = 0; i < _buildingModels.Count; i++)
            {
                BuildingModelEntry e = _buildingModels[i];
                if (e != null && e.model != null && e.defId == defId)
                    return e;
            }

            return null;
        }

        private float YawOffsetFor(string defId)
        {
            for (int i = 0; i < _buildingModels.Count; i++)
            {
                BuildingModelEntry e = _buildingModels[i];
                if (e != null && e.defId == defId)
                    return e.yawOffset;
            }

            return 0f;
        }

        /// <summary>
        /// 인스펙터에서 각도를 돌리면 즉시 다시 앉힌다. 녹지 반경 슬라이더와 같은 이유로
        /// 열어 둔다 — 이 값은 화면을 보면서 정해야 한다.
        /// </summary>
        private void ApplyModelYawIfChanged()
        {
            if (Mathf.Approximately(_appliedModelYaw, _modelYaw))
                return;

            _appliedModelYaw = _modelYaw;

            foreach (BuildingInstance b in _runtime.State.Buildings)
            {
                if (_modelVisuals.Contains(b.Id))
                    RefreshVisual(b);
            }
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
            _modelVisuals.Remove(id);
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

        /// <summary>
        /// 지금 진행을 기기에 쓴다.
        ///
        /// 부르는 시점이 셋인 이유 — 안드로이드에서는 <c>OnApplicationQuit</c>이 안 불릴 수
        /// 있다(홈 버튼으로 나간 뒤 시스템이 앱을 정리하는 경우). 백그라운드로 내려가는
        /// <c>OnApplicationPause(true)</c>가 가장 믿을 만한 자리고, 화면 전환은
        /// <c>OnDestroy</c>가 받는다.
        /// </summary>
        private void WriteSave()
        {
            if (!_useSaveFile || _runtime == null)
                return;

            SaveStore.Save(_runtime.CaptureSave());
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                WriteSave();
        }

        private void OnApplicationQuit()
        {
            WriteSave();
        }

        private void OnDestroy()
        {
            // 화면을 떠난다 — 월드로 가는 길일 수도 있고 앱이 닫히는 중일 수도 있다.
            WriteSave();

            // 내 구독만 끊는다.
            //
            // ⚠️ _runtime.Dispose()를 부르면 안 된다. 런타임은 세션이 들고 있고 씬보다
            //    오래 산다 — 여기서 Dispose하면 런타임이 상태 통지에 걸어 둔 녹지 재계산이
            //    끊겨서, 월드에 갔다 온 뒤로는 복구해도 녹지가 안 뜬다.
            if (_runtime != null)
                _runtime.Store.Changed -= OnStateChanged;

            if (_selectionMesh != null)
                Destroy(_selectionMesh);
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.Editor
{
    /// <summary>
    /// 기지 저작 창 — 씬에 세운 기지를 그대로 데이터로 저장한다.
    ///
    /// **씬에 보이는 것이 곧 기지다.** 건물을 옮기면 그 칸이 기지 배치가 되고,
    /// 돌리고 키우면 그게 그 건물의 아트 보정이 된다. 게임을 켜면 여기서 본 그대로 나온다.
    ///
    /// 저장하는 곳이 둘인 이유 — 성격이 다르기 때문이다:
    /// - 어느 칸에 서는가 → `base-layout.json` (기획 데이터)
    /// - 어느 각도로 얼마나 크게 → `building-art.json` (아트 보정)
    /// 둘 다 **게임이 읽는 바로 그 파일**이다. 툴 산출물과 런타임 입력이 갈라지면
    /// 갈라진 쪽이 먼저 죽는다(뉴포리아의 위치 저작 씬이 그렇게 죽었다).
    ///
    /// 저장하면 건물이 칸에 딱 맞게 **붙는 것이 눈에 보인다** — 그게 게임에 뜰 자리다.
    /// </summary>
    public sealed class BuildingArtWindow : EditorWindow
    {
        [System.Serializable]
        private sealed class Row
        {
            public int slotId;
            public string defId;
            public string footprint;
            public string cell;
            public float yaw;
            public float scale = 1f;
            public Vector3 offset;
        }

        private const float MinScale = 0.2f;
        private const float MaxScale = 4f;

        [SerializeField] private List<Row> _rows = new List<Row>();
        [SerializeField] private Vector2 _scroll;

        private List<LineupSlot> _slots;
        private TerritoryPresenter _presenter;
        private TerritoryGround _ground;
        private BuildingCatalog _catalog;

        public static void Open()
        {
            BuildingArtWindow window = GetWindow<BuildingArtWindow>("기지 저작");
            window.minSize = new Vector2(680f, 320f);
            window.EnsureSlots();
        }

        private void OnEnable()
        {
            TryFindScene();
        }

        /// <summary>창으로 돌아오면 씬에서 손으로 바꾼 것을 숫자에 반영한다.</summary>
        private void OnFocus()
        {
            SyncFromScene();
        }

        private void OnGUI()
        {
            // 플레이 중에는 아무것도 하지 않는다. 이 창은 씬의 건물을 계속 만지는데,
            // 게임이 돌아가는 동안 그러면 서로 부딪치고 에디터가 멈춘 것처럼 느려진다
            // (2026-10-14에 플레이를 누르자 멈췄다).
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "게임이 실행 중입니다.\n기지 저작은 플레이를 멈춘 뒤에 이어서 하세요.",
                    MessageType.Info);
                return;
            }

            if (_presenter == null || _ground == null)
            {
                TryFindScene();

                if (_presenter == null || _ground == null)
                {
                    EditorGUILayout.HelpBox(
                        "열린 씬에서 기지를 찾지 못했습니다.\n" +
                        "Assets/Scenes/Territory.unity 를 열거나 " +
                        "메뉴의 Olympus/Setup/기지 씬 만들기 를 실행하세요.",
                        MessageType.Warning);

                    if (GUILayout.Button("다시 찾기"))
                        EnsureSlots();

                    return;
                }
            }

            DrawToolbar();

            if (BuildingArtEditor.FindLineupRoot() == null)
            {
                EditorGUILayout.HelpBox(
                    "[기지 세우기]를 누르면 지금 저장된 배치대로 건물이 섭니다.\n" +
                    "그다음 씬에서 건물을 옮기고·돌리고·키운 뒤 [저장]을 누르면 그대로 게임에 반영됩니다.",
                    MessageType.Info);
            }

            DrawRows();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("기지 세우기", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                Lineup();

            if (GUILayout.Button("치우기", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                Clear();

            if (GUILayout.Button("씬에서 읽기", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                SyncFromScene();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("파일에서 되돌리기", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                RevertFromFile();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRows()
        {
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("건물", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
            GUILayout.Label("칸", EditorStyles.miniBoldLabel, GUILayout.Width(34f));
            GUILayout.Label("자리", EditorStyles.miniBoldLabel, GUILayout.Width(64f));
            GUILayout.Label("각도", EditorStyles.miniBoldLabel, GUILayout.Width(148f));
            GUILayout.Label("크기 (1 = 칸에 꽉 참)", EditorStyles.miniBoldLabel, GUILayout.Width(146f));
            GUILayout.Label("칸 안에서 밀기 X / Y / Z", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _rows.Count; i++)
            {
                DrawRow(_rows[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(Row row)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(row.defId, EditorStyles.label, GUILayout.Width(120f)))
                SelectInScene(row);

            GUILayout.Label(row.footprint, EditorStyles.miniLabel, GUILayout.Width(34f));
            GUILayout.Label(row.cell, EditorStyles.miniLabel, GUILayout.Width(64f));

            EditorGUI.BeginChangeCheck();

            row.yaw = GUILayout.HorizontalSlider(row.yaw, -180f, 180f, GUILayout.Width(80f));
            row.yaw = EditorGUILayout.FloatField(row.yaw, GUILayout.Width(44f));

            if (GUILayout.Button("↺", GUILayout.Width(22f)))
                row.yaw = Mathf.Repeat(row.yaw + 270f, 360f) - 180f;

            GUILayout.Space(6f);

            row.scale = GUILayout.HorizontalSlider(row.scale, MinScale, MaxScale, GUILayout.Width(90f));
            row.scale = EditorGUILayout.FloatField(row.scale, GUILayout.Width(50f));

            GUILayout.Space(6f);

            // 밀기는 숫자로만 준다 — 씬에서 끈 것은 "어느 칸"으로 들어가지, 칸 안 보정이 아니다.
            row.offset.x = EditorGUILayout.FloatField(row.offset.x, GUILayout.Width(40f));
            row.offset.y = EditorGUILayout.FloatField(row.offset.y, GUILayout.Width(40f));
            row.offset.z = EditorGUILayout.FloatField(row.offset.z, GUILayout.Width(40f));

            if (GUILayout.Button("1로", GUILayout.Width(32f)))
            {
                row.yaw = 0f;
                row.scale = 1f;
                row.offset = Vector3.zero;
            }

            if (EditorGUI.EndChangeCheck())
            {
                // 0 이하는 모델이 사라지거나 뒤집힌다 — 숫자 칸에 직접 쓸 수 있으므로 막는다.
                row.scale = Mathf.Max(0.01f, row.scale);
                ApplyRow(row);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("저장 — 기지 배치 + 아트", GUILayout.Height(28f)))
                Save();

            if (GUILayout.Button("각도·크기 전부 되돌리기", GUILayout.Width(160f), GUILayout.Height(28f)))
                ResetAll();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "씬에서 건물을 옮기면 그 칸이 기지 배치가 되고, 돌리고 키우면 아트 보정이 됩니다. " +
                "게임을 켜면 여기서 본 그대로 나옵니다.\n" +
                "저장하면 건물이 칸에 딱 맞게 붙습니다 — 그게 게임에 뜰 자리입니다.",
                MessageType.None);
        }

        private void Lineup()
        {
            // 이미 서 있으면 통째로 다시 세운다 — 그 전에 묻는다.
            if (!ConfirmLosingSceneWork("기지 세우기", "지금 저장된 배치대로 처음부터 다시 세웁니다."))
                return;

            if (!EnsureSlots())
                return;

            BuildingArtEditor.BuildLineup(_presenter, _ground, _slots, TableFromRows());
        }

        private bool EnsureSlots()
        {
            TryFindScene();

            if (_presenter == null || _ground == null)
                return false;

            _catalog = BuildingArtEditor.LoadCatalog();
            if (_catalog == null)
                return false;

            _slots = BuildingArtEditor.ComputeSlots(_presenter, _ground, _catalog);

            if (_rows.Count != _slots.Count)
                FillRows(BuildingArtEditor.LoadTable());

            return true;
        }

        /// <summary>씬에 서 있는 모습을 읽어 숫자에 반영한다. 씬이 기준이다.</summary>
        private void SyncFromScene()
        {
            // 창으로 돌아올 때마다 불린다 — 플레이 중에는 씬을 건드리지 않는다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!EnsureSlots() || BuildingArtEditor.FindLineupRoot() == null)
                return;

            BaseLayoutFile layout;
            FillRows(ReadScene(out layout, false));
            Repaint();
        }

        /// <summary>씬에 세운 기지를 치운다. 저장하지 않은 작업이 있으면 먼저 묻는다.</summary>
        private void Clear()
        {
            if (!ConfirmLosingSceneWork(
                    "기지 치우기",
                    "씬에 세운 기지를 치웁니다."))
            {
                return;
            }

            BuildingArtEditor.RemoveLineup();
        }

        /// <summary>파일 값으로 되돌린다. 씬에 서 있는 것도 그 값으로 다시 세운다.</summary>
        private void RevertFromFile()
        {
            if (!ConfirmLosingSceneWork(
                    "파일에서 되돌리기",
                    "마지막으로 저장한 상태로 되돌립니다."))
            {
                return;
            }

            if (!EnsureSlots())
                return;

            FillRows(BuildingArtEditor.LoadTable());

            if (BuildingArtEditor.FindLineupRoot() != null)
                BuildingArtEditor.BuildLineup(_presenter, _ground, _slots, TableFromRows());

            Repaint();
        }

        /// <summary>
        /// 씬에 서 있는 건물들을 읽어 아트 보정과 기지 배치로 되돌린다.
        /// 씬에 없는 건물(모델이 아직 없는 것)은 지금 데이터의 자리를 그대로 지킨다.
        /// </summary>
        private BuildingArtTable ReadScene(out BaseLayoutFile layout, bool verify)
        {
            var table = new BuildingArtTable();
            var warnings = new List<string>();

            layout = new BaseLayoutFile { baseSize = _ground.BaseSize };

            for (int i = 0; i < _slots.Count; i++)
            {
                LineupSlot slot = _slots[i];
                Transform target = BuildingArtEditor.FindBuilding(slot);
                Vector3 offset = OffsetOf(slot.SlotId);
                GridPos anchor = slot.Anchor;

                if (target != null)
                {
                    anchor = BuildingArtEditor.AnchorFromScene(target, slot, _ground, offset);

                    Vector3 scale = target.localScale;

                    if (Mathf.Abs(scale.x - scale.y) > 0.001f || Mathf.Abs(scale.x - scale.z) > 0.001f)
                    {
                        warnings.Add(slot.DefId + ": 축마다 크기가 달라서(" + scale +
                                     ") X 기준으로만 저장합니다.");
                    }

                    Bounds before;
                    bool hadBounds = BuildingArtEditor.TryWorldBounds(target, out before);

                    float tilt;
                    BuildingArtAdjustment adjustment = BuildingArtEditor.ReadBack(
                        target, slot, _presenter, _ground, offset, out tilt);

                    if (adjustment == null)
                    {
                        warnings.Add(slot.DefId + ": 모델 경계를 재지 못해 건너뜁니다.");
                        continue;
                    }

                    if (tilt > 1f)
                    {
                        warnings.Add(slot.DefId + ": " + tilt.ToString("0") +
                                     "° 기울어져 있습니다. 세운 채 돌린 각도만 저장됩니다.");
                    }

                    AddOnce(table, adjustment);

                    if (verify)
                    {
                        LineupSlot snapped = slot;
                        snapped.Anchor = anchor;
                        snapped.GroundCenter =
                            _ground.FootprintCenterWorld(anchor, slot.Def.Footprint, 0f);

                        BuildingArtEditor.Apply(target, snapped, adjustment, _presenter, _ground);

                        Bounds after;
                        if (hadBounds && BuildingArtEditor.TryWorldBounds(target, out after)
                            && !SameShape(before, after))
                        {
                            warnings.Add(slot.DefId + ": 저장한 값으로 다시 세우니 크기가 달라집니다. " +
                                         "건물 전체가 아니라 안쪽 부품을 움직이신 것 같습니다.");
                        }
                    }
                }

                layout.slots.Add(new BaseSlotRecord
                {
                    slotId = slot.SlotId,
                    defId = slot.DefId,
                    x = anchor.X,
                    y = anchor.Y,
                    zoneId = slot.ZoneId,
                });
            }

            if (warnings.Count > 0)
                Debug.LogWarning("기지 저작 — 확인할 것:\n" + string.Join("\n", warnings));

            return table;
        }

        private void Save()
        {
            if (!EnsureSlots())
                return;

            if (BuildingArtEditor.FindLineupRoot() == null)
            {
                BuildingArtEditor.SaveTable(TableFromRows());
                Debug.Log("아트 보정만 저장했습니다 — 씬에 기지가 서 있지 않아 배치는 건드리지 않았습니다.");
                return;
            }

            BaseLayoutFile layout;
            BuildingArtTable art = ReadScene(out layout, true);

            // 겹치거나 범위를 벗어난 배치는 **쓰지 않는다.** 그대로 저장하면 게임이
            // 시작하자마자 터지고, 원인이 저작 실수라는 것이 화면에 드러나지 않는다.
            IReadOnlyList<LayoutProblem> problems =
                TerritoryDataLoader.ToLayout(layout, _ground.Bounds).Validate(_catalog);

            if (problems.Count > 0)
            {
                var lines = new List<string>();
                for (int i = 0; i < problems.Count; i++)
                {
                    lines.Add(NameOfSlot(problems[i].SlotId) + " — " + problems[i].Message);
                }

                Debug.LogError("기지 배치를 저장하지 못했습니다:\n" + string.Join("\n", lines));

                EditorUtility.DisplayDialog(
                    "기지 배치 저장",
                    "건물이 서로 겹치거나 기지 밖으로 나가 있어 저장하지 않았습니다.\n" +
                    "어느 건물이 문제인지는 콘솔에 적었습니다.\n\n" +
                    "겹친 건물을 떼어 놓고 다시 저장하세요.",
                    "확인");

                return;
            }

            BuildingArtEditor.SaveTable(art);
            BuildingArtEditor.SaveLayout(layout);

            // 새 자리로 다시 세운다 — 칸에 딱 붙은 모습이 게임에 뜰 모습이다.
            EnsureSlots();
            FillRows(art);
            BuildingArtEditor.BuildLineup(_presenter, _ground, _slots, art);
            Repaint();

            Debug.Log(
                "기지를 저장했습니다 — 건물 " + layout.slots.Count + "채의 자리(" +
                BuildingArtEditor.BaseLayoutPath + ")와 각도·크기(" +
                BuildingArtEditor.ArtDataPath + ").\n" +
                "게임을 켜면 지금 보이는 그대로 나옵니다.");
        }

        private string NameOfSlot(int slotId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].SlotId == slotId)
                    return _slots[i].DefId;
            }

            return "Slot#" + slotId;
        }

        /// <summary>그 건물을 씬에서 골라 화면 가운데로 가져온다 — 21채 중에 찾기 편하게.</summary>
        private void SelectInScene(Row row)
        {
            LineupSlot slot;
            if (!TryGetSlot(row.slotId, out slot))
                return;

            Transform target = BuildingArtEditor.FindBuilding(slot);

            if (target == null)
                return;

            Selection.activeGameObject = target.gameObject;
            SceneView.FrameLastActiveSceneView();
        }

        private bool TryGetSlot(int slotId, out LineupSlot slot)
        {
            slot = default(LineupSlot);

            if (_slots == null)
                return false;

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].SlotId != slotId)
                    continue;

                slot = _slots[i];
                return true;
            }

            return false;
        }

        private void FillRows(BuildingArtTable table)
        {
            _rows.Clear();

            for (int i = 0; i < _slots.Count; i++)
            {
                LineupSlot slot = _slots[i];
                BuildingArtAdjustment a = table.For(slot.DefId);

                _rows.Add(new Row
                {
                    slotId = slot.SlotId,
                    defId = slot.DefId,
                    footprint = slot.Def.Footprint.Width + "x" + slot.Def.Footprint.Height,
                    cell = slot.Anchor.X + ", " + slot.Anchor.Y,
                    yaw = a.YawDegrees,
                    scale = a.ScaleMultiplier,
                    offset = new Vector3(a.OffsetX, a.OffsetY, a.OffsetZ),
                });
            }
        }

        private void ApplyRow(Row row)
        {
            // 여기서 EnsureSlots를 부르지 않는다 — 값이 바뀔 때마다 JSON 두 개를 다시 읽게 되고,
            // 그건 매 프레임 일어날 수 있다. 자리는 창을 열거나 기지를 세울 때 준비된다.
            if (_slots == null)
                return;

            LineupSlot slot;
            if (!TryGetSlot(row.slotId, out slot))
                return;

            Transform target = BuildingArtEditor.FindBuilding(slot);

            if (target == null)
                return;

            // 지금 씬에 서 있는 자리를 유지한 채 각도·크기만 바꾼다 —
            // 슬라이더를 건드렸다고 건물이 원래 칸으로 튀어가면 배치 작업이 끊긴다.
            slot.Anchor = BuildingArtEditor.AnchorFromScene(target, slot, _ground, row.offset);
            slot.GroundCenter = _ground.FootprintCenterWorld(slot.Anchor, slot.Def.Footprint, 0f);

            BuildingArtEditor.Apply(target, slot, ToAdjustment(row), _presenter, _ground);
        }

        private void ApplyAll()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                ApplyRow(_rows[i]);
            }
        }

        /// <summary>
        /// 씬에 세운 기지가 있으면 "저장 안 한 작업이 사라진다"고 먼저 묻는다.
        ///
        /// 저장했는지 아닌지를 가려내지 않고 **서 있으면 무조건 묻는** 이유 — 사람이 씬에서
        /// 건물을 옮긴 것은 우리가 알 길이 없고, 틀리면 작업이 통째로 날아간다.
        /// 한 번 더 묻는 값이 훨씬 싸다.
        /// </summary>
        private bool ConfirmLosingSceneWork(string title, string what)
        {
            if (BuildingArtEditor.FindLineupRoot() == null)
                return true;

            return EditorUtility.DisplayDialog(
                title,
                what + "\n\n" +
                "저장하지 않은 배치·각도·크기는 사라집니다.\n" +
                "계속할까요?",
                "계속",
                "그만두기");
        }

        private void ResetAll()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "각도·크기 되돌리기",
                "건물 " + _rows.Count + "채의 각도·크기·밀기를 전부 기본값(0도, 1배)으로 되돌립니다.\n" +
                "자리(어느 칸에 서는가)는 그대로 둡니다.\n\n" +
                "아직 저장은 되지 않으므로, 잘못 눌렀으면 [파일에서 되돌리기]로 되살릴 수 있습니다.",
                "되돌리기",
                "그만두기");

            if (!confirmed)
                return;

            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].yaw = 0f;
                _rows[i].scale = 1f;
                _rows[i].offset = Vector3.zero;
            }

            ApplyAll();
        }

        private BuildingArtTable TableFromRows()
        {
            var table = new BuildingArtTable();

            for (int i = 0; i < _rows.Count; i++)
            {
                AddOnce(table, ToAdjustment(_rows[i]));
            }

            return table;
        }

        /// <summary>
        /// 아트 보정은 건물 **종류마다** 하나다 — 같은 건물이 두 채 서면(농장 2개 등)
        /// 먼저 읽은 쪽을 쓴다. 자리는 채마다 따로지만 모양 보정은 같은 아트에 걸린다.
        /// </summary>
        private static void AddOnce(BuildingArtTable table, BuildingArtAdjustment adjustment)
        {
            BuildingArtAdjustment existing;

            if (table.TryGet(adjustment.DefId, out existing))
                return;

            table.Add(adjustment);
        }

        private static BuildingArtAdjustment ToAdjustment(Row row)
        {
            return new BuildingArtAdjustment(
                row.defId, row.yaw, Mathf.Max(0.01f, row.scale),
                row.offset.x, row.offset.y, row.offset.z);
        }

        private Vector3 OffsetOf(int slotId)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].slotId == slotId)
                    return _rows[i].offset;
            }

            return Vector3.zero;
        }

        /// <summary>크기와 생김새가 눈으로 같은가. 건물 크기의 1%까지는 같은 것으로 본다.</summary>
        private static bool SameShape(Bounds a, Bounds b)
        {
            return Vector3.Distance(a.size, b.size) <= Mathf.Max(0.01f, a.size.magnitude * 0.01f);
        }

        private void TryFindScene()
        {
            _presenter = Object.FindFirstObjectByType<TerritoryPresenter>();
            _ground = Object.FindFirstObjectByType<TerritoryGround>();
        }
    }
}

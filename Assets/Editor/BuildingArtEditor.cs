using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.Editor
{
    /// <summary>편집 중인 건물 한 채. 기지 배치(base-layout.json)의 자리 하나에 대응한다.</summary>
    internal struct LineupSlot
    {
        public int SlotId;
        public string DefId;
        public BuildingDef Def;
        public BuildingModelEntry Entry;

        /// <summary>지금 데이터가 말하는 자리(최소 코너 칸).</summary>
        public GridPos Anchor;

        public int ZoneId;

        public Vector3 GroundCenter;
    }

    /// <summary>
    /// 기지 저작의 공통 부분 — 배치 읽고 쓰기, 씬에 세우기, 씬에서 되읽기.
    /// 화면은 <see cref="BuildingArtWindow"/>가 담당한다.
    ///
    /// 씬에 세운 자리가 **곧 기지 배치다.** 건물을 옮기면 그 칸이 저장되고,
    /// 돌리고 키우면 아트 보정으로 저장된다. 게임은 같은 파일을 읽는다.
    ///
    /// ⚠️ 씬에서 되읽을 때는 "클릭했을 때 잡히는 것"과 "읽는 것"이 같아야 한다.
    /// 그래서 건물을 <see cref="PrefabUtility.InstantiatePrefab"/>으로 세운다 —
    /// 그냥 복제하면 클릭이 안쪽 부품을 잡고, 사람이 만진 곳과 읽는 곳이 어긋난다
    /// (2026-10-14에 두 번 밟았다).
    ///
    /// 포즈 계산은 <see cref="BuildingModelPlacement"/>가 한다 — 게임과 같은 함수다.
    /// </summary>
    public static class BuildingArtEditor
    {
        public const string ArtDataPath = "Assets/Data/building-art.json";

        public const string BaseLayoutPath = "Assets/Data/base-layout.json";

        private const string BuildingDefsPath = "Assets/Data/building-defs.json";
        internal const string LineupRootName = "BuildingArtLineup";

        [MenuItem("Olympus/기지 저작 창 열기", false, 200)]
        public static void OpenWindow()
        {
            BuildingArtWindow.Open();
        }

        /// <summary>
        /// 편집할 건물 목록을 **기지 배치 파일에서** 읽는다.
        ///
        /// 임의의 줄로 늘어놓지 않는 이유 — 씬에 세운 자리가 곧 기지 배치다. 게임을 켜면
        /// 여기서 본 그대로 나와야 하므로, 시작점도 실제 배치여야 한다.
        /// </summary>
        internal static List<LineupSlot> ComputeSlots(
            TerritoryPresenter presenter, TerritoryGround ground, BuildingCatalog catalog)
        {
            var slots = new List<LineupSlot>();

            BaseLayoutFile layout = LoadLayoutFile();
            if (layout == null)
                return slots;

            for (int i = 0; i < layout.slots.Count; i++)
            {
                BaseSlotRecord record = layout.slots[i];

                BuildingDef def;
                if (!catalog.TryGet(record.defId, out def))
                {
                    Debug.LogWarning("건물 정의에 없는 자리입니다: " + record.defId);
                    continue;
                }

                var anchor = new GridPos(record.x, record.y);

                slots.Add(new LineupSlot
                {
                    SlotId = record.slotId,
                    DefId = record.defId,
                    Def = def,
                    Entry = EntryFor(presenter, record.defId),
                    Anchor = anchor,
                    ZoneId = record.zoneId,
                    GroundCenter = ground.FootprintCenterWorld(anchor, def.Footprint, 0f),
                });
            }

            return slots;
        }

        private static BuildingModelEntry EntryFor(TerritoryPresenter presenter, string defId)
        {
            IReadOnlyList<BuildingModelEntry> entries = presenter.BuildingModels;

            for (int i = 0; i < entries.Count; i++)
            {
                BuildingModelEntry entry = entries[i];

                if (entry != null && entry.model != null && entry.defId == defId)
                    return entry;
            }

            return null;
        }

        internal static BaseLayoutFile LoadLayoutFile()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(BaseLayoutPath);

            if (json == null)
            {
                Debug.LogError("기지 배치 파일이 없습니다: " + BaseLayoutPath);
                return null;
            }

            return TerritoryDataLoader.LoadLayoutFile(json);
        }

        /// <summary>씬에서 읽은 자리를 기지 배치 파일에 쓴다. 게임이 읽는 그 파일이다.</summary>
        internal static void SaveLayout(BaseLayoutFile file)
        {
            File.WriteAllText(BaseLayoutPath, TerritoryDataLoader.ToJson(file));
            AssetDatabase.ImportAsset(BaseLayoutPath);
        }

        /// <summary>건물을 전부 새로 세운다. 이미 서 있으면 먼저 치운다.</summary>
        internal static void BuildLineup(
            TerritoryPresenter presenter,
            TerritoryGround ground,
            List<LineupSlot> slots,
            BuildingArtTable art)
        {
            RemoveLineup();

            var rootGo = new GameObject(LineupRootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "건물 아트 늘어놓기");

            for (int i = 0; i < slots.Count; i++)
            {
                LineupSlot slot = slots[i];

                // 모델이 아직 없는 건물은 세울 것이 없다 — 게임에서는 그레이박스 큐브다.
                if (slot.Entry == null)
                {
                    Debug.LogWarning("모델이 없어 건너뜁니다: " + slot.DefId +
                                     " — 이 건물의 자리는 지금 배치 그대로 유지됩니다.");
                    continue;
                }

                // ⚠️ 반드시 프리팹 인스턴스로 만든다(Object.Instantiate가 아니라).
                // 씬 뷰에서 건물을 클릭했을 때 Unity가 **프리팹 루트**를 잡아 주기 때문이다.
                // 그냥 복제하면 클릭이 건물 안쪽 부품을 잡고, 사람은 부품을 움직이는데
                // 우리는 루트를 읽게 되어 저장값이 엉뚱해진다(2026-10-14에 밟았다).
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    slot.Entry.model, rootGo.transform);

                // 이름으로 어느 자리인지 되찾는다. defId가 아니라 자리 번호를 앞에 붙이는 이유 —
                // 같은 건물이 두 채 서는 날(농장 2개 등) 이름이 겹쳐 한 채만 읽히게 된다.
                instance.name = SceneNameOf(slot);

                // 머티리얼을 직접 입힌다 — FBX가 들고 온 것을 쓰지 않는다(게임과 같은 이유).
                if (slot.Entry.material != null)
                {
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        renderers[r].sharedMaterial = slot.Entry.material;
                    }
                }

                Apply(instance.transform, slot, art.For(slot.DefId), presenter, ground);
            }
        }

        /// <summary>
        /// 아트가 들고 나온 회전. 편집 툴은 모델 자신을 돌리므로 이걸 깔고 그 위에 각도를 얹는다.
        /// 날려 버리면 건물이 뒤로 눕는다(2026-09-11에 본부가 하늘을 보고 누웠다).
        /// </summary>
        internal static Quaternion BaseRotationOf(BuildingModelEntry entry)
        {
            return entry.model.transform.localRotation;
        }

        /// <summary>
        /// 값 하나를 씬에 반영한다. 게임이 건물을 앉히는 것과 **같은 함수**를 쓴다 —
        /// 여기서 따로 계산하면 편집 창에서 본 모습과 게임에 뜨는 모습이 갈라진다.
        /// </summary>
        internal static void Apply(
            Transform target,
            LineupSlot slot,
            BuildingArtAdjustment adjustment,
            TerritoryPresenter presenter,
            TerritoryGround ground)
        {
            BuildingModelPlacement.Place(
                target,
                BaseRotationOf(slot.Entry),
                slot.GroundCenter,
                new Vector3(adjustment.OffsetX, adjustment.OffsetY, adjustment.OffsetZ),
                presenter.ModelYaw + adjustment.YawDegrees,
                TargetX(slot, presenter, ground),
                TargetZ(slot, presenter, ground),
                adjustment.ScaleMultiplier);
        }

        /// <summary>
        /// 씬에서 사람이 맞춰 놓은 **각도와 크기**를 저장할 값으로 되돌린다.
        /// 되돌리지 못하면 null — 부르는 쪽이 건너뛰고 이유를 말한다.
        ///
        /// 밀기(<paramref name="keepOffset"/>)는 씬에서 읽지 않고 들고 있던 값을 그대로 쓴다 —
        /// 늘어놓은 자리에서 옮긴 것은 비교하려고 옮긴 것이지 보정이 아니다
        /// (<see cref="BuildingModelPlacement.TryReadBack"/> 주석 참고).
        /// </summary>
        internal static BuildingArtAdjustment ReadBack(
            Transform target,
            LineupSlot slot,
            TerritoryPresenter presenter,
            TerritoryGround ground,
            Vector3 keepOffset,
            out float tiltDegrees)
        {
            float yaw;
            float scale;

            bool ok = BuildingModelPlacement.TryReadBack(
                target,
                BaseRotationOf(slot.Entry),
                presenter.ModelYaw,
                TargetX(slot, presenter, ground),
                TargetZ(slot, presenter, ground),
                out yaw, out scale, out tiltDegrees);

            if (!ok)
                return null;

            // ⚠️ 소수점을 잘라서 돌려준다. 재서 나온 날것의 값(1.4984873533248902)을 그대로
            // 숫자 입력칸에 넣으면, 칸이 보여주는 글자("1.498487")와 값이 달라서 IMGUI가
            // **매 프레임 "값이 바뀌었다"고 보고한다.** 그러면 21채를 매 프레임 다시 앉히게 되고
            // 에디터가 멈춘 것처럼 느려진다(2026-10-14에 플레이 중 멈췄다).
            // 파일도 읽기 좋아진다 — 사람이 열어 보는 파일이다.
            return new BuildingArtAdjustment(
                slot.DefId,
                Mathf.Round(yaw * 100f) / 100f,
                Mathf.Max(0.0001f, Mathf.Round(scale * 10000f) / 10000f),
                keepOffset.x, keepOffset.y, keepOffset.z);
        }

        /// <summary>
        /// 씬에 서 있는 건물이 **어느 칸에 선 것인지** 읽는다. 기지 배치 저작의 핵심이다.
        ///
        /// 건물의 바닥 중심이 기준이다 — <see cref="BuildingModelPlacement.Place"/>가 그 점을
        /// 자리 중앙에 맞춰 세우므로, 되읽을 때도 같은 점을 봐야 왕복이 맞는다.
        /// 칸 사이에 놓인 것은 가장 가까운 칸으로 붙는다(<see cref="GridFootprint.AnchorForCenter"/>).
        /// </summary>
        internal static GridPos AnchorFromScene(
            Transform target, LineupSlot slot, TerritoryGround ground, Vector3 offset)
        {
            Bounds bounds;
            if (!TryWorldBounds(target, out bounds))
                return slot.Anchor;

            // 밀기는 자리와 무관한 보정이다 — 빼고 봐야 자리가 제대로 나온다.
            Vector3 center = new Vector3(bounds.center.x - offset.x, 0f, bounds.center.z - offset.z);
            Vector3 local = ground.transform.InverseTransformPoint(center);

            return slot.Def.Footprint.AnchorForCenter(new WorldXZ(local.x, local.z), ground.Grid);
        }

        internal static bool TryWorldBounds(Transform target, out Bounds bounds)
        {
            bounds = new Bounds();

            if (target == null)
                return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static float TargetX(
            LineupSlot slot, TerritoryPresenter presenter, TerritoryGround ground)
        {
            return slot.Def.Footprint.Width * ground.Grid.CellSize * presenter.FootprintInset;
        }

        private static float TargetZ(
            LineupSlot slot, TerritoryPresenter presenter, TerritoryGround ground)
        {
            return slot.Def.Footprint.Height * ground.Grid.CellSize * presenter.FootprintInset;
        }

        internal static void SaveTable(BuildingArtTable table)
        {
            File.WriteAllText(ArtDataPath, TerritoryDataLoader.ToJson(table));
            AssetDatabase.ImportAsset(ArtDataPath);
        }

        internal static BuildingArtTable LoadTable()
        {
            return TerritoryDataLoader.LoadArtTable(
                AssetDatabase.LoadAssetAtPath<TextAsset>(ArtDataPath));
        }

        internal static BuildingCatalog LoadCatalog()
        {
            var defs = AssetDatabase.LoadAssetAtPath<TextAsset>(BuildingDefsPath);

            if (defs == null)
            {
                Debug.LogError("건물 정의 파일이 없습니다: " + BuildingDefsPath);
                return null;
            }

            return TerritoryDataLoader.LoadCatalog(defs);
        }

        internal static string SceneNameOf(LineupSlot slot)
        {
            return slot.SlotId + "_" + slot.DefId;
        }

        internal static Transform FindBuilding(LineupSlot slot)
        {
            GameObject root = FindLineupRoot();

            if (root == null)
                return null;

            return root.transform.Find(SceneNameOf(slot));
        }

        internal static GameObject FindLineupRoot()
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();

            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == LineupRootName)
                    return roots[i];
            }

            return null;
        }

        internal static void RemoveLineup()
        {
            GameObject root = FindLineupRoot();

            if (root != null)
                Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 플레이를 누를 때 편집용 기지가 씬에 서 있으면 먼저 묻는다.
    ///
    /// 그대로 두면 게임이 세우는 건물과 편집용 건물이 같은 자리에 두 겹으로 겹쳐 보인다.
    /// 그렇다고 말없이 치우면 저장 안 한 작업이 날아간다 — 그래서 묻는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class BuildingArtPlayModeGuard
    {
        static BuildingArtPlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            if (BuildingArtEditor.FindLineupRoot() == null)
                return;

            int choice = EditorUtility.DisplayDialogComplex(
                "기지 저작",
                "씬에 편집용 기지가 서 있습니다.\n" +
                "이대로 플레이하면 건물이 두 겹으로 겹쳐 보입니다.\n\n" +
                "저장하지 않은 배치가 있으면 [플레이 취소]를 누르고 먼저 저장하세요.",
                "치우고 플레이",
                "플레이 취소",
                "그대로 플레이");

            if (choice == 0)
            {
                BuildingArtEditor.RemoveLineup();
                return;
            }

            if (choice == 1)
                EditorApplication.isPlaying = false;
        }
    }
}

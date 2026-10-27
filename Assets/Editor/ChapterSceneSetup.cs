using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Olympus.Game.Battle;
using Olympus.Game.Chapter;
using Olympus.Game.Territory;

namespace Olympus.Editor
{
    /// <summary>
    /// 기지 씬에 1장 오브젝트를 세운다 — "Chapter"(거인 사건 + 협곡 전투 재생). HUD를 지을 때마다 같이 불린다
    /// (<see cref="HudSceneBuilder.Build"/>). 이미 있으면 배선만 다시 한다 — 인스펙터에서 맞춘 자리 값
    /// (거인 방향·협곡 거리)은 남는다.
    /// </summary>
    internal static class ChapterSceneSetup
    {
        private const string RootName = "Chapter";
        private const string ChapterDataPath = "Assets/Data/chapter1.json";
        private const string MaterialFolder = "Assets/Settings/Chapter";

        internal static GiantThreatDirector EnsureChapter(TerritoryPresenter presenter)
        {
            var presenterSo = new SerializedObject(presenter);
            TerritorySceneBuilder.Wire(presenterSo, "_chapter", TerritorySceneBuilder.LoadRequired<TextAsset>(ChapterDataPath));
            presenterSo.ApplyModifiedPropertiesWithoutUndo();

            UnityEngine.SceneManagement.Scene scene = presenter.gameObject.scene;
            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName)
                {
                    root = go;
                    break;
                }
            }

            if (root == null)
            {
                root = new GameObject(RootName);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            }

            // ?? 를 쓰지 않는다 — 에디터의 GetComponent는 없을 때 "가짜 null"을 돌려줘서 ?? 가 그걸 통과시킨다.
            BattleArena arena = root.GetComponent<BattleArena>();
            if (arena == null)
                arena = root.AddComponent<BattleArena>();

            GiantThreatDirector director = root.GetComponent<GiantThreatDirector>();
            if (director == null)
                director = root.AddComponent<GiantThreatDirector>();

            BattleSceneBuilder.WireArenaMaterials(arena);
            BattleSoldierSetup.Wire(arena);

            EnsureFolder("Assets/Settings");
            EnsureFolder(MaterialFolder);

            var so = new SerializedObject(director);
            TerritorySceneBuilder.Wire(so, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so, "_arena", arena);
            TerritorySceneBuilder.Wire(so, "_canyonFloorMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/CanyonFloor.mat", "CanyonFloor", new Color(0.55f, 0.48f, 0.36f)));
            TerritorySceneBuilder.Wire(so, "_canyonWallMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/CanyonWall.mat", "CanyonWall", new Color(0.40f, 0.36f, 0.33f)));
            // 거리는 조립할 때마다 여기 값으로 맞춘다 — 컴포넌트를 재사용하므로 코드 기본값만 바꾸면 기존 씬에 안 들어간다
            // (2026-10-27 "협곡·도적 소굴이 멀다"를 고치며 밟음). 인스펙터에서 맞춘 값은 여기에 옮겨 적는다.
            so.FindProperty("_canyonGap").floatValue = 1f;
            so.FindProperty("_approachLength").floatValue = 32f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 도적 습격은 따로 재생 부분을 쓴다 — 거인 협곡 전투와 동시에 걸릴 수 있어서(도적은 기다리지만 그림은 남는다).
            Transform raidsT = root.transform.Find("Raids");
            if (raidsT == null)
            {
                raidsT = new GameObject("Raids").transform;
                raidsT.SetParent(root.transform, false);
            }

            BattleArena raidArena = raidsT.GetComponent<BattleArena>();
            if (raidArena == null)
                raidArena = raidsT.gameObject.AddComponent<BattleArena>();
            BattleSceneBuilder.WireArenaMaterials(raidArena);
            BattleSoldierSetup.Wire(raidArena);

            RaidDirector raidDirector = raidsT.GetComponent<RaidDirector>();
            if (raidDirector == null)
                raidDirector = raidsT.gameObject.AddComponent<RaidDirector>();

            var rso = new SerializedObject(raidDirector);
            TerritorySceneBuilder.Wire(rso, "_presenter", presenter);
            TerritorySceneBuilder.Wire(rso, "_arena", raidArena);
            TerritorySceneBuilder.Wire(rso, "_giant", director);
            TerritorySceneBuilder.Wire(rso, "_fieldMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/CanyonFloor.mat", "CanyonFloor", new Color(0.55f, 0.48f, 0.36f)));
            rso.ApplyModifiedPropertiesWithoutUndo();

            // 성문 밖 상시 전선 — 볼거리 병사는 도적 쪽 재생 부분의 모델 목록을 빌린다(같은 모델·정면 맞춤).
            FrontlineDirector front = root.GetComponent<FrontlineDirector>();
            if (front == null)
                front = root.AddComponent<FrontlineDirector>();

            var fso = new SerializedObject(front);
            TerritorySceneBuilder.Wire(fso, "_presenter", presenter);
            TerritorySceneBuilder.Wire(fso, "_arena", raidArena);
            TerritorySceneBuilder.Wire(fso, "_giant", director);
            TerritorySceneBuilder.Wire(fso, "_outpostWoodMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/OutpostWood.mat", "OutpostWood", new Color(0.45f, 0.32f, 0.20f)));
            TerritorySceneBuilder.Wire(fso, "_outpostStoneMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/OutpostStone.mat", "OutpostStone", new Color(0.45f, 0.42f, 0.40f)));
            TerritorySceneBuilder.Wire(fso, "_hpBackMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/OutpostHpBack.mat", "OutpostHpBack", new Color(0.10f, 0.10f, 0.10f)));
            TerritorySceneBuilder.Wire(fso, "_hpFillMaterial", TerritorySceneBuilder.CreateOrLoadFlatMaterial(
                MaterialFolder + "/OutpostHpFill.mat", "OutpostHpFill", new Color(0.85f, 0.25f, 0.20f)));
            fso.FindProperty("_outpostDistance").floatValue = 34f;   // 위 거인 거리와 같은 이유
            fso.FindProperty("_outpostStep").floatValue = 5f;
            fso.ApplyModifiedPropertiesWithoutUndo();

            TroopGarrisonView garrison = root.GetComponent<TroopGarrisonView>();
            if (garrison == null)
                garrison = root.AddComponent<TroopGarrisonView>();

            var gso = new SerializedObject(garrison);
            TerritorySceneBuilder.Wire(gso, "_presenter", presenter);
            TerritorySceneBuilder.Wire(gso, "_arena", arena);
            TerritorySceneBuilder.Wire(gso, "_director", director);
            gso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            return director;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}

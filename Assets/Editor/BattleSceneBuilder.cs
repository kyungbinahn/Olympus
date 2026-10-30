using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Game.Battle;
using Olympus.UI;

namespace Olympus.Editor
{
    /// <summary>
    /// 전선(전투) 씬을 조립한다. 기지·월드맵 씬과 같은 방식이다 — 손으로 씬을 쓰지 않고 메뉴로 짓는다.
    ///
    /// 병사는 씬에 미리 두지 않는다. 기지의 군대·스테이지에 따라 매번 달라서 <see cref="BattlePresenter"/>가
    /// 실행 중에 세운다. 씬에는 땅·빛·카메라·HUD와 재질만 있다.
    /// </summary>
    public static class BattleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Battle.unity";
        private const string MaterialFolder = "Assets/Settings/Battle";

        [MenuItem("Olympus/Setup/전투 씬 만들기", false, 120)]
        public static void BuildBattleScene()
        {
            AssetDatabase.Refresh();
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Settings");
            EnsureFolder(MaterialFolder);

            UnityEngine.SceneManagement.Scene scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 빛·카메라·땅은 손으로 맞췄을 수 있다 — 있으면 그대로 둔다. 재질 배선과 HUD는 매번 다시 한다.
            FindOrCreateLight();
            FindOrCreateCamera();
            FindOrCreateGround();
            BattlePresenter presenter = FindOrCreatePresenter();
            BattleArena arena = EnsureArena(presenter);
            WireArenaMaterials(arena);
            BattleSoldierSetup.Wire(arena);
            BuildHud(presenter);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("전투 씬을 준비했습니다: " + ScenePath +
                      "\n기지 씬에서 플레이를 시작해 하단 '전선' 버튼으로 들어오세요 (이 씬을 바로 틀면 기지로 돌아갑니다).");
        }

        private static Material Mat(string name, Color color)
        {
            return TerritorySceneBuilder.CreateOrLoadFlatMaterial(MaterialFolder + "/" + name + ".mat", name, color);
        }

        /// <summary>전선 씬의 재생 부분 — 진행(BattlePresenter)과 같은 오브젝트에 붙이고 서로 잇는다.</summary>
        internal static BattleArena EnsureArena(BattlePresenter presenter)
        {
            BattleArena arena = presenter.GetComponent<BattleArena>();
            if (arena == null)
                arena = presenter.gameObject.AddComponent<BattleArena>();

            var so = new SerializedObject(presenter);
            TerritorySceneBuilder.Wire(so, "_arena", arena);
            so.ApplyModifiedPropertiesWithoutUndo();
            return arena;
        }

        /// <summary>도형 병사·체력바 재질. 전선 씬과 기지(협곡 전투)가 같은 재질을 쓴다.</summary>
        internal static void WireArenaMaterials(BattleArena arena)
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder(MaterialFolder);

            var so = new SerializedObject(arena);

            // 아군은 파랑(그리스 방패색), 적은 녹슨 붉은 갈색 — 진영이 색으로 바로 갈린다.
            TerritorySceneBuilder.Wire(so, "_playerMaterial", Mat("BattlePlayer", new Color(0.22f, 0.40f, 0.78f)));
            TerritorySceneBuilder.Wire(so, "_enemyMaterial", Mat("BattleEnemy", new Color(0.62f, 0.26f, 0.20f)));
            TerritorySceneBuilder.Wire(so, "_skinMaterial", Mat("BattleSkin", new Color(0.86f, 0.70f, 0.55f)));
            TerritorySceneBuilder.Wire(so, "_bronzeMaterial", Mat("BattleBronze", new Color(0.80f, 0.58f, 0.25f)));
            TerritorySceneBuilder.Wire(so, "_woodMaterial", Mat("BattleWood", new Color(0.42f, 0.30f, 0.20f)));
            TerritorySceneBuilder.Wire(so, "_flashMaterial", Mat("BattleFlash", new Color(1f, 1f, 1f)));
            TerritorySceneBuilder.Wire(so, "_hpBackMaterial", Mat("BattleHpBack", new Color(0.10f, 0.10f, 0.10f)));
            TerritorySceneBuilder.Wire(so, "_hpPlayerMaterial", Mat("BattleHpPlayer", new Color(0.35f, 0.85f, 0.40f)));
            TerritorySceneBuilder.Wire(so, "_hpEnemyMaterial", Mat("BattleHpEnemy", new Color(0.90f, 0.30f, 0.25f)));

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void FindOrCreateLight()
        {
            if (FindRoot("Directional Light") != null)
                return;

            var go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>아군 뒤쪽 위에서 싸움터 전체를 내려다본다. 세로 화면이라 앞뒤(Z)로 길게 담는다.</summary>
        private static void FindOrCreateCamera()
        {
            if (FindRoot("BattleCamera") != null)
                return;

            var go = new GameObject("BattleCamera");
            go.tag = "MainCamera";

            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.66f, 0.74f);
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 200f;

            go.transform.position = new Vector3(0f, 26f, -22f);
            go.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
        }

        private static void FindOrCreateGround()
        {
            if (FindRoot("Ground") != null)
                return;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat("BattleGround", new Color(0.58f, 0.52f, 0.38f));
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // 가운데 선 — 양 진영이 어디서 만나는지 읽히게.
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "CenterLine";
            line.transform.SetParent(ground.transform, false);
            line.transform.localScale = new Vector3(4f, 0.01f, 0.02f);
            line.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            line.GetComponent<Renderer>().sharedMaterial = Mat("BattleLine", new Color(0.48f, 0.42f, 0.30f));
            Object.DestroyImmediate(line.GetComponent<Collider>());
        }

        private static BattlePresenter FindOrCreatePresenter()
        {
            GameObject existing = FindRoot("BattlePresenter");
            if (existing != null)
            {
                var found = existing.GetComponent<BattlePresenter>();
                if (found != null)
                    return found;
            }

            var go = new GameObject("BattlePresenter");
            return go.AddComponent<BattlePresenter>();
        }

        private static void BuildHud(BattlePresenter presenter)
        {
            HudSceneBuilder.EnsureKoreanFont();
            float hudScale = HudSceneBuilder.ExistingHudScale();
            HudSceneBuilder.DestroyIfExists("HUD");
            HudSceneBuilder.DestroyIfExists("EventSystem");
            HudSceneBuilder.CreateEventSystem();
            RectTransform canvas = HudSceneBuilder.CreateCanvas(hudScale);

            var text = new Color(0.96f, 0.95f, 0.90f);
            var dim = new Color(0.82f, 0.80f, 0.72f);
            var gold = new Color(0.85f, 0.65f, 0.25f);
            var red = new Color(0.70f, 0.28f, 0.22f);
            var grey = new Color(0.30f, 0.28f, 0.26f);

            // 위 — 스테이지 / 전투력 / 진행
            RectTransform top = HudSceneBuilder.CreatePanel(
                canvas, "TopPanel",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -24f), sizeDelta: new Vector2(-48f, 170f));

            TMP_Text title = HudSceneBuilder.CreateText(top, "TitleText", "", 36, FontStyles.Bold, text,
                TextAlignmentOptions.Midline, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(-32f, 50f));
            TMP_Text power = HudSceneBuilder.CreateText(top, "PowerText", "", 30, FontStyles.Normal, text,
                TextAlignmentOptions.Midline, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -66f), new Vector2(-32f, 44f));
            TMP_Text status = HudSceneBuilder.CreateText(top, "StatusText", "", 26, FontStyles.Normal, dim,
                TextAlignmentOptions.Midline, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -114f), new Vector2(-32f, 40f));

            // 아래 — 싸우기 전
            RectTransform ready = BottomPanel(canvas, "ReadyPanel", 260f);
            TMP_Text readyText = HudSceneBuilder.CreateText(ready, "ReadyText", "", 26, FontStyles.Normal, text,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(-48f, 110f));
            Button start = HudSceneBuilder.CreateButton(ready, "StartButton", "전투 시작", out _,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(300f, 96f),
                fillColor: red, labelFontSize: 34);
            Button back = HudSceneBuilder.CreateButton(ready, "BackButton", "돌아가기", out _,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(220f, 96f),
                fillColor: grey);

            // 아래 — 싸우는 중
            RectTransform fight = BottomPanel(canvas, "FightPanel", 140f);
            Button speed = HudSceneBuilder.CreateButton(fight, "SpeedButton", "x1", out TMP_Text speedLabel,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(180f, 92f),
                fillColor: grey, labelFontSize: 32);
            Button skip = HudSceneBuilder.CreateButton(fight, "SkipButton", "건너뛰기", out _,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(240f, 92f),
                fillColor: grey);

            // 결과
            RectTransform result = BottomPanel(canvas, "ResultPanel", 340f);
            TMP_Text resultTitle = HudSceneBuilder.CreateText(result, "ResultTitle", "", 56, FontStyles.Bold, text,
                TextAlignmentOptions.Midline, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(-48f, 70f));
            TMP_Text resultBody = HudSceneBuilder.CreateText(result, "ResultBody", "", 26, FontStyles.Normal, dim,
                TextAlignmentOptions.Top, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -100f), new Vector2(-48f, 100f));
            Button home = HudSceneBuilder.CreateButton(result, "HomeButton", "기지로", out _,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(240f, 96f),
                fillColor: gold);
            Button next = HudSceneBuilder.CreateButton(result, "NextButton", "다음 스테이지", out _,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(300f, 96f),
                fillColor: red, labelFontSize: 30);

            var controller = new GameObject("BattleHudController");
            controller.transform.SetParent(canvas, false);
            var hud = controller.AddComponent<BattleHudView>();

            var so = new SerializedObject(hud);
            TerritorySceneBuilder.Wire(so, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so, "_strings", TerritorySceneBuilder.LoadRequired<TextAsset>("Assets/Data/strings-ko.json"));
            TerritorySceneBuilder.Wire(so, "_titleText", title);
            TerritorySceneBuilder.Wire(so, "_powerText", power);
            TerritorySceneBuilder.Wire(so, "_statusText", status);
            TerritorySceneBuilder.Wire(so, "_readyPanel", ready.gameObject);
            TerritorySceneBuilder.Wire(so, "_readyText", readyText);
            TerritorySceneBuilder.Wire(so, "_startButton", start);
            TerritorySceneBuilder.Wire(so, "_backButton", back);
            TerritorySceneBuilder.Wire(so, "_fightPanel", fight.gameObject);
            TerritorySceneBuilder.Wire(so, "_speedButton", speed);
            TerritorySceneBuilder.Wire(so, "_speedLabel", speedLabel);
            TerritorySceneBuilder.Wire(so, "_skipButton", skip);
            TerritorySceneBuilder.Wire(so, "_resultPanel", result.gameObject);
            TerritorySceneBuilder.Wire(so, "_resultTitle", resultTitle);
            TerritorySceneBuilder.Wire(so, "_resultBody", resultBody);
            TerritorySceneBuilder.Wire(so, "_homeButton", home);
            TerritorySceneBuilder.Wire(so, "_nextButton", next);
            so.ApplyModifiedPropertiesWithoutUndo();

            fight.gameObject.SetActive(false);
            result.gameObject.SetActive(false);
        }

        private static RectTransform BottomPanel(RectTransform canvas, string name, float height)
        {
            return HudSceneBuilder.CreatePanel(
                canvas, name,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 24f), sizeDelta: new Vector2(-48f, height));
        }

        private static GameObject FindRoot(string name)
        {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                    return roots[i];
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                    return;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Game.World;
using Olympus.UI;

namespace Olympus.Editor
{
    /// <summary>
    /// 월드맵 씬을 조립한다. 기지 씬과 같은 방식이다 — 손으로 씬을 쓰지 않고
    /// 메뉴 명령으로 짓는다(<see cref="TerritorySceneBuilder"/>의 설명 참고).
    ///
    /// 지형은 오브젝트가 아니라 텍스처 한 장이다. 512x512면 26만 칸이라 칸마다
    /// 오브젝트를 만들면 씬이 열리지 않는다(<see cref="WorldMapTexture"/>).
    /// </summary>
    public static class WorldSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/World.unity";
        private const string MaterialPath = "Assets/Settings/WorldMap.mat";

        [MenuItem("Olympus/Setup/월드맵 씬 만들기", false, 110)]
        public static void BuildWorldScene()
        {
            AssetDatabase.Refresh();

            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Settings");

            Material material = CreateOrLoadMapMaterial();

            UnityEngine.SceneManagement.Scene scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 지도 판·카메라는 손으로 맞춘 값(씨앗·높이 구간·줌)이 있을 수 있어
            // 있으면 그대로 두고, HUD만 매번 다시 짓는다 — 기지 씬과 같은 규칙이다.
            WorldMapView world = FindOrCreateWorld(material);
            WorldCamera camera = FindOrCreateCamera(world);
            WorldPresenter presenter = FindOrCreatePresenter(world, camera);

            BuildHud(presenter);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "월드맵 씬을 준비했습니다: " + ScenePath +
                "\n지형은 텍스처 한 장으로 굽습니다 — 칸마다 오브젝트를 만들지 않습니다." +
                "\n드래그로 팬, 휠(또는 두 손가락)로 줌. 칸을 탭하면 좌표와 내용이 뜹니다.");

            EditorGUIUtility.PingObject(world.gameObject);
        }

        private static WorldMapView FindOrCreateWorld(Material material)
        {
            GameObject existing = FindRoot("WorldMap");
            if (existing != null)
            {
                var found = existing.GetComponent<WorldMapView>();
                if (found != null)
                    return found;
            }

            var go = new GameObject("WorldMap");
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            renderer.sharedMaterial = material;

            // 지도는 빛을 받지 않는다 — 색이 그대로 나와야 지형이 구분된다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            WorldMapView view = go.AddComponent<WorldMapView>();

            var so = new SerializedObject(view);
            TerritorySceneBuilder.Wire(so, "_renderer", renderer);
            TerritorySceneBuilder.Wire(so, "_meshFilter", filter);
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        private static WorldCamera FindOrCreateCamera(WorldMapView world)
        {
            GameObject existing = FindRoot("WorldCamera");
            if (existing != null)
            {
                var found = existing.GetComponent<WorldCamera>();
                if (found != null)
                    return found;
            }

            var go = new GameObject("WorldCamera");
            go.tag = "MainCamera";

            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
            camera.orthographic = true;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 500f;

            WorldCamera rig = go.AddComponent<WorldCamera>();

            var so = new SerializedObject(rig);
            TerritorySceneBuilder.Wire(so, "_world", world);
            so.ApplyModifiedPropertiesWithoutUndo();

            return rig;
        }

        private static WorldPresenter FindOrCreatePresenter(WorldMapView world, WorldCamera camera)
        {
            GameObject existing = FindRoot("WorldPresenter");
            if (existing != null)
            {
                var found = existing.GetComponent<WorldPresenter>();
                if (found != null)
                    return found;
            }

            var go = new GameObject("WorldPresenter");
            WorldPresenter presenter = go.AddComponent<WorldPresenter>();

            var so = new SerializedObject(presenter);
            TerritorySceneBuilder.Wire(so, "_world", world);
            TerritorySceneBuilder.Wire(so, "_camera", camera);
            so.ApplyModifiedPropertiesWithoutUndo();

            return presenter;
        }

        private static void BuildHud(WorldPresenter presenter)
        {
            HudSceneBuilder.EnsureKoreanFont();

            HudSceneBuilder.DestroyIfExists("HUD");
            HudSceneBuilder.DestroyIfExists("EventSystem");

            HudSceneBuilder.CreateEventSystem();
            RectTransform canvas = HudSceneBuilder.CreateCanvas();

            // 고른 칸 정보 — 좌표와 거기 있는 것.
            RectTransform info = HudSceneBuilder.CreatePanel(
                canvas, "TileInfoPanel",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 40f), sizeDelta: new Vector2(620f, 190f));

            TMP_Text coordText = HudSceneBuilder.CreateText(
                info, "CoordText", "", 34, FontStyles.Bold,
                new Color(0.96f, 0.95f, 0.90f), TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -16f), sizeDelta: new Vector2(-40f, 46f));

            TMP_Text contentText = HudSceneBuilder.CreateText(
                info, "ContentText", "", 26, FontStyles.Normal,
                new Color(0.78f, 0.76f, 0.68f), TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -66f), sizeDelta: new Vector2(-40f, 40f));

            TMP_Text enterLabel;
            Button enterButton = HudSceneBuilder.CreateButton(
                info, "EnterBaseButton", "들어가기", out enterLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-20f, 16f), sizeDelta: new Vector2(200f, 58f),
                fillColor: new Color(0.30f, 0.55f, 0.30f));

            // 기지로 — 항상 보인다. 월드에서 길을 잃지 않게 하는 유일한 출구다.
            TMP_Text backLabel;
            Button backButton = HudSceneBuilder.CreateButton(
                canvas, "BackButton", "기지로", out backLabel,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f), pivot: new Vector2(0f, 0f),
                anchoredPos: new Vector2(24f, 24f), sizeDelta: new Vector2(200f, 76f),
                fillColor: new Color(0.85f, 0.65f, 0.25f));

            var controller = new GameObject("WorldHudController");
            controller.transform.SetParent(canvas, false);

            var hud = controller.AddComponent<WorldHudView>();
            var so = new SerializedObject(hud);
            TerritorySceneBuilder.Wire(so, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so, "_infoRoot", info.gameObject);
            TerritorySceneBuilder.Wire(so, "_coordText", coordText);
            TerritorySceneBuilder.Wire(so, "_contentText", contentText);
            TerritorySceneBuilder.Wire(so, "_enterBaseButton", enterButton);
            TerritorySceneBuilder.Wire(so, "_enterBaseLabel", enterLabel);
            TerritorySceneBuilder.Wire(so, "_backButton", backButton);
            TerritorySceneBuilder.Wire(so, "_backLabel", backLabel);
            so.ApplyModifiedPropertiesWithoutUndo();

            info.gameObject.SetActive(false);
        }

        /// <summary>
        /// 지도용 머티리얼. 조명을 받지 않는 Unlit이어야 지형 색이 그대로 나온다 —
        /// Lit으로 두면 빛 방향에 따라 지도 색이 달라져 지형을 색으로 구분할 수 없다.
        /// </summary>
        private static Material CreateOrLoadMapMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture");

            if (shader == null)
            {
                Debug.LogWarning("Unlit 셰이더를 못 찾아 기본 셰이더로 대체합니다.");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = "WorldMap" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static GameObject FindRoot(string name)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();

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
            string leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
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

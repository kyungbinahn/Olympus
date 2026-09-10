using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Olympus.Game.Territory;

namespace Olympus.Editor
{
    /// <summary>
    /// 기지 씬을 프로그램으로 조립한다.
    ///
    /// 씬 파일을 손으로 쓰지 않는 이유 — .unity는 GUID·fileID로 얽힌 YAML이라
    /// 손으로 만들면 조용히 깨지는 부류의 사고가 난다(Cat이 meta GUID 재발급으로
    /// 씬이 프리팹 8종을 잃었고, 에러가 한 줄도 안 났다).
    /// 메뉴 명령으로 두면 재실행 가능하고, 코드라서 diff로 읽히고 리뷰된다.
    /// </summary>
    public static class TerritorySceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Territory.unity";
        private const string DevastatedMaterialPath = "Assets/Settings/GroundDevastated.mat";
        private const string RestoredMaterialPath = "Assets/Settings/GroundRestored.mat";
        private const string DevastatedTexturePath = "Assets/Settings/GridCheckerDevastated.asset";
        private const string RestoredTexturePath = "Assets/Settings/GridCheckerRestored.asset";

        // 황폐 — 마르고 갈라진 땅. 배경 아트 디렉션의 "누렇게 마른 목초, 갈라진 땅".
        private static readonly Color DevastatedLight = new Color(0.60f, 0.55f, 0.42f);
        private static readonly Color DevastatedDark = new Color(0.52f, 0.47f, 0.36f);

        // 복구 — 되찾은 녹지.
        private static readonly Color RestoredLight = new Color(0.44f, 0.60f, 0.34f);
        private static readonly Color RestoredDark = new Color(0.37f, 0.52f, 0.28f);

        [MenuItem("Olympus/Setup/기지 씬 만들기", false, 100)]
        public static void BuildTerritoryScene()
        {
            if (File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "기지 씬 다시 만들기",
                    ScenePath + " 가 이미 있습니다.\n\n" +
                    "다시 만들면 그 씬에 손으로 넣은 것이 모두 사라집니다.\n계속할까요?",
                    "다시 만들기", "취소");

                if (!overwrite)
                    return;
            }

            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Settings");

            Material devastated = CreateOrLoadGroundMaterial(
                DevastatedMaterialPath, DevastatedTexturePath, "GroundDevastated",
                DevastatedLight, DevastatedDark);

            Material restored = CreateOrLoadGroundMaterial(
                RestoredMaterialPath, RestoredTexturePath, "GroundRestored",
                RestoredLight, RestoredDark);

            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLight();
            TerritoryGround ground = CreateGround(devastated, restored);
            CreateCamera(ground);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();

            Debug.Log(
                "기지 씬을 만들었습니다: " + ScenePath +
                "\n격자 " + ground.BaseSize + "x" + ground.BaseSize +
                " (칸 " + ground.Grid.CellSize + " 유닛), 범위 " + ground.Bounds +
                "\n좌클릭 드래그로 팬, 휠로 줌.");

            EditorGUIUtility.PingObject(ground.gameObject);
        }

        private static void CreateLight()
        {
            var go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            // 그림자가 건물 형태를 읽게 해 주는 각도. 정오 수직광은 형태가 납작해 보인다.
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static TerritoryGround CreateGround(Material devastated, Material restored)
        {
            var go = new GameObject("TerritoryGround");
            go.AddComponent<MeshFilter>();

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();

            // 순서가 서브메쉬 번호와 같아야 한다 — 0=황폐, 1=녹지.
            // 개수가 어긋나면 Unity가 조용히 첫 머티리얼로 덮어 그린다.
            renderer.sharedMaterials = new[] { devastated, restored };

            // 지면은 그림자를 받기만 한다 — 드리울 대상이 없다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            TerritoryGround ground = go.AddComponent<TerritoryGround>();
            ground.Rebuild();

            return ground;
        }

        private static void CreateCamera(TerritoryGround ground)
        {
            var go = new GameObject("TerritoryCamera");
            go.tag = "MainCamera";

            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;

            // 회색 하늘 — 그레이박스 단계에서는 배경이 지면과 구분만 되면 된다.
            camera.backgroundColor = new Color(0.24f, 0.26f, 0.28f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 45f;

            TerritoryCamera rig = go.AddComponent<TerritoryCamera>();

            // 팬 범위를 기지에 묶어 주려면 지면을 알아야 한다.
            SerializedObject so = new SerializedObject(rig);
            so.FindProperty("_ground").objectReferenceValue = ground;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material CreateOrLoadGroundMaterial(
            string materialPath, string texturePath, string name, Color light, Color dark)
        {
            Texture2D checker = CreateOrLoadCheckerTexture(texturePath, name + "Checker", light, dark);

            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                existing.mainTexture = checker;
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("URP Lit 셰이더를 못 찾아 기본 셰이더로 대체합니다.");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = name };
            material.mainTexture = checker;

            // 그레이박스 지면 — 광택 없이 무광. 아트가 들어오면 이 머티리얼을 교체한다.
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        /// <summary>
        /// 2×2 체커 텍스처. 지면 메쉬가 UV를 칸마다 0~1로 깔아 주므로
        /// 이 텍스처를 반복으로 물리면 칸 경계가 그대로 눈에 보인다 — 셰이더 작업이 필요 없다.
        /// </summary>
        private static Texture2D CreateOrLoadCheckerTexture(
            string texturePath, string name, Color light, Color dark)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (existing != null)
                return existing;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = name,

                // 칸 경계가 흐려지면 격자를 읽을 수 없다 — 반드시 Point.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };

            tex.SetPixels(new[] { light, dark, dark, light });
            tex.Apply();

            AssetDatabase.CreateAsset(tex, texturePath);
            return tex;
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

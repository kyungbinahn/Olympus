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
            // 데이터 JSON을 Unity 밖에서 만들었을 수 있다 — 임포트되기 전이면
            // LoadAssetAtPath가 null을 주고, 기지가 조용히 비어 있게 뜬다.
            AssetDatabase.Refresh();

            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Settings");

            Material devastated = CreateOrLoadGroundMaterial(
                DevastatedMaterialPath, DevastatedTexturePath, "GroundDevastated",
                DevastatedLight, DevastatedDark);

            Material restored = CreateOrLoadGroundMaterial(
                RestoredMaterialPath, RestoredTexturePath, "GroundRestored",
                RestoredLight, RestoredDark);

            // 씬 파일이 이미 있으면 그 씬을 그대로 연다(새로 밀지 않는다) — 아래
            // FindOrCreate* 들이 이미 있는 오브젝트는 손대지 않고 넘어간다. 그래서
            // 카메라 화각·복구 반경처럼 인스펙터에서 눈으로 맞춘 값이 다시 실행해도 남는다.
            UnityEngine.SceneManagement.Scene scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            FindOrCreateLight();
            TerritoryGround ground = FindOrCreateGround(devastated, restored);
            FindOrCreateCamera(ground);
            TerritoryPresenter presenter = FindOrCreatePresenter(ground);

            // HUD는 전부 코드가 소유한 내용이라(손으로 맞출 값이 없다) 매번 통째로 다시 짓는다.
            HudSceneBuilder.Build(presenter);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();

            Debug.Log(
                "기지 씬을 준비했습니다: " + ScenePath +
                "\n격자 " + ground.BaseSize + "x" + ground.BaseSize +
                " (칸 " + ground.Grid.CellSize + " 유닛), 범위 " + ground.Bounds +
                "\n좌클릭 드래그로 팬, 휠로 줌.");

            EditorGUIUtility.PingObject(ground.gameObject);
        }

        /// <summary>
        /// HUD만 다시 짓는다 — 지면·카메라·기지 데이터는 전혀 건드리지 않는다.
        /// HUD를 반복해서 고칠 때는 이 명령을 쓴다(위의 "기지 씬 만들기"는 매번 전부를 훑는다).
        /// </summary>
        [MenuItem("Olympus/Setup/HUD 다시 만들기", false, 101)]
        public static void RebuildHud()
        {
            TerritoryPresenter presenter = FindRootComponent<TerritoryPresenter>("TerritoryPresenter");

            if (presenter == null)
            {
                EditorUtility.DisplayDialog(
                    "HUD 다시 만들기",
                    "열린 씬에서 TerritoryPresenter를 찾지 못했습니다.\n" +
                    "먼저 \"기지 씬 만들기\"로 씬을 한 번 만들어야 합니다.",
                    "확인");
                return;
            }

            HudSceneBuilder.Build(presenter);

            EditorSceneManager.MarkSceneDirty(presenter.gameObject.scene);
            EditorSceneManager.SaveScene(presenter.gameObject.scene);

            Debug.Log("HUD를 다시 만들었습니다.");
        }

        private static void FindOrCreateLight()
        {
            // 세기·각도를 손으로 맞췄을 수 있다 — 있으면 그대로 둔다.
            if (FindRoot("Directional Light") != null)
                return;

            var go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            // 그림자가 건물 형태를 읽게 해 주는 각도. 정오 수직광은 형태가 납작해 보인다.
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static TerritoryGround FindOrCreateGround(Material devastated, Material restored)
        {
            TerritoryGround existing = FindRootComponent<TerritoryGround>("TerritoryGround");
            if (existing != null)
                return existing;

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

        private static void FindOrCreateCamera(TerritoryGround ground)
        {
            // 화각·줌 범위·피치를 손으로 맞췄을 수 있다 — 있으면 그대로 둔다.
            if (FindRoot("TerritoryCamera") != null)
                return;

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

        private static TerritoryPresenter FindOrCreatePresenter(TerritoryGround ground)
        {
            // 시작 자원·디버그 단축키·녹지 반경 같은 인스펙터 값을 손으로 맞췄을 수 있다.
            TerritoryPresenter existing = FindRootComponent<TerritoryPresenter>("TerritoryPresenter");
            if (existing != null)
                return existing;

            var go = new GameObject("TerritoryPresenter");
            TerritoryPresenter presenter = go.AddComponent<TerritoryPresenter>();

            var so = new SerializedObject(presenter);

            Wire(so, "_ground", ground);
            Wire(so, "_buildingDefs", LoadRequired<TextAsset>("Assets/Data/building-defs.json"));
            Wire(so, "_baseLayout", LoadRequired<TextAsset>("Assets/Data/base-layout.json"));
            Wire(so, "_strings", LoadRequired<TextAsset>("Assets/Data/strings-ko.json"));

            // 그레이박스 색 — 폐허는 어둡고 칙칙하게, 완성은 대리석처럼 밝게.
            // 복구가 진행되면 화면이 어두운 데서 밝은 쪽으로 옮겨가는 것이 눈에 보인다.
            Wire(so, "_ruinedMaterial",
                CreateOrLoadFlatMaterial("Assets/Settings/BuildingRuined.mat", "BuildingRuined",
                    new Color(0.34f, 0.31f, 0.28f)));

            Wire(so, "_constructingMaterial",
                CreateOrLoadFlatMaterial("Assets/Settings/BuildingConstructing.mat", "BuildingConstructing",
                    new Color(0.78f, 0.66f, 0.32f)));

            Wire(so, "_completeMaterial",
                CreateOrLoadFlatMaterial("Assets/Settings/BuildingComplete.mat", "BuildingComplete",
                    new Color(0.88f, 0.86f, 0.80f)));

            Wire(so, "_selectionMaterial",
                CreateOrLoadFlatMaterial("Assets/Settings/CellSelection.mat", "CellSelection",
                    new Color(0.30f, 0.85f, 0.95f)));

            so.ApplyModifiedPropertiesWithoutUndo();

            return presenter;
        }

        /// <summary>지금 열려 있는 씬의 최상위 오브젝트 중 그 이름을 찾는다. 없으면 null.</summary>
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

        private static T FindRootComponent<T>(string name) where T : Component
        {
            GameObject go = FindRoot(name);
            return go == null ? null : go.GetComponent<T>();
        }

        internal static void Wire(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);

            if (prop == null)
            {
                Debug.LogError("직렬화 필드를 못 찾았습니다: " + propertyName +
                               " — 필드 이름이 바뀌었으면 이 배선도 함께 고쳐야 합니다.");
                return;
            }

            prop.objectReferenceValue = value;
        }

        internal static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
            {
                Debug.LogError(
                    "필요한 에셋이 없습니다: " + path +
                    " — 없으면 기지가 비어 있게 뜹니다. 파일을 확인하세요.");
            }

            return asset;
        }

        /// <summary>단색 무광 머티리얼. 그레이박스 전용이고, 실제 아트가 들어오면 교체된다.</summary>
        private static Material CreateOrLoadFlatMaterial(string path, string name, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            AssetDatabase.CreateAsset(material, path);
            return material;
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

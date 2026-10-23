using System.Linq;
using Olympus.Game.Battle;
using Olympus.Game.Territory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Olympus.Editor
{
    /// <summary>
    /// 아트 납품 파일(<c>Assets/Art/Characters/</c>의 fbx·png)이 들어오거나 바뀌면 전투 씬에 저절로 다시 연결한다.
    ///
    /// 왜 자동인가 — 납품물을 복사해 두고 "메뉴를 누르세요"로 끝냈더니 메뉴가 안 눌린 채 플레이해서
    /// "적용된 게 안 보인다"가 났다(2026-10-22). 파일을 넣는 것과 게임에 나오는 것 사이에 손 단계를 두지 않는다.
    ///
    /// 더해서 소품(prop_)이 이 가져오기 규칙보다 먼저 들어와 Humanoid로 잘못 가져와진 경우를 고쳐 다시 가져온다.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class BattleArtAutoWire : AssetPostprocessor
    {
        /// <summary>
        /// 연결 방식(<see cref="BattleSoldierSetup"/>)을 바꾸면 올린다 — 코드가 다시 읽힐 때 숫자가 다르면 저절로 다시 연결한다.
        /// 아트 파일은 그대로인데 연결 코드만 바뀐 경우(정면 자동 맞춤 등)에도 메뉴를 누르지 않게.
        /// </summary>
        private const int WireVersion = 9;
        private const string VersionFile = "Library/OlympusBattleWireVersion.txt";

        static BattleArtAutoWire()
        {
            string have = System.IO.File.Exists(VersionFile) ? System.IO.File.ReadAllText(VersionFile).Trim() : "";
            if (have == WireVersion.ToString() || _scheduled)
                return;

            _scheduled = true;
            EditorApplication.delayCall += Run;
        }

        private const string CharacterFolder = "Assets/Art/Characters/";
        private const string BattleScenePath = "Assets/Scenes/Battle.unity";

        private static bool _scheduled;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // 연결이 만드는 파일(Generated 팀색 판, .mat, .controller)은 빼야 한다 — 안 빼면 연결 → 가져오기 → 연결로 돈다.
            bool relevant = imported.Concat(moved).Any(IsDeliveredArt);
            if (!relevant || _scheduled)
                return;

            _scheduled = true;
            EditorApplication.delayCall += Run;
        }

        private static bool IsDeliveredArt(string path)
        {
            if (!path.StartsWith(CharacterFolder) || path.Contains("/Generated/"))
                return false;
            return path.EndsWith(".fbx") || path.EndsWith(".png");
        }

        [MenuItem("Olympus/Setup/전투 병사·1장 화면 다시 연결 (씬 안 열어도 됨)", false, 122)]
        private static void Run()
        {
            _scheduled = false;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                // 플레이 중에는 씬을 열고 닫을 수 없다 — 플레이가 끝나면 다시 시도.
                EditorApplication.playModeStateChanged -= RetryAfterPlay;
                EditorApplication.playModeStateChanged += RetryAfterPlay;
                return;
            }

            if (FixMisimportedProps())
                return; // 다시 가져오기가 끝나면 OnPostprocessAllAssets가 또 부른다.

            WireBattleScene();
        }

        private static void RetryAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;
            EditorApplication.playModeStateChanged -= RetryAfterPlay;
            Run();
        }

        /// <summary>소품이 Humanoid로 들어와 있으면 다시 가져온다(규칙은 <see cref="CharacterModelPostprocessor"/>). 하나라도 했으면 true.</summary>
        private static bool FixMisimportedProps()
        {
            bool any = false;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharacterFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!CharacterModelPostprocessor.IsProp(path))
                    continue;

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || importer.animationType == ModelImporterAnimationType.None)
                    continue;

                Debug.Log("소품을 뼈대 없이 다시 가져옵니다: " + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                any = true;
            }
            return any;
        }

        private const string TerritoryScenePath = "Assets/Scenes/Territory.unity";

        private static void WireBattleScene()
        {
            bool battleOk = WithScene(BattleScenePath, scene =>
            {
                BattlePresenter presenter = scene.GetRootGameObjects()
                    .Select(go => go.GetComponentInChildren<BattlePresenter>(true))
                    .FirstOrDefault(p => p != null);

                if (presenter == null)
                {
                    Debug.LogWarning("전투 씬에 BattlePresenter가 없습니다 — Olympus/Setup/전투 씬 만들기를 다시 누르세요.");
                    return false;
                }

                BattleArena arena = BattleSceneBuilder.EnsureArena(presenter);
                BattleSceneBuilder.WireArenaMaterials(arena);
                BattleSoldierSetup.Wire(arena);
                return true;
            });

            // 기지 씬: 1장 오브젝트(거인·협곡 전투)와 HUD(상단 띠·훈련 칸)를 같이 맞춘다 — HUD는 코드가 전부 정하는
            // 내용이라 통째로 다시 지어도 잃는 것이 없다.
            bool territoryOk = WithScene(TerritoryScenePath, scene =>
            {
                TerritoryPresenter presenter = scene.GetRootGameObjects()
                    .Select(go => go.GetComponentInChildren<TerritoryPresenter>(true))
                    .FirstOrDefault(p => p != null);

                if (presenter == null)
                {
                    Debug.LogWarning("기지 씬에 TerritoryPresenter가 없습니다 — Olympus/Setup/기지 씬 만들기를 누르세요.");
                    return false;
                }

                HudSceneBuilder.Build(presenter);
                return true;
            });

            if (battleOk || territoryOk)
            {
                System.IO.File.WriteAllText(VersionFile, WireVersion.ToString());
                Debug.Log("병사 모델·1장 화면을 자동으로 연결했습니다 (전투 씬 " + battleOk + ", 기지 씬 " + territoryOk + ").");
            }
        }

        /// <summary>
        /// 씬을 (열려 있지 않으면 덧붙여) 열고, 잠깐 활성 씬으로 삼아 일을 시키고, 저장하고, 원래대로 돌린다.
        /// 활성 씬으로 삼는 이유 — HUD 빌더가 "활성 씬의 최상위"에서 옛 HUD를 찾아 지운다.
        /// </summary>
        private static bool WithScene(string path, System.Func<Scene, bool> work)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                return false;

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            bool ok = false;
            try
            {
                SceneManager.SetActiveScene(scene);
                ok = work(scene);
                if (ok)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (!wasOpen)
                    EditorSceneManager.CloseScene(scene, true);
            }

            return ok;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Olympus.Editor
{
    /// <summary>
    /// 씬 조립 코드(또는 조립이 읽는 UI 그림·글꼴)가 바뀌면, 코드가 다시 읽힐 때 그 씬을 저절로 다시 짓는다.
    ///
    /// 왜 — 조립 메뉴가 여덟 개로 늘어서 "무엇을 고치면 어느 메뉴를 다시 눌러야 하나"를 사람이 외우고 있었다.
    /// 안 누르고 플레이하면 옛 화면이 떠서 "고쳤다는데 그대로다"가 된다(<see cref="BattleArtAutoWire"/>와 같은 사고).
    ///
    /// 무엇이 바뀌었나는 버전 숫자가 아니라 **입력 파일 내용의 지문**으로 판단한다 — 숫자는 올리는 걸 잊는다.
    /// 지문은 Library/(기계마다 따로, 커밋 안 됨)에 둔다. 처음 여는 기계는 한 번 다 짓는데, 조립은 몇 번을
    /// 다시 해도 같은 결과라(이미 있는 오브젝트·손으로 맞춘 값은 건드리지 않는다) 손해가 없다.
    ///
    /// 손으로 맞춘 것을 지키는 규칙: 열린 씬에 저장 안 한 변경이 있으면 짓지 않고 미룬다. 조립은 씬을 통째로
    /// 열고 닫기 때문에, 그대로 진행하면 저장 안 한 작업이 묻지도 않고 사라진다.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneAutoRebuild
    {
        private const string StampFile = "Library/OlympusSceneBuildStamp.txt";

        private sealed class Target
        {
            public string Name;
            public string ScenePath;
            public Action Build;
            public string[] Inputs;
        }

        // 조립이 읽는 공통 입력 — HUD 조립 코드, 바탕 그림(9-slice 테두리가 .meta에 있다), 아이콘, 글꼴 원본.
        private static readonly string[] HudInputs =
        {
            "Assets/Editor/HudSceneBuilder.cs",
            "Assets/Art/UI/",
            "Assets/Art/Fonts/",
        };

        private static readonly Target[] Targets =
        {
            new Target
            {
                Name = "기지",
                ScenePath = "Assets/Scenes/Territory.unity",
                Build = TerritorySceneBuilder.BuildTerritoryScene,
                Inputs = HudInputs.Concat(new[]
                {
                    "Assets/Editor/TerritorySceneBuilder.cs",
                    "Assets/Editor/ChapterSceneSetup.cs",
                    // 기지 안에도 전투 무대가 둘 있다(협곡·습격) — 병사 연결이 바뀌면 기지도 다시 짓는다.
                    "Assets/Editor/BattleSceneBuilder.cs",
                    "Assets/Editor/BattleSoldierSetup.cs",
                }).ToArray(),
            },
            new Target
            {
                Name = "월드맵",
                ScenePath = "Assets/Scenes/World.unity",
                Build = WorldSceneBuilder.BuildWorldScene,
                Inputs = HudInputs.Concat(new[] { "Assets/Editor/WorldSceneBuilder.cs" }).ToArray(),
            },
            new Target
            {
                Name = "전투",
                ScenePath = "Assets/Scenes/Battle.unity",
                Build = BattleSceneBuilder.BuildBattleScene,
                Inputs = HudInputs.Concat(new[]
                {
                    "Assets/Editor/BattleSceneBuilder.cs",
                    "Assets/Editor/BattleSoldierSetup.cs",
                }).ToArray(),
            },
        };

        private static bool _waitingForSave;

        /// <summary>
        /// 이 에디터가 지금 실행 중인 조립 코드(.cs)의 내용 지문 — 코드가 읽힐 때 잰다.
        /// 디스크의 .cs가 이것과 다르면 "고친 코드가 아직 컴파일 전"이다. 그때 지으면 옛 코드로 지어 놓고
        /// 새 코드의 지문을 남겨서, 새 코드로는 영영 다시 안 짓게 된다(2026-10-26 — 플레이를 끈 순간 그렇게 됐다).
        /// </summary>
        private static readonly Dictionary<string, string> LoadedCode = new Dictionary<string, string>();

        static SceneAutoRebuild()
        {
            // 파일 가져오기용 보조 프로세스도 에디터 코드를 읽는다 — 거기서 씬을 짓게 두면 본 에디터와 같은 파일을 두고 다툰다.
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            foreach (string f in Targets.SelectMany(t => t.Inputs).Where(i => i.EndsWith(".cs")).Distinct())
                LoadedCode[f] = File.Exists(f) ? Fingerprint(new[] { f }) : "";

            EditorApplication.delayCall += TryRun;
        }

        private static string StaleCode()
        {
            foreach (KeyValuePair<string, string> kv in LoadedCode)
            {
                string now = File.Exists(kv.Key) ? Fingerprint(new[] { kv.Key }) : "";
                if (now != kv.Value)
                    return kv.Key;
            }
            return null;
        }

        [MenuItem("Olympus/Setup/바뀐 씬 지금 다시 조립", false, 99)]
        private static void RunFromMenu()
        {
            TryRun();
        }

        [MenuItem("Olympus/Setup/모든 씬 강제로 다시 조립", false, 98)]
        private static void ForceAll()
        {
            if (File.Exists(StampFile))
                File.Delete(StampFile);
            TryRun();
        }

        private static void TryRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= RetryAfterPlay;
                EditorApplication.playModeStateChanged += RetryAfterPlay;
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // 컴파일이 끝나면 코드가 다시 읽혀 정적 생성자가 또 부른다. 가져오기만 진행 중이면 잠깐 뒤에 다시.
                EditorApplication.delayCall += TryRun;
                return;
            }

            Dictionary<string, string> stamps = ReadStamps();
            List<KeyValuePair<Target, string>> pending = new List<KeyValuePair<Target, string>>();

            foreach (Target t in Targets)
            {
                string print = Fingerprint(t.Inputs);
                string have;
                if (!stamps.TryGetValue(t.Name, out have) || have != print)
                    pending.Add(new KeyValuePair<Target, string>(t, print));
            }

            if (pending.Count == 0)
                return;

            string stale = StaleCode();
            if (stale != null)
            {
                // 컴파일이 끝나 코드가 다시 읽히면 정적 생성자가 다시 부른다. 바깥에서 고친 파일은 Unity가 아직 못 봤을 수
                // 있어(창이 뒤에 있으면 자동 새로고침이 안 돈다) 새로고침을 민다.
                Debug.Log("[자동 조립] 고친 코드가 아직 컴파일 전이라 미룹니다: " + stale);
                AssetDatabase.Refresh();
                return;
            }

            if (AnyOpenSceneDirty())
            {
                if (!_waitingForSave)
                {
                    Debug.LogWarning(
                        "[자동 조립] 저장 안 한 씬 변경이 있어 미룹니다 (" + string.Join(", ", pending.Select(p => p.Key.Name)) +
                        "). 씬을 저장하면 이어서 다시 짓습니다.");
                    _waitingForSave = true;
                    EditorSceneManager.sceneSaved += RetryAfterSave;
                }
                return;
            }

            // 조립은 씬을 Single로 연다 — 끝나면 형님이 보던 씬으로 되돌린다.
            string[] openPaths = OpenScenePaths();
            string activePath = SceneManager.GetActiveScene().path;

            var done = new List<string>();
            foreach (KeyValuePair<Target, string> p in pending)
            {
                try
                {
                    p.Key.Build();
                    stamps[p.Key.Name] = p.Value;
                    WriteStamps(stamps);
                    done.Add(p.Key.Name);
                }
                catch (Exception e)
                {
                    // 지문을 안 남긴다 — 다음에 코드가 다시 읽힐 때 또 시도한다.
                    Debug.LogError("[자동 조립] " + p.Key.Name + " 씬을 짓다가 실패했습니다: " + e);
                }
            }

            RestoreScenes(openPaths, activePath);

            if (done.Count > 0)
                Debug.Log("[자동 조립] 코드가 바뀌어 다시 지었습니다: " + string.Join(", ", done));
        }

        private static void RetryAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;
            EditorApplication.playModeStateChanged -= RetryAfterPlay;
            TryRun();
        }

        private static void RetryAfterSave(Scene scene)
        {
            if (AnyOpenSceneDirty())
                return;
            EditorSceneManager.sceneSaved -= RetryAfterSave;
            _waitingForSave = false;
            EditorApplication.delayCall += TryRun;
        }

        private static bool AnyOpenSceneDirty()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }
            return false;
        }

        private static string[] OpenScenePaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                string path = SceneManager.GetSceneAt(i).path;
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            return paths.ToArray();
        }

        private static void RestoreScenes(string[] paths, string activePath)
        {
            if (paths.Length == 0)
                return;

            EditorSceneManager.OpenScene(paths[0], OpenSceneMode.Single);
            for (int i = 1; i < paths.Length; i++)
                EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Additive);

            Scene active = SceneManager.GetSceneByPath(activePath);
            if (active.IsValid() && active.isLoaded)
                SceneManager.SetActiveScene(active);
        }

        /// <summary>
        /// 입력 파일 내용의 지문. 폴더는 아래의 .cs·.png·.otf·.ttf와 그 .meta만 본다 — 글꼴 폴더의 TMP 글꼴 자산(.asset)은
        /// 게임을 돌릴 때마다 새 글자가 그려져 들어가 바뀌므로, 넣으면 열 때마다 다시 짓는 고리가 된다.
        /// </summary>
        private static string Fingerprint(string[] inputs)
        {
            var files = new List<string>();
            foreach (string input in inputs)
            {
                if (input.EndsWith("/"))
                {
                    if (!Directory.Exists(input))
                        continue;
                    files.AddRange(Directory.GetFiles(input, "*", SearchOption.AllDirectories).Where(IsFingerprinted));
                }
                else if (File.Exists(input))
                {
                    files.Add(input);
                }
            }

            files = files.Select(f => f.Replace('\\', '/')).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();

            using (var md5 = MD5.Create())
            {
                foreach (string f in files)
                {
                    byte[] name = Encoding.UTF8.GetBytes(f);
                    md5.TransformBlock(name, 0, name.Length, null, 0);
                    byte[] body = File.ReadAllBytes(f);
                    md5.TransformBlock(body, 0, body.Length, null, 0);
                }
                md5.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(md5.Hash).Replace("-", "");
            }
        }

        private static bool IsFingerprinted(string path)
        {
            string p = path.EndsWith(".meta") ? path.Substring(0, path.Length - 5) : path;
            string ext = Path.GetExtension(p).ToLowerInvariant();
            return ext == ".cs" || ext == ".png" || ext == ".otf" || ext == ".ttf";
        }

        private static Dictionary<string, string> ReadStamps()
        {
            var map = new Dictionary<string, string>();
            if (!File.Exists(StampFile))
                return map;

            foreach (string line in File.ReadAllLines(StampFile, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0)
                    map[line.Substring(0, eq)] = line.Substring(eq + 1).Trim();
            }
            return map;
        }

        private static void WriteStamps(Dictionary<string, string> stamps)
        {
            File.WriteAllLines(StampFile, stamps.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8);
        }
    }
}

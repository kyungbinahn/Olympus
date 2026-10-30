using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// 바깥(AI 작업자)이 파일로 명령을 넣으면 에디터가 실행하고 결과를 파일로 돌려준다.
    ///
    /// 왜 — 코드를 고치는 쪽은 Unity 화면을 못 보고, 그래서 "메뉴 누르고 플레이해서 봐 주세요"가
    /// 매번 사람 몫이었다. 조작(메뉴·플레이·캡처)과 확인(콘솔·컴파일 오류)을 이 통로로 넘긴다.
    ///
    /// 왜 파일인가 — 네트워크 포트·외부 패키지·별도 서버가 필요 없고, 주고받은 것이 그대로 남아 읽힌다.
    /// 명령 파일은 Temp/(커밋 안 됨, Unity가 닫힐 때 지운다)에 둔다. 바깥 쪽 도구는 tools/editor-bridge/ub.sh.
    ///
    ///   Temp/EditorBridge/inbox/{id}.json   ← 명령 { "id", "cmd", "arg" }
    ///   Temp/EditorBridge/outbox/{id}.json  → 결과 { "id", "ok", "message", 상태 }
    ///   Temp/EditorBridge/console.log       → 콘솔 로그와 컴파일 오류를 계속 덧붙인다
    ///   Temp/EditorBridge/shots/            → 화면 캡처
    ///
    /// 실행할 수 있는 것은 아래 Execute의 목록뿐이고, 임의 메서드 호출(call)은 Olympus 이름공간으로 막는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorBridge
    {
        private const string Root = "Temp/EditorBridge/";
        private const string Inbox = Root + "inbox/";
        private const string Outbox = Root + "outbox/";
        private const string ShotFolder = Root + "shots/";
        private const string ConsoleLog = Root + "console.log";

        private const double PollSeconds = 0.25;
        private const double ShotTimeoutSeconds = 8.0;

        private static readonly object LogLock = new object();

        private static double _nextPoll;

        // 캡처는 다음 프레임 끝에 파일이 생긴다 — 그때까지 결과 쓰기를 미룬다.
        private static string _pendingShotId;
        private static string _pendingShotPath;
        private static double _pendingShotDeadline;

        [Serializable]
        private sealed class Command
        {
            public string id;
            public string cmd;
            public string arg;
        }

        [Serializable]
        private sealed class Result
        {
            public string id;
            public bool ok;
            public string message;
            public bool isPlaying;
            public bool isCompiling;
            public bool isUpdating;
            public string activeScene;
            public string time;
            public int frame;
            public float gameTime;
        }

        static EditorBridge()
        {
            // Unity는 파일 가져오기용 보조 프로세스(Asset Import Worker)에서도 에디터 코드를 읽는다. 거기서도 돌면
            // 보조 프로세스가 명령을 가로채 엉뚱한 상태(플레이 안 함)로 답한다 — 실제로 그렇게 헤맸다.
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            Directory.CreateDirectory(Inbox);
            Directory.CreateDirectory(Outbox);
            Directory.CreateDirectory(ShotFolder);

            AppendLog("=== 코드 다시 읽음 " + DateTime.Now.ToString("HH:mm:ss") + " ===");

            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;

            // 컴파일 오류는 logMessageReceived로 안 온다 — 따로 받아 적는다.
            CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnCompiled;
            CompilationPipeline.compilationStarted -= OnCompileStarted;
            CompilationPipeline.compilationStarted += OnCompileStarted;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;

            // 바깥에서 플레이를 시켰으면 Unity 창이 뒤에 있어도 게임이 멈추지 않아야 캡처가 된다.
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                Application.runInBackground = true;
            AppendLog("[플레이] " + change);
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] [").Append(type).Append("] ").Append(message);

            // 경고·오류만 호출 위치를 붙인다 — 일반 로그까지 붙이면 파일이 금방 부푼다.
            if (type != LogType.Log && !string.IsNullOrEmpty(stackTrace))
            {
                string[] lines = stackTrace.Split('\n');
                sb.Append("\n    ").Append(string.Join("\n    ", lines.Take(6)));
            }

            AppendLog(sb.ToString());
        }

        private static void OnCompileStarted(object context)
        {
            AppendLog("[컴파일 시작] " + DateTime.Now.ToString("HH:mm:ss") + (EditorApplication.isPlaying ? " (플레이 중!)" : ""));
        }

        private static void OnBeforeReload()
        {
            AppendLog("[코드 다시 읽기 직전] 플레이=" + EditorApplication.isPlaying + " 컴파일=" + EditorApplication.isCompiling);
        }

        private static void OnCompiled(string assembly, CompilerMessage[] messages)
        {
            AppendLog("[컴파일 끝] " + Path.GetFileName(assembly));
            foreach (CompilerMessage m in messages)
            {
                if (m.type == CompilerMessageType.Error)
                    AppendLog("[컴파일 오류] " + m.file + ":" + m.line + " " + m.message);
            }
        }

        private static void AppendLog(string line)
        {
            try
            {
                lock (LogLock)
                    File.AppendAllText(ConsoleLog, line + "\n", Encoding.UTF8);
            }
            catch (IOException)
            {
                // 바깥에서 파일을 읽는 순간과 겹쳤다 — 로그 한 줄을 놓치는 편이 에디터를 멈추는 것보다 낫다.
            }
        }

        private static void Poll()
        {
            if (_pendingShotId != null)
            {
                FinishShotIfReady();
                return;
            }

            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

            if (!Directory.Exists(Inbox))
                return;

            string file = Directory.GetFiles(Inbox, "*.json").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (file == null)
                return;

            Command command;
            try
            {
                command = JsonUtility.FromJson<Command>(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (Exception e)
            {
                File.Delete(file);
                AppendLog("[브리지] 명령 파일을 못 읽음: " + Path.GetFileName(file) + " " + e.Message);
                return;
            }

            // 실행 전에 지운다 — 명령이 코드 다시 읽기(플레이·새로고침)를 일으키면 같은 명령이 끝없이 다시 돈다.
            File.Delete(file);

            if (command == null || string.IsNullOrEmpty(command.id))
                return;

            try
            {
                Execute(command);
            }
            catch (Exception e)
            {
                Reply(command.id, false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Execute(Command c)
        {
            switch (c.cmd)
            {
                case "ping":
                    Reply(c.id, true, "pong");
                    return;

                case "refresh":
                    // 결과를 먼저 쓴다 — 새로고침이 컴파일을 부르면 이 코드가 다시 읽혀 이어서 쓸 수 없다.
                    Reply(c.id, true, "새로고침 요청함 — 컴파일이 끝날 때까지 ping으로 기다릴 것");
                    AssetDatabase.Refresh();
                    return;

                case "menu":
                    bool ran = EditorApplication.ExecuteMenuItem(c.arg);
                    Reply(c.id, ran, ran ? "실행함: " + c.arg : "그런 메뉴가 없음: " + c.arg);
                    return;

                case "play":
                    Reply(c.id, true, "플레이 시작 요청함");
                    EditorApplication.isPlaying = true;
                    return;

                case "stop":
                    Reply(c.id, true, "플레이 정지 요청함");
                    EditorApplication.isPlaying = false;
                    return;

                case "open":
                    // 저장 안 한 변경이 있으면 열지 않는다 — "저장할까요?" 창을 띄우면 아무도 안 눌러서 에디터가 멈추고,
                    // 묻지 않고 열면 사람의 작업이 사라진다.
                    if (EditorApplication.isPlaying || AnySceneDirty())
                    {
                        Reply(c.id, false, "플레이 중이거나 저장 안 한 씬 변경이 있어 못 엶");
                        return;
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(c.arg);
                    Reply(c.id, true, "열었음: " + c.arg);
                    return;

                case "shot":
                    StartShot(c);
                    return;

                case "call":
                    Reply(c.id, true, CallStatic(c.arg));
                    return;

                default:
                    Reply(c.id, false, "모르는 명령: " + c.cmd + " (ping, refresh, menu, play, stop, open, shot, call)");
                    return;
            }
        }

        private static bool AnySceneDirty()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }
            return false;
        }

        private static void StartShot(Command c)
        {
            if (!EditorApplication.isPlaying)
            {
                Reply(c.id, false, "캡처는 플레이 중에만 됩니다 — 먼저 play");
                return;
            }

            string name = string.IsNullOrEmpty(c.arg) ? DateTime.Now.ToString("HHmmss") : c.arg;
            string path = Path.GetFullPath(ShotFolder + name + ".png");
            if (File.Exists(path))
                File.Delete(path);

            ScreenCapture.CaptureScreenshot(path);

            _pendingShotId = c.id;
            _pendingShotPath = path;
            _pendingShotDeadline = EditorApplication.timeSinceStartup + ShotTimeoutSeconds;

            // 창이 뒤에 있으면 게임 화면이 다시 그려지지 않아 캡처가 안 끝난다 — 한 번 더 그리게 민다.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void FinishShotIfReady()
        {
            bool exists = File.Exists(_pendingShotPath) && new FileInfo(_pendingShotPath).Length > 0;
            bool late = EditorApplication.timeSinceStartup > _pendingShotDeadline;

            if (!exists && !late)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }

            Reply(_pendingShotId, exists, exists ? _pendingShotPath : "캡처 파일이 안 생김 (게임 화면 탭이 닫혀 있는지 확인)");
            _pendingShotId = null;
            _pendingShotPath = null;
        }

        /// <summary>
        /// "Olympus.이름공간.클래스.메서드 인자1 인자2" — 정적 메서드만. 인자는 메서드의 형식(int·float·bool·string)으로 바꾼다.
        /// 아무 메서드나 부를 수 있으면 통로가 너무 넓어져서 Olympus 이름공간으로 묶는다.
        /// </summary>
        private static string CallStatic(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                throw new ArgumentException("call 뒤에 '클래스.메서드 인자...'가 필요");

            string[] parts = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string full = parts[0];
            int dot = full.LastIndexOf('.');
            if (dot <= 0)
                throw new ArgumentException("'클래스.메서드' 모양이 아님: " + full);

            string typeName = full.Substring(0, dot);
            string methodName = full.Substring(dot + 1);
            if (!typeName.StartsWith("Olympus."))
                throw new ArgumentException("Olympus 이름공간만 부를 수 있음: " + typeName);

            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, false))
                .FirstOrDefault(t => t != null);
            if (type == null)
                throw new ArgumentException("그런 클래스가 없음: " + typeName);

            string[] args = parts.Skip(1).ToArray();
            MethodInfo method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
            if (method == null)
                throw new ArgumentException("인자 " + args.Length + "개짜리 정적 메서드가 없음: " + full);

            ParameterInfo[] ps = method.GetParameters();
            var values = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
                values[i] = Convert.ChangeType(args[i], ps[i].ParameterType, System.Globalization.CultureInfo.InvariantCulture);

            object result;
            try
            {
                result = method.Invoke(null, values);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }

            return result == null ? "(반환값 없음)" : result.ToString();
        }

        private static void Reply(string id, bool ok, string message)
        {
            var r = new Result
            {
                id = id,
                ok = ok,
                message = message,
                isPlaying = EditorApplication.isPlaying,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                time = DateTime.Now.ToString("HH:mm:ss"),
                frame = Time.frameCount,
                gameTime = Time.time,
            };

            // 임시 이름으로 다 쓴 뒤 바꾼다 — 바깥이 반쯤 쓴 파일을 읽지 않게.
            string tmp = Outbox + id + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(r), Encoding.UTF8);
            string final = Outbox + id + ".json";
            if (File.Exists(final))
                File.Delete(final);
            File.Move(tmp, final);
        }
    }
}

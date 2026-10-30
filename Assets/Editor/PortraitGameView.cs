using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// 에디터 Game 창을 세로 1080×1920으로 고정한다.
    ///
    /// 왜 필요한가 — 이 게임은 세로 전용이다(Player Settings의 화면 방향이 Portrait, 회전 잠금).
    /// 그런데 에디터의 Game 창 크기는 프로젝트 설정이 아니라 <b>에디터 개인 설정</b>이라 레포에 안 실리고,
    /// 기본값 "Free Aspect"는 창 모양 그대로 늘어난다 — 가로로 넓은 창에서 플레이하면 화면이 와이드로 보이고,
    /// HUD(1080×1920 기준, CanvasScaler)도 그 비율에 맞춰 엉뚱하게 펼쳐진다(2026-10-20에 밟았다).
    /// 그래서 에디터가 켜질 때 세로 크기를 만들어 고른다.
    ///
    /// Unity가 Game 창 크기 API를 공개하지 않아서 내부 타입을 리플렉션으로 건드린다. Unity 버전이 바뀌어
    /// 실패하면 경고만 남기고 넘어간다 — 그때는 Game 창 위쪽 해상도 메뉴에서 1080x1920을 직접 고르면 된다.
    /// </summary>
    [InitializeOnLoad]
    internal static class PortraitGameView
    {
        private const int Width = 1080;
        private const int Height = 1920;
        private const string Label = "Olympus 세로";
        private const string SessionKey = "Olympus.PortraitGameView.Applied";

        static PortraitGameView()
        {
            // 에디터를 켤 때 한 번만 — 그 뒤에 형님이 다른 크기를 고르면 그걸 존중한다.
            if (SessionState.GetBool(SessionKey, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (Apply(false))
                    SessionState.SetBool(SessionKey, true);
            };
        }

        [MenuItem("Olympus/Setup/게임 화면 세로(1080x1920)로", false, 130)]
        private static void ApplyFromMenu()
        {
            Apply(true);
        }

        private static bool Apply(bool verbose)
        {
            try
            {
                Assembly editor = typeof(EditorWindow).Assembly;
                Type sizesType = editor.GetType("UnityEditor.GameViewSizes");
                Type sizeType = editor.GetType("UnityEditor.GameViewSize");
                Type sizeKindType = editor.GetType("UnityEditor.GameViewSizeType");
                Type gameViewType = editor.GetType("UnityEditor.GameView");

                object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                    .GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);

                object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
                object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
                Type groupClass = group.GetType();

                int index = FindSize(group, groupClass);
                if (index < 0)
                {
                    object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
                    object size = Activator.CreateInstance(sizeType, fixedResolution, Width, Height, Label);
                    groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                    index = FindSize(group, groupClass);
                }

                UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(gameViewType);
                if (views.Length == 0)
                {
                    // Game 창이 아직 안 열렸다 — 크기만 만들어 두고, 다음에 메뉴로 고르면 된다.
                    if (verbose)
                        Debug.Log("Game 창이 열려 있지 않습니다. Game 창을 연 뒤 이 메뉴를 다시 눌러 주세요.");
                    return false;
                }

                PropertyInfo selected = gameViewType.GetProperty("selectedSizeIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                foreach (UnityEngine.Object view in views)
                {
                    selected.SetValue(view, index);
                    ((EditorWindow)view).Repaint();
                }

                if (verbose)
                    Debug.Log("Game 창을 세로 " + Width + "x" + Height + "로 맞췄습니다.");

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "Game 창을 세로로 맞추지 못했습니다(Unity 내부 구조가 바뀐 것 같습니다). " +
                    "Game 창 위쪽 해상도 메뉴에서 1080x1920을 직접 골라 주세요.\n" + e.Message);
                return false;
            }
        }

        /// <summary>그 크기가 이미 목록에 있으면 번호, 없으면 -1. 같은 크기를 두 번 넣지 않으려는 것이다.</summary>
        private static int FindSize(object group, Type groupClass)
        {
            int count = (int)groupClass.GetMethod("GetTotalCount").Invoke(group, null);
            MethodInfo get = groupClass.GetMethod("GetGameViewSize");

            for (int i = 0; i < count; i++)
            {
                object size = get.Invoke(group, new object[] { i });
                Type t = size.GetType();
                int w = (int)t.GetProperty("width").GetValue(size);
                int h = (int)t.GetProperty("height").GetValue(size);

                if (w == Width && h == Height)
                    return i;
            }

            return -1;
        }
    }
}

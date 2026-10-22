using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Olympus.Game.Territory;
using Olympus.UI;

namespace Olympus.Editor
{
    /// <summary>
    /// 기지 씬의 HUD(Canvas + 자원 표시 + 선택 패널)를 조립한다. <see cref="TerritorySceneBuilder"/>가
    /// 씬을 새로 만들 때 함께 부른다.
    ///
    /// 이 프로젝트가 새 Input System 전용(activeInputHandler: 1)이라 EventSystem에는
    /// 레거시 StandaloneInputModule이 아니라 InputSystemUIInputModule을 달아야
    /// 버튼이 클릭을 받는다.
    /// </summary>
    internal static class HudSceneBuilder
    {
        private const string KoreanFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Pretendard-Black SDF.asset";

        private static readonly Color PanelBackground = new Color(0.12f, 0.10f, 0.08f, 0.75f);
        private static readonly Color TextColor = new Color(0.96f, 0.95f, 0.90f);
        private static readonly Color DimTextColor = new Color(0.78f, 0.76f, 0.68f);
        private static readonly Color WarnColor = new Color(0.95f, 0.45f, 0.40f);
        private static readonly Color AccentColor = new Color(0.95f, 0.80f, 0.45f);

        /// <summary>임무 바는 내비바(높이 160) 위에 여유 16을 두고 앉는다.</summary>
        private const float QuestBarBottom = 176f;
        private const float QuestBarHeight = 110f;

        /// <summary>
        /// 유니티가 기본 제공하는 둥근 모서리 9-slice 스프라이트. 실제 아트가 들어오기 전까지
        /// 패널·버튼이 각진 사각형보다는 나아 보이게 하는 임시 그레이박스 형태다 —
        /// 건물이 원시 큐브인 것과 같은 이유(<see cref="TerritoryPresenter"/> 참고).
        /// </summary>
        private static Sprite RoundedPanelSprite =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static TMP_FontAsset _koreanFont;

        /// <summary>한글 폰트를 물어 둔다. 이걸 부르기 전에 만든 텍스트는 한글이 네모로 나온다.</summary>
        internal static void EnsureKoreanFont()
        {
            if (_koreanFont != null)
                return;

            _koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

            if (_koreanFont == null)
            {
                Debug.LogWarning(
                    "한글 폰트를 못 찾았습니다: " + KoreanFontPath +
                    " — TMP 기본 폰트(라틴 전용)로 대체되어 한글이 네모로 보일 수 있습니다.");
            }
        }

        public static void Build(TerritoryPresenter presenter)
        {
            if (presenter == null)
            {
                Debug.LogError("HUD를 배선할 TerritoryPresenter가 없습니다.");
                return;
            }

            EnsureKoreanFont();

            // 이전에 지었던 HUD가 있으면 통째로 밀고 새로 짓는다 — HUD 안에는 손으로
            // 맞출 값이 없으므로(전부 코드가 정하는 색·배치) 부분 갱신을 신경 쓸 이유가 없다.
            // 이렇게 해야 이 메서드를 몇 번을 다시 불러도 Canvas가 중복 생기지 않는다.
            DestroyIfExists("HUD");
            DestroyIfExists("EventSystem");

            CreateEventSystem();
            RectTransform canvas = CreateCanvas();

            CreateResourceBar(
                canvas, out TMP_Text hqText, out TMP_Text woodText, out TMP_Text stoneText,
                out TMP_Text foodText, out TMP_Text goldText);

            CreateQuestBar(
                canvas, out Button questBarButton, out TMP_Text questTitle, out TMP_Text questProgress,
                out Button questClaimButton);

            RectTransform infoPanel = CreateBuildingInfoPanel(
                canvas,
                out TMP_Text nameText,
                out TMP_Text statusText,
                out TMP_Text costText,
                out TMP_Text messageText,
                out Button repairButton,
                out TMP_Text repairLabel,
                out Button collectButton,
                out TMP_Text collectLabel,
                out Button[] speedUpButtons,
                out TMP_Text[] speedUpLabels);

            infoPanel.gameObject.SetActive(false);

            CreateNavBar(
                canvas,
                out Button battleButton, out Button exploreButton, out Button heroesButton, out Button allianceButton,
                out Button shopButton, out Button bagButton, out Button worldMapButton);

            // 1장(거인·훈련) — 오브젝트와 화면을 같이 맞춘다. 장 오브젝트가 없으면 여기서 세운다.
            Olympus.Game.Chapter.GiantThreatDirector director = ChapterSceneSetup.EnsureChapter(presenter);
            ChapterHudParts chapterParts = CreateChapterHud(canvas);

            Button settingsButton = CreateSettingsButton(canvas, out _);
            RectTransform toastPanel = CreateToast(canvas, out TMP_Text toastText);

            var controller = new GameObject("HudController");
            controller.transform.SetParent(canvas, false);

            var resourceView = controller.AddComponent<ResourceBarView>();
            var so1 = new SerializedObject(resourceView);
            TerritorySceneBuilder.Wire(so1, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so1, "_hqText", hqText);
            TerritorySceneBuilder.Wire(so1, "_woodText", woodText);
            TerritorySceneBuilder.Wire(so1, "_stoneText", stoneText);
            TerritorySceneBuilder.Wire(so1, "_foodText", foodText);
            TerritorySceneBuilder.Wire(so1, "_goldText", goldText);
            so1.ApplyModifiedPropertiesWithoutUndo();

            var infoView = controller.AddComponent<BuildingInfoPanelView>();
            var so2 = new SerializedObject(infoView);
            TerritorySceneBuilder.Wire(so2, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so2, "_root", infoPanel.gameObject);
            TerritorySceneBuilder.Wire(so2, "_nameText", nameText);
            TerritorySceneBuilder.Wire(so2, "_statusText", statusText);
            TerritorySceneBuilder.Wire(so2, "_costText", costText);
            TerritorySceneBuilder.Wire(so2, "_messageText", messageText);
            TerritorySceneBuilder.Wire(so2, "_repairButton", repairButton);
            TerritorySceneBuilder.Wire(so2, "_repairButtonLabel", repairLabel);
            TerritorySceneBuilder.Wire(so2, "_collectButton", collectButton);
            TerritorySceneBuilder.Wire(so2, "_collectButtonLabel", collectLabel);
            WireList(so2, "_speedUpButtons", speedUpButtons);
            WireList(so2, "_speedUpLabels", speedUpLabels);
            so2.ApplyModifiedPropertiesWithoutUndo();

            var toastView = controller.AddComponent<ToastView>();
            var so3 = new SerializedObject(toastView);
            TerritorySceneBuilder.Wire(so3, "_root", toastPanel.gameObject);
            TerritorySceneBuilder.Wire(so3, "_text", toastText);
            so3.ApplyModifiedPropertiesWithoutUndo();

            var questView = controller.AddComponent<QuestBarView>();
            var so5 = new SerializedObject(questView);
            TerritorySceneBuilder.Wire(so5, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so5, "_toast", toastView);
            TerritorySceneBuilder.Wire(so5, "_barButton", questBarButton);
            TerritorySceneBuilder.Wire(so5, "_titleText", questTitle);
            TerritorySceneBuilder.Wire(so5, "_progressText", questProgress);
            TerritorySceneBuilder.Wire(so5, "_claimButton", questClaimButton);
            so5.ApplyModifiedPropertiesWithoutUndo();

            var chapterView = controller.AddComponent<ChapterHudView>();
            var so6 = new SerializedObject(chapterView);
            TerritorySceneBuilder.Wire(so6, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so6, "_director", director);
            TerritorySceneBuilder.Wire(so6, "_raids", director != null ? director.GetComponentInChildren<Olympus.Game.Chapter.RaidDirector>(true) : null);
            TerritorySceneBuilder.Wire(so6, "_toast", toastView);
            TerritorySceneBuilder.Wire(so6, "_bannerRoot", chapterParts.Banner.gameObject);
            TerritorySceneBuilder.Wire(so6, "_bannerButton", chapterParts.BannerButton);
            TerritorySceneBuilder.Wire(so6, "_bannerTitle", chapterParts.BannerTitle);
            TerritorySceneBuilder.Wire(so6, "_bannerDetail", chapterParts.BannerDetail);
            TerritorySceneBuilder.Wire(so6, "_dispatchButton", chapterParts.DispatchButton);
            TerritorySceneBuilder.Wire(so6, "_dispatchLabel", chapterParts.DispatchLabel);
            TerritorySceneBuilder.Wire(so6, "_trainRoot", chapterParts.Train.gameObject);
            TerritorySceneBuilder.Wire(so6, "_trainInfo", chapterParts.TrainInfo);
            WireList(so6, "_trainButtons", chapterParts.TrainButtons);
            WireList(so6, "_trainLabels", chapterParts.TrainLabels);
            TerritorySceneBuilder.Wire(so6, "_battleRoot", chapterParts.Battle.gameObject);
            TerritorySceneBuilder.Wire(so6, "_speedButton", chapterParts.SpeedButton);
            TerritorySceneBuilder.Wire(so6, "_speedLabel", chapterParts.SpeedLabel);
            TerritorySceneBuilder.Wire(so6, "_skipButton", chapterParts.SkipButton);
            so6.ApplyModifiedPropertiesWithoutUndo();

            var chromeView = controller.AddComponent<HudChromeView>();
            var so4 = new SerializedObject(chromeView);
            TerritorySceneBuilder.Wire(so4, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so4, "_toast", toastView);
            TerritorySceneBuilder.Wire(so4, "_battleButton", battleButton);
            TerritorySceneBuilder.Wire(so4, "_exploreButton", exploreButton);
            TerritorySceneBuilder.Wire(so4, "_heroesButton", heroesButton);
            TerritorySceneBuilder.Wire(so4, "_allianceButton", allianceButton);
            TerritorySceneBuilder.Wire(so4, "_shopButton", shopButton);
            TerritorySceneBuilder.Wire(so4, "_bagButton", bagButton);
            TerritorySceneBuilder.Wire(so4, "_worldMapButton", worldMapButton);
            TerritorySceneBuilder.Wire(so4, "_settingsButton", settingsButton);
            so4.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class ChapterHudParts
        {
            public RectTransform Banner;
            public Button BannerButton;
            public TMP_Text BannerTitle;
            public TMP_Text BannerDetail;
            public Button DispatchButton;
            public TMP_Text DispatchLabel;
            public RectTransform Train;
            public TMP_Text TrainInfo;
            public Button[] TrainButtons;
            public TMP_Text[] TrainLabels;
            public RectTransform Battle;
            public Button SpeedButton;
            public TMP_Text SpeedLabel;
            public Button SkipButton;
        }

        /// <summary>
        /// 1장 화면 조각. 상단 띠는 자원 바 아래(거인이 보이는 동안 늘 뜸), 훈련 칸은 정보 패널 바로 위
        /// (병영을 눌렀을 때만), 전투 버튼은 오른쪽 아래. 처음엔 다 꺼 두고 <see cref="ChapterHudView"/>가 켠다.
        /// </summary>
        private static ChapterHudParts CreateChapterHud(RectTransform canvas)
        {
            var parts = new ChapterHudParts();
            var danger = new Color(0.62f, 0.20f, 0.16f);
            var blue = new Color(0.30f, 0.42f, 0.62f);

            parts.Banner = CreatePanel(
                canvas, "ChapterBanner",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -190f), sizeDelta: new Vector2(-48f, 140f));
            parts.BannerButton = parts.Banner.gameObject.AddComponent<Button>();
            parts.BannerButton.targetGraphic = parts.Banner.GetComponent<Image>();

            parts.BannerTitle = CreateText(parts.Banner, "Title", "", 36, FontStyles.Bold, TextColor,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -14f), new Vector2(-300f, 56f));
            parts.BannerTitle.richText = true;
            parts.BannerDetail = CreateText(parts.Banner, "Detail", "", 28, FontStyles.Normal, TextColor,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 14f), new Vector2(-300f, 52f));
            parts.BannerDetail.richText = true;

            parts.DispatchButton = CreateButton(parts.Banner, "DispatchButton", "협곡으로 출정!", out parts.DispatchLabel,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-16f, 0f), sizeDelta: new Vector2(260f, 100f), fillColor: danger, labelFontSize: 28);
            parts.Banner.gameObject.SetActive(false);

            // 정보 패널(380 높이) 바로 위.
            parts.Train = CreatePanel(
                canvas, "TrainingPanel",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, QuestBarBottom + QuestBarHeight + 16f + 380f + 12f), sizeDelta: new Vector2(720f, 190f));
            parts.TrainInfo = CreateText(parts.Train, "Info", "", 26, FontStyles.Normal, TextColor,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(-40f, 84f));
            parts.TrainInfo.richText = true;

            parts.TrainButtons = new Button[4];
            parts.TrainLabels = new TMP_Text[4];
            string[] names = { "TrainSmall", "TrainLarge", "TrainMax", "TrainWing" };
            for (int i = 0; i < 4; i++)
            {
                Color? fill = null;
                if (i == 3)
                    fill = blue;

                parts.TrainButtons[i] = CreateButton(parts.Train, names[i], "", out parts.TrainLabels[i],
                    anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f), pivot: new Vector2(0f, 0f),
                    anchoredPos: new Vector2(20f + i * 172f, 16f), sizeDelta: new Vector2(160f, 72f),
                    fillColor: fill, labelFontSize: 24);
            }
            parts.Train.gameObject.SetActive(false);

            parts.Battle = CreatePanel(
                canvas, "CanyonBattleControls",
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-24f, QuestBarBottom + QuestBarHeight + 16f), sizeDelta: new Vector2(360f, 104f));
            parts.SpeedButton = CreateButton(parts.Battle, "SpeedButton", "x1", out parts.SpeedLabel,
                anchorMin: new Vector2(0f, 0.5f), anchorMax: new Vector2(0f, 0.5f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(16f, 0f), sizeDelta: new Vector2(150f, 76f), fillColor: blue);
            TMP_Text skipLabel;
            parts.SkipButton = CreateButton(parts.Battle, "SkipButton", "건너뛰기", out skipLabel,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-16f, 0f), sizeDelta: new Vector2(170f, 76f), fillColor: new Color(0.30f, 0.28f, 0.26f));
            parts.Battle.gameObject.SetActive(false);

            return parts;
        }

        private static void WireList(SerializedObject so, string propertyName, Object[] values)
        {
            SerializedProperty prop = so.FindProperty(propertyName);

            if (prop == null)
            {
                Debug.LogError("직렬화 필드를 못 찾았습니다: " + propertyName +
                               " — 필드 이름이 바뀌었으면 이 배선도 함께 고쳐야 합니다.");
                return;
            }

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        internal static void DestroyIfExists(string rootName)
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                {
                    Object.DestroyImmediate(roots[i]);
                    return;
                }
            }
        }

        internal static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        internal static RectTransform CreateCanvas()
        {
            var go = new GameObject("HUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // 세로(portrait) 고정 — 기기마다 세로 길이가 더 크게 갈리므로(노치·비율 차이)
            // 너비를 기준으로 맞춘다. 높이를 기준으로 맞추면 좁은 화면에서 HUD가 옆으로 밀려난다.
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0f;

            go.AddComponent<GraphicRaycaster>();

            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 왼쪽 위 — 본부 줄 한 줄, 그 아래 자원 2열(목재|식량, 석재|금). 식량·금은 본부 Lv.5에
        /// 생기므로(ResourceBarView) 그 전에는 오른쪽 열이 비어 있다.
        /// </summary>
        private static RectTransform CreateResourceBar(
            RectTransform canvas, out TMP_Text hqText, out TMP_Text woodText, out TMP_Text stoneText,
            out TMP_Text foodText, out TMP_Text goldText)
        {
            RectTransform panel = CreatePanel(
                canvas, "ResourceBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(24f, -24f), sizeDelta: new Vector2(520f, 150f));

            hqText = CreateText(
                panel, "HqText", "본부 Lv.0", 26, FontStyles.Bold, AccentColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -8f), sizeDelta: new Vector2(-32f, 40f));

            woodText = CreateResourceCell(panel, "WoodText", "목재 0", 0, 0);
            foodText = CreateResourceCell(panel, "FoodText", "식량 0", 1, 0);
            stoneText = CreateResourceCell(panel, "StoneText", "석재 0", 0, 1);
            goldText = CreateResourceCell(panel, "GoldText", "금 0", 1, 1);

            return panel;
        }

        private static TMP_Text CreateResourceCell(RectTransform panel, string name, string text, int column, int row)
        {
            return CreateText(
                panel, name, text, 30, FontStyles.Normal, TextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(column * 0.5f, 1f), anchorMax: new Vector2(column * 0.5f + 0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -52f - row * 44f), sizeDelta: new Vector2(-32f, 40f));
        }

        /// <summary>
        /// 하단 임무 바 — 내비바 바로 위에 한 줄. 바 전체가 버튼이고(누르면 그 건물로 카메라가 간다),
        /// 오른쪽 "받기"는 그 안의 별도 버튼이다. UGUI는 겹친 버튼 중 맨 위 것만 클릭을 받으므로
        /// "받기"를 눌러도 바의 이동이 같이 일어나지 않는다.
        /// </summary>
        private static RectTransform CreateQuestBar(
            RectTransform canvas, out Button barButton, out TMP_Text title, out TMP_Text progress, out Button claimButton)
        {
            RectTransform bar = CreatePanel(
                canvas, "QuestBar",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, QuestBarBottom), sizeDelta: new Vector2(-48f, QuestBarHeight));

            barButton = bar.gameObject.AddComponent<Button>();
            barButton.targetGraphic = bar.GetComponent<Image>();

            title = CreateText(
                bar, "TitleText", "", 30, FontStyles.Bold, TextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(24f, -12f), sizeDelta: new Vector2(-232f, 44f));

            progress = CreateText(
                bar, "ProgressText", "", 24, FontStyles.Normal, DimTextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0f, 0f),
                anchoredPos: new Vector2(24f, 12f), sizeDelta: new Vector2(-232f, 36f));

            claimButton = CreateButton(
                bar, "ClaimButton", "받기", out _,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-16f, 0f), sizeDelta: new Vector2(168f, 76f),
                fillColor: new Color(0.85f, 0.65f, 0.25f));

            return bar;
        }

        private static RectTransform CreateBuildingInfoPanel(
            RectTransform canvas,
            out TMP_Text nameText,
            out TMP_Text statusText,
            out TMP_Text costText,
            out TMP_Text messageText,
            out Button repairButton,
            out TMP_Text repairLabel,
            out Button collectButton,
            out TMP_Text collectLabel,
            out Button[] speedUpButtons,
            out TMP_Text[] speedUpLabels)
        {
            // 임무 바 위에 여유 16을 두고 앉는다. 겹치면 복구 버튼과 "받기"가 같은 자리에서
            // 서로 탭을 가로챈다.
            RectTransform panel = CreatePanel(
                canvas, "BuildingInfoPanel",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, QuestBarBottom + QuestBarHeight + 16f), sizeDelta: new Vector2(720f, 380f));

            nameText = CreateText(
                panel, "NameText", "", 36, FontStyles.Bold, TextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -16f), sizeDelta: new Vector2(-40f, 48f));

            statusText = CreateText(
                panel, "StatusText", "", 26, FontStyles.Normal, DimTextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -66f), sizeDelta: new Vector2(-40f, 40f));

            // 여러 줄 — 본부는 조건 줄 두세 개 + 비용 한 줄이 들어간다. 색 표시에 TMP 리치 텍스트를 쓴다.
            costText = CreateText(
                panel, "CostText", "", 24, FontStyles.Normal, TextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -108f), sizeDelta: new Vector2(-40f, 140f));

            messageText = CreateText(
                panel, "MessageText", "", 22, FontStyles.Normal, WarnColor, TextAlignmentOptions.BottomLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 92f), sizeDelta: new Vector2(-40f, 32f));

            repairButton = CreateButton(
                panel, "RepairButton", "복구", out repairLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-20f, 16f), sizeDelta: new Vector2(190f, 64f));

            collectButton = CreateButton(
                panel, "CollectButton", "수확", out collectLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-222f, 16f), sizeDelta: new Vector2(190f, 64f),
                fillColor: new Color(0.30f, 0.45f, 0.65f));

            // 가속 버튼은 progression.json의 speedUps 수만큼 쓴다. 남는 칸은 패널이 숨긴다.
            const int speedUpSlots = 3;
            speedUpButtons = new Button[speedUpSlots];
            speedUpLabels = new TMP_Text[speedUpSlots];

            for (int i = 0; i < speedUpSlots; i++)
            {
                speedUpButtons[i] = CreateButton(
                    panel, "SpeedUpButton" + i, "", out speedUpLabels[i],
                    anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 0f), pivot: new Vector2(0f, 0f),
                    anchoredPos: new Vector2(20f + i * 124f, 16f), sizeDelta: new Vector2(112f, 64f),
                    fillColor: new Color(0.55f, 0.42f, 0.70f), labelFontSize: 20);
            }

            return panel;
        }

        /// <summary>
        /// 하단 내비바. SLG 화면 하면 떠오르는 그 줄이다 — 탐색·영웅·동맹·상점·가방은
        /// 아직 그 기능이 없어서 눌러도 토스트만 뜬다(<see cref="HudChromeView"/>).
        /// 외출(월드맵)만 색을 다르게 둔 이유는 마일스톤 ③에서 실제로 연결될 자리라서다.
        /// </summary>
        private static void CreateNavBar(
            RectTransform canvas,
            out Button battleButton, out Button exploreButton, out Button heroesButton, out Button allianceButton,
            out Button shopButton, out Button bagButton, out Button worldMapButton)
        {
            RectTransform bar = CreatePanel(
                canvas, "NavBar",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(0f, 160f));

            var neutral = new Color(0.24f, 0.22f, 0.20f);
            var highlight = new Color(0.85f, 0.65f, 0.25f);

            // 전선은 붉은 색 — 싸우러 나가는 버튼이 다른 메뉴와 섞여 보이지 않게.
            battleButton = CreateNavButton(bar, "BattleButton", "전선", 0, new Color(0.70f, 0.28f, 0.22f), out _);
            exploreButton = CreateNavButton(bar, "ExploreButton", "탐색", 1, neutral, out _);
            heroesButton = CreateNavButton(bar, "HeroesButton", "영웅", 2, neutral, out _);
            allianceButton = CreateNavButton(bar, "AllianceButton", "동맹", 3, neutral, out _);
            shopButton = CreateNavButton(bar, "ShopButton", "상점", 4, neutral, out _);
            bagButton = CreateNavButton(bar, "BagButton", "가방", 5, neutral, out _);
            worldMapButton = CreateNavButton(bar, "WorldMapButton", "외출", 6, highlight, out _);
        }

        /// <summary>내비바를 6칸으로 나눈 <paramref name="slot"/>번째 자리에 버튼을 놓는다.</summary>
        private static Button CreateNavButton(
            RectTransform bar, string name, string label, int slot, Color color, out TMP_Text labelText)
        {
            // 처음 배치일 뿐이다 — 열린 버튼만 남겨 고르게 다시 나누는 것은 HudChromeView가 한다.
            const int slotCount = 7;
            float min = (float)slot / slotCount;
            float max = (float)(slot + 1) / slotCount;

            return CreateButton(
                bar, name, label, out labelText,
                anchorMin: new Vector2(min, 0f), anchorMax: new Vector2(max, 1f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(-12f, -16f),
                fillColor: color, labelFontSize: 24);
        }

        private static Button CreateSettingsButton(RectTransform canvas, out TMP_Text label)
        {
            return CreateButton(
                canvas, "SettingsButton", "설정", out label,
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-20f, -20f), sizeDelta: new Vector2(88f, 88f),
                fillColor: new Color(0.24f, 0.22f, 0.20f), labelFontSize: 18);
        }

        private static RectTransform CreateToast(RectTransform canvas, out TMP_Text toastText)
        {
            RectTransform panel = CreatePanel(
                canvas, "Toast",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 300f), sizeDelta: new Vector2(900f, 220f));

            toastText = CreateText(
                panel, "Text", "", 28, FontStyles.Normal, TextColor, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(-40f, 0f));

            return panel;
        }

        internal static RectTransform CreatePanel(
            Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Image bg = go.AddComponent<Image>();
            bg.sprite = RoundedPanelSprite;
            bg.type = Image.Type.Sliced;
            bg.color = PanelBackground;

            return rect;
        }

        internal static TMP_Text CreateText(
            Transform parent, string name, string initialText, int fontSize, FontStyles style,
            Color color, TextAlignmentOptions alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            var text = go.AddComponent<TextMeshProUGUI>();
            if (_koreanFont != null)
                text.font = _koreanFont;
            text.text = initialText;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;

            return text;
        }

        internal static Button CreateButton(
            Transform parent, string name, string label, out TMP_Text labelText,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            Color? fillColor = null, int labelFontSize = 26)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            Image image = go.AddComponent<Image>();
            image.sprite = RoundedPanelSprite;
            image.type = Image.Type.Sliced;
            image.color = fillColor ?? new Color(0.30f, 0.55f, 0.30f);

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // 못 눌렀을 때(재료 부족)도 눈으로 구분되도록 색을 확실히 다르게 둔다.
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f);
            button.colors = colors;

            labelText = CreateText(
                go.transform, "Label", label, labelFontSize, FontStyles.Bold, Color.white, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: Vector2.zero);

            return button;
        }
    }
}

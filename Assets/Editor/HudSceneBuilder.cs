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

        // 확정 시안(OlympusArt/ui/out/HUD-mockup-v9-normal.png)의 색·배치 — compose_v7.py와 같은 값.
        private static readonly Color InkColor = new Color(0.36f, 0.20f, 0.13f);
        private static readonly Color InkDimColor = new Color(0.50f, 0.35f, 0.22f);
        private static readonly Color CreamTextColor = new Color(0.98f, 0.93f, 0.83f);
        private static readonly Color ClaimGreen = new Color(0.42f, 0.56f, 0.27f);
        private static readonly Color DispatchCream = new Color(0.97f, 0.93f, 0.84f);

        /// <summary>하단 탭 줄 높이 — 시안 176(탭 칸 160 + 위아래 여유).</summary>
        private const float NavBarHeight = 176f;

        /// <summary>임무 바(양피지) — 시안 v9: 높이 100, 탭 줄 위로 12 띄운다.</summary>
        private const float QuestBarBottom = NavBarHeight + 12f;
        private const float QuestBarHeight = 100f;

        /// <summary>전선 띠 — 위협 띠(y 182~282)와 그 원형 액자 아래 끝(y 296) 바로 밑.</summary>
        private const float FrontlineTop = 308f;
        private const float FrontlineHeight = 56f;

        /// <summary>왼쪽 열 칸(공사·훈련) — 시안 v9: 100×108, 전선 띠 아래 16부터 120 간격.</summary>
        private const float LeftTileTop = FrontlineTop + FrontlineHeight + 16f;

        /// <summary>
        /// 아트 아이콘 PNG는 사방에 12/256씩 여백이 있다 — 프레임 안쪽 면을 "꽉 채우려면" 안쪽 폭을 이 비율로 나눈다
        /// (compose_v7.py의 0.906·0.047과 같은 값). 시안 규칙: 아이콘은 프레임 밖으로 나가지 않는다.
        /// </summary>
        private const float IconInkFraction = 0.906f;
        private const float IconMarginFraction = 0.047f;

        /// <summary>v9 탭 프레임(frame_tab)의 9-slice 테두리 px — 화면 금테 두께로 나눠 배수를 정한다.</summary>
        private const float TabFrameBorderPx = 44f;

        /// <summary>스킨 조각은 2배 해상도로 그렸다(make_hud_skin.py) — 이 배수로 줄여야 테두리가 시안 두께가 된다.</summary>
        private const float SkinScale = 2f;

        /// <summary>
        /// 유니티가 기본 제공하는 둥근 모서리 9-slice 스프라이트. 실제 아트가 들어오기 전까지
        /// 패널·버튼이 각진 사각형보다는 나아 보이게 하는 임시 그레이박스 형태다 —
        /// 건물이 원시 큐브인 것과 같은 이유(<see cref="TerritoryPresenter"/> 참고).
        /// </summary>
        private static Sprite RoundedPanelSprite =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        // 글꼴 두 벌 — 본문은 Pretendard SemiBold(읽기 쉬움), 제목은 Noto Serif KR Bold(석조 비문 느낌).
        // 처음 쓰던 Pretendard-Black 한 벌은 가장 굵은 굵기에 Bold 효과까지 겹쳐 획이 뭉개졌다.
        private const string FontFolder = "Assets/Art/Fonts/";
        private const string BodyFontName = "Pretendard-SemiBold";
        private const string TitleFontName = "NotoSerifKR-Bold";

        /// <summary>이 크기 이상인 글은 제목 글꼴로 쓴다 — 부르는 쪽마다 고르게 하면 화면마다 기준이 갈린다.</summary>
        private const int TitleFontMinSize = 30;

        private static TMP_FontAsset _koreanFont;
        private static TMP_FontAsset _titleFont;

        /// <summary>한글 폰트를 물어 둔다. 이걸 부르기 전에 만든 텍스트는 한글이 네모로 나온다.</summary>
        internal static void EnsureKoreanFont()
        {
            if (_koreanFont != null)
                return;

            _koreanFont = LoadOrCreateFontAsset(BodyFontName);
            _titleFont = LoadOrCreateFontAsset(TitleFontName);

            if (_koreanFont == null)
            {
                Debug.LogWarning("본문 글꼴을 못 만들었습니다 — 예전 글꼴로 대신합니다: " + KoreanFontPath);
                _koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            }

            if (_koreanFont == null)
            {
                Debug.LogWarning(
                    "한글 폰트를 못 찾았습니다: " + KoreanFontPath +
                    " — TMP 기본 폰트(라틴 전용)로 대체되어 한글이 네모로 보일 수 있습니다.");
            }

            if (_titleFont == null)
                _titleFont = _koreanFont;
        }

        /// <summary>
        /// 글꼴 파일(.otf) 옆에 TMP 글꼴 자산이 없으면 만든다. 글자는 쓰일 때 그때그때 그려 넣는
        /// 동적 방식이다 — 한글 11,172자를 미리 다 구우면 자산이 수십 MB가 된다.
        /// 손으로 TMP Font Asset Creator를 돌리게 하지 않는 이유: 그 설정값이 어디에도 안 남아
        /// 다음 사람이 같은 자산을 다시 못 만든다.
        /// </summary>
        private static TMP_FontAsset LoadOrCreateFontAsset(string fontName)
        {
            string assetPath = FontFolder + fontName + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
                return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + fontName + ".otf");
            if (font == null)
                return null;

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(
                font, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null)
                return null;

            asset.name = fontName + " SDF";
            AssetDatabase.CreateAsset(asset, assetPath);

            // 글자판 그림과 재질은 자산 안에 같이 넣어야 저장된다 — 빠지면 씬을 다시 열 때 글자가 사라진다.
            asset.atlasTextures[0].name = fontName + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = fontName + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
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
            float hudScale = ExistingHudScale();
            DestroyIfExists("HUD");
            DestroyIfExists("EventSystem");

            CreateEventSystem();
            RectTransform canvas = CreateCanvas(hudScale);

            ResourceBarParts res = CreateResourceBar(canvas);
            CreateQueueTile(canvas, out GameObject queueTile, out TMP_Text queueText);

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
            FrontlineParts front = CreateFrontlineStrip(canvas);

            // 오른쪽 위 원형 버튼 줄 — 설정(92), 그 아래 우편·특가(84, 자리만). 시안 v9: 세 버튼 중심이 오른쪽 끝에서 60.
            Button settingsButton = CreateRoundButton(canvas, "SettingsButton", "icon_settings", "설정", new Vector2(-14f, -14f), 92f);
            Button mailButton = CreateRoundButton(canvas, "MailButton", "icon_mail", "우편", new Vector2(-18f, -124f), 84f);
            Button offerButton = CreateRoundButton(canvas, "OfferButton", "icon_offer", "특가", new Vector2(-18f, -220f), 84f);
            RectTransform toastPanel = CreateToast(canvas, out TMP_Text toastText);

            var controller = new GameObject("HudController");
            controller.transform.SetParent(canvas, false);

            var resourceView = controller.AddComponent<ResourceBarView>();
            var so1 = new SerializedObject(resourceView);
            TerritorySceneBuilder.Wire(so1, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so1, "_levelText", res.LevelText);
            TerritorySceneBuilder.Wire(so1, "_prosperityText", res.ProsperityText);
            TerritorySceneBuilder.Wire(so1, "_woodText", res.Wood);
            TerritorySceneBuilder.Wire(so1, "_stoneText", res.Stone);
            TerritorySceneBuilder.Wire(so1, "_foodText", res.Food);
            TerritorySceneBuilder.Wire(so1, "_goldText", res.Gold);
            TerritorySceneBuilder.Wire(so1, "_foodCell", res.FoodCell);
            TerritorySceneBuilder.Wire(so1, "_goldCell", res.GoldCell);
            TerritorySceneBuilder.Wire(so1, "_queueRoot", queueTile);
            TerritorySceneBuilder.Wire(so1, "_queueText", queueText);
            so1.FindProperty("_showLabels").boolValue = Icon("icon_res_wood") == null;
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
            TerritorySceneBuilder.Wire(so6, "_bannerCaption", chapterParts.BannerCaption);
            TerritorySceneBuilder.Wire(so6, "_bannerClock", chapterParts.BannerClock);
            TerritorySceneBuilder.Wire(so6, "_troopRow", chapterParts.TroopRow);
            TerritorySceneBuilder.Wire(so6, "_troopText", chapterParts.TroopText);
            TerritorySceneBuilder.Wire(so6, "_troopGaugeFill", chapterParts.TroopGaugeFill);
            TerritorySceneBuilder.Wire(so6, "_trainTile", chapterParts.TrainTile);
            TerritorySceneBuilder.Wire(so6, "_trainTileRing", chapterParts.TrainTileRing);
            TerritorySceneBuilder.Wire(so6, "_trainTileTime", chapterParts.TrainTileTime);
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

            var frontView = controller.AddComponent<FrontlineHudView>();
            var so7 = new SerializedObject(frontView);
            TerritorySceneBuilder.Wire(so7, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so7, "_toast", toastView);
            TerritorySceneBuilder.Wire(so7, "_root", front.Root);
            TerritorySceneBuilder.Wire(so7, "_button", front.Button);
            TerritorySceneBuilder.Wire(so7, "_nameText", front.Name);
            TerritorySceneBuilder.Wire(so7, "_barRoot", front.BarRoot);
            TerritorySceneBuilder.Wire(so7, "_barFill", front.BarFill);
            TerritorySceneBuilder.Wire(so7, "_statusText", front.Status);
            TerritorySceneBuilder.Wire(so7, "_warningText", front.Warning);
            so7.ApplyModifiedPropertiesWithoutUndo();

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
            TerritorySceneBuilder.Wire(so4, "_mailButton", mailButton);
            TerritorySceneBuilder.Wire(so4, "_offerButton", offerButton);
            so4.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class FrontlineParts
        {
            public GameObject Root;
            public Button Button;
            public TMP_Text Name;
            public GameObject BarRoot;
            public RectTransform BarFill;
            public TMP_Text Status;
            public TMP_Text Warning;
        }

        /// <summary>
        /// 성문 밖 전선 띠 — 시안 v9: 위협 띠 아래 한 줄(700×56, 금테 + 짙은 갈색 strip_front): 전진기지 아이콘(띠 안쪽에 꽉 차게) ·
        /// 적 기지 이름 · 체력 바 · 무너지기까지. 위협 띠의 원형 액자 아래 끝(y=296)을 피해서 앉는다.
        /// </summary>
        private static FrontlineParts CreateFrontlineStrip(RectTransform canvas)
        {
            var parts = new FrontlineParts();
            RectTransform strip = CreatePanel(
                canvas, "FrontlineStrip",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(20f, -FrontlineTop), sizeDelta: new Vector2(700f, FrontlineHeight), skin: "strip_front");
            parts.Root = strip.gameObject;
            parts.Button = MakeButton(strip.GetComponent<Image>());

            bool icon = AddIcon(strip, "icon_outpost", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), FrontlineHeight - 4f) != null;
            float nameLeft = icon ? 60f : 16f;

            parts.Name = CreateText(strip, "Name", "", 22, FontStyles.Bold, CreamTextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(nameLeft, 0f), sizeDelta: new Vector2(176f - nameLeft - 4f, 0f));

            // 체력 바: 짙은 바탕 + 붉은 채움(금 게이지 그림에 붉은빛을 곱한다 — 적의 체력이라 아군 금색과 구분).
            RectTransform bar = CreatePanel(strip, "Bar",
                anchorMin: new Vector2(0f, 0.5f), anchorMax: new Vector2(0f, 0.5f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(176f, 0f), sizeDelta: new Vector2(264f, 18f), skin: "gauge_track",
                fallbackColor: new Color(0.27f, 0.09f, 0.08f));
            bar.GetComponent<Image>().raycastTarget = false;
            parts.BarRoot = bar.gameObject;

            parts.BarFill = CreatePanel(bar, "Fill",
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(3f, 0f), sizeDelta: new Vector2(-6f, -6f), skin: "gauge_fill",
                fallbackColor: new Color(0.85f, 0.35f, 0.28f));
            Image fill = parts.BarFill.GetComponent<Image>();
            fill.raycastTarget = false;
            if (Skin("gauge_fill") != null)
                fill.color = new Color(1f, 0.50f, 0.42f);

            parts.Status = CreateText(strip, "Status", "", 20, FontStyles.Bold, CreamTextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(454f, 0f), sizeDelta: new Vector2(-470f, 0f));

            // 밀릴 때: 바와 남은 시간을 비키고 이 경고가 그 자리를 다 쓴다. 짙은 판 위라 경고 빨강이 읽힌다.
            parts.Warning = CreateText(strip, "Warning", "", 20, FontStyles.Bold, new Color(1f, 0.48f, 0.42f), TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(176f, 0f), sizeDelta: new Vector2(-192f, 0f));
            parts.Warning.gameObject.SetActive(false);

            parts.Root.SetActive(false);
            return parts;
        }

        private sealed class ChapterHudParts
        {
            public RectTransform Banner;
            public Button BannerButton;
            public TMP_Text BannerCaption;
            public TMP_Text BannerClock;
            public GameObject TroopRow;
            public TMP_Text TroopText;
            public RectTransform TroopGaugeFill;
            public GameObject TrainTile;
            public Image TrainTileRing;
            public TMP_Text TrainTileTime;
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
            var blue = new Color(0.30f, 0.42f, 0.62f);

            // 위협 띠 — 시안 v9: 띠 x 78~640, y 182~282(원형 액자가 왼쪽에 반쯤 걸친다), 액자 지름 128.
            // 띠 자체는 액자 중심(x=78)에서 시작하고, 액자는 띠의 자식으로 왼쪽 바깥에 걸어 둔다 —
            // 자식이 나중에 그려지므로 액자가 띠 테두리를 덮는다.
            parts.Banner = CreatePanel(
                canvas, "ChapterBanner",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(78f, -182f), sizeDelta: new Vector2(562f, 100f), skin: "band_crimson");
            parts.BannerButton = parts.Banner.gameObject.AddComponent<Button>();
            parts.BannerButton.targetGraphic = parts.Banner.GetComponent<Image>();

            bool portrait = AddIcon(parts.Banner, "portrait_giant_brute_framed_256", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, 128f) != null;
            float textLeft = portrait ? 76f : 24f;

            parts.BannerCaption = CreateText(parts.Banner, "Caption", "", 20, FontStyles.Bold, CreamTextColor,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(textLeft, -8f), new Vector2(300f, 28f));
            parts.BannerCaption.richText = true;

            parts.BannerClock = CreateText(parts.Banner, "Clock", "", 44, FontStyles.Bold, CreamTextColor,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(textLeft - 2f, -36f), new Vector2(170f, 54f));

            // 오른쪽 묶음: 투구 + "용사 n/300" + 금색 게이지. 출정 버튼이 뜨면 통째로 숨는다.
            RectTransform row = CreateRect(parts.Banner, "TroopRow",
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(textLeft + 160f, 0f), sizeDelta: new Vector2(-(textLeft + 160f) - 18f, 0f));
            parts.TroopRow = row.gameObject;

            bool helmet = AddIcon(row, "icon_soldier", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-4f, -8f), 44f) != null;
            parts.TroopText = CreateText(row, "TroopText", "", 22, FontStyles.Bold, CreamTextColor,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(helmet ? 44f : 0f, -12f), new Vector2(helmet ? -44f : 0f, 34f));
            parts.TroopText.richText = true;

            RectTransform track = CreatePanel(row, "Gauge",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -60f), sizeDelta: new Vector2(0f, 18f), skin: "gauge_track",
                fallbackColor: new Color(0.27f, 0.09f, 0.08f));
            track.GetComponent<Image>().raycastTarget = false;

            // 채움은 9-slice라 Filled를 못 쓴다 — 오른쪽 앵커를 비율로 옮긴다(ChapterHudView.SetGauge).
            parts.TroopGaugeFill = CreatePanel(track, "Fill",
                anchorMin: Vector2.zero, anchorMax: new Vector2(0.4f, 1f), pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(3f, 0f), sizeDelta: new Vector2(-6f, -6f), skin: "gauge_fill",
                fallbackColor: new Color(0.84f, 0.67f, 0.41f));
            parts.TroopGaugeFill.GetComponent<Image>().raycastTarget = false;

            parts.DispatchButton = CreateButton(parts.Banner, "DispatchButton", "협곡으로 출정!", out parts.DispatchLabel,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-12f, 0f), sizeDelta: new Vector2(280f, 76f), fillColor: DispatchCream, labelFontSize: 26);
            if (AddIcon(parts.DispatchButton.transform, "icon_march", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), 64f) != null)
                parts.DispatchLabel.margin = new Vector4(60f, 0f, 0f, 0f);
            parts.DispatchButton.gameObject.SetActive(false);
            parts.Banner.gameObject.SetActive(false);

            CreateTrainTile(canvas, parts);

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

        /// <summary>처음 짓는 HUD의 크기 배율 — 형님 판단 "살짝 너무 크다"(2026-10-23)로 시안보다 12% 작게 시작한다.</summary>
        private const float DefaultHudScale = 0.88f;

        /// <summary>
        /// 지금 씬 HUD에 형님이 슬라이더로 맞춰 둔 크기. HUD를 다시 지을 때 이 값을 이어 쓴다 —
        /// 안 그러면 "HUD 다시 만들기"를 누를 때마다 눈으로 맞춘 크기가 기본값으로 돌아간다.
        /// </summary>
        internal static float ExistingHudScale()
        {
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != "HUD")
                    continue;

                HudScale existing = roots[i].GetComponent<HudScale>();
                if (existing != null)
                    return existing.Scale;
            }

            return DefaultHudScale;
        }

        internal static RectTransform CreateCanvas(float hudScale = DefaultHudScale)
        {
            var go = new GameObject("HUD");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // 세로(portrait) 고정 — 기기마다 세로 길이가 더 크게 갈리므로(노치·비율 차이)
            // 너비를 기준으로 맞춘다. 높이를 기준으로 맞추면 좁은 화면에서 HUD가 옆으로 밀려난다.
            scaler.referenceResolution = new Vector2(HudScale.DesignWidth, HudScale.DesignHeight);
            scaler.matchWidthOrHeight = 0f;

            // 크기 슬라이더(인스펙터: HUD → HudScale). 기준 해상도를 배율만큼 넓혀 전체를 같은 비율로 줄인다.
            go.AddComponent<HudScale>().Scale = hudScale;

            go.AddComponent<GraphicRaycaster>();

            return go.GetComponent<RectTransform>();
        }

        private sealed class ResourceBarParts
        {
            public TMP_Text LevelText;
            public TMP_Text ProsperityText;
            public TMP_Text Wood;
            public TMP_Text Stone;
            public TMP_Text Food;
            public TMP_Text Gold;
            public GameObject FoodCell;
            public GameObject GoldCell;
        }

        /// <summary>
        /// 왼쪽 위 상태창 — 시안 v9: 월계관 Lv 배지(136) 오른쪽에 대리석 칩 3행 2열
        /// (번영도|전투력, 목재|식량, 석재|금). 게임에 전투력 값이 없어 그 칸은 비운다.
        /// 식량·금은 본부 Lv.2·5에 생기므로(ResourceBarView) 그 전에는 오른쪽 열이 비어 있다.
        /// </summary>
        private static ResourceBarParts CreateResourceBar(RectTransform canvas)
        {
            var parts = new ResourceBarParts();
            RectTransform root = CreateRect(
                canvas, "ResourceBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(540f, 170f));

            parts.ProsperityText = CreateResourceChip(root, "Prosperity", 0, 0, "icon_prosperity", out _);
            parts.Wood = CreateResourceChip(root, "Wood", 0, 1, "icon_res_wood", out _);
            parts.Food = CreateResourceChip(root, "Food", 1, 1, "icon_res_food", out parts.FoodCell);
            parts.Stone = CreateResourceChip(root, "Stone", 0, 2, "icon_res_stone", out _);
            parts.Gold = CreateResourceChip(root, "Gold", 1, 2, "icon_res_gold", out parts.GoldCell);

            // 월계관 배지 — 원판 중심은 그림의 (134, 129)/272 자리(tools/hud-art/import_hud_v9.py가 찍어 준다).
            const float badgeSize = 136f;
            Vector2 badgePos = new Vector2(10f, -12f);
            Sprite badgeSprite = Skin("frame_badge_lv");
            if (badgeSprite != null)
                AddImage(root, "LevelBadge", badgeSprite, new Vector2(0f, 1f), new Vector2(0f, 1f), badgePos, new Vector2(badgeSize, badgeSize));

            parts.LevelText = CreateText(
                root, "LevelText", "Lv.0", 32, FontStyles.Bold, InkColor, TextAlignmentOptions.Midline,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: badgePos + new Vector2(134f / 272f * badgeSize, -129f / 272f * badgeSize), sizeDelta: new Vector2(110f, 44f));
            Outline(parts.LevelText, Color.white);

            return parts;
        }

        /// <summary>
        /// 자원 칩 하나(158×40) — 양 끝이 반원인 대리석 알약, 아이콘은 왼쪽 원 안에 꽉 차게(밖으로 안 나감), 숫자는 나머지 가운데.
        /// 아이콘과 글자가 칩의 자식이라 ResourceBarView가 칩을 숨기면(식량·금 잠김) 통째로 숨는다.
        /// </summary>
        private static TMP_Text CreateResourceChip(RectTransform root, string name, int column, int row, string icon, out GameObject cell)
        {
            const float w = 158f, h = 40f;
            RectTransform chip = CreatePanel(
                root, name + "Cell",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(170f + column * (w + 30f), -20f - row * 48f), sizeDelta: new Vector2(w, h),
                skin: "frame_chip", fallbackColor: PanelBackground);
            Image bg = chip.GetComponent<Image>();
            bg.raycastTarget = false;
            FitCapsToHeight(bg, h);
            cell = chip.gameObject;

            bool hasIcon = AddIcon(chip, icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 0f), h - 2f) != null;

            TMP_Text t = CreateText(
                chip, name + "Text", "0", 24, FontStyles.Bold, hasIcon ? InkColor : TextColor, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: Vector2.zero);
            t.margin = new Vector4(hasIcon ? h : 8f, 0f, 6f, 0f);

            // 칩은 한 줄짜리라 숫자가 길어지면(10,000,000) 두 줄로 꺾이는 대신 글자를 줄인다.
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = true;
            t.fontSizeMin = 14f;
            t.fontSizeMax = 24f;
            return t;
        }

        /// <summary>왼쪽 열 1번 칸 — 공사 대기열(망치 a/b). 대기열 한도가 생기는 본부 Lv.4부터 ResourceBarView가 켠다.</summary>
        private static void CreateQueueTile(RectTransform canvas, out GameObject tile, out TMP_Text text)
        {
            RectTransform t = CreateLeftTile(canvas, "QueueTile", 0);
            AddTileIcon(t, "icon_build", 100f, 108f, LeftTileGold);
            text = CreateTileLabel(t, "0/0", 22, 18f);
            tile = t.gameObject;
            tile.SetActive(false);
        }

        /// <summary>
        /// 왼쪽 열 2번 칸 — 훈련 진행(투구 + 남은 시간, 칸 바로 밑에 진행 바). 훈련이 도는 동안 ChapterHudView가 켠다.
        /// 진행 바는 칸의 자식이라 칸과 같이 숨는다(칸 밖에 그려져도 자식이면 함께 꺼진다).
        /// </summary>
        private static void CreateTrainTile(RectTransform canvas, ChapterHudParts parts)
        {
            RectTransform t = CreateLeftTile(canvas, "TrainTile", 1);
            AddTileIcon(t, "icon_soldier", 100f, 108f, LeftTileGold);
            parts.TrainTileTime = CreateTileLabel(t, "0:00", 22, 18f);

            RectTransform track = CreatePanel(t, "Progress",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -4f), sizeDelta: new Vector2(80f, 12f), skin: "gauge_track",
                fallbackColor: new Color(0.16f, 0.10f, 0.07f));
            track.GetComponent<Image>().raycastTarget = false;

            // 채움은 Filled(가로)로 — ChapterHudView가 fillAmount만 바꾼다. 6px 높이라 9-slice 끝이 안 보여서 늘림 그림이어도 된다.
            Sprite fillSprite = Skin("gauge_fill") ?? RoundedPanelSprite;
            parts.TrainTileRing = AddImage(track, "Fill", fillSprite, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(74f, 6f));
            parts.TrainTileRing.type = Image.Type.Filled;
            parts.TrainTileRing.fillMethod = Image.FillMethod.Horizontal;
            parts.TrainTileRing.fillOrigin = (int)Image.OriginHorizontal.Left;
            parts.TrainTileRing.fillAmount = 0.3f;

            parts.TrainTile = t.gameObject;
            parts.TrainTile.SetActive(false);
        }

        /// <summary>왼쪽 열 칸의 금테 두께(화면 단위) — 시안: 높이 140 이하 칸은 15, 하단 탭(164)은 22.</summary>
        private const float LeftTileGold = 15f;
        private const float NavTileGold = 22f;

        /// <summary>왼쪽 열 칸(100×108) — 금테 대리석 타일(frame_tab), 전선 띠 아래부터 120씩.</summary>
        private static RectTransform CreateLeftTile(RectTransform canvas, string name, int index)
        {
            RectTransform t = CreatePanel(
                canvas, name,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(20f, -LeftTileTop - index * 120f), sizeDelta: new Vector2(100f, 108f),
                skin: "frame_tab", fallbackColor: new Color(0.24f, 0.22f, 0.20f));
            Image bg = t.GetComponent<Image>();
            bg.raycastTarget = false;
            FitTabFrame(bg, LeftTileGold);
            return t;
        }

        /// <summary>
        /// 탭 타일 아이콘 — 금테 안쪽 면을 꽉 채운다(시안 v9 규칙: 프레임 밖으로 안 나감). 아이콘 PNG 여백만큼
        /// 키우고 위로 당겨서 그림 자체가 안쪽 면 위 끝에 닿게 한다. 라벨은 아이콘 아래쪽에 겹쳐 쓴다.
        /// </summary>
        private static Image AddTileIcon(RectTransform tile, string icon, float tileW, float tileH, float gold)
        {
            float s = (Mathf.Min(tileW, tileH) - 2f * gold) / IconInkFraction;
            return AddIcon(tile, icon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(gold - s * IconMarginFraction)), s);
        }

        /// <summary>타일 라벨 — 짙은 갈색 글자에 흰 테두리. 아이콘 위에 겹치므로 테두리가 있어야 읽힌다.</summary>
        private static TMP_Text CreateTileLabel(RectTransform tile, string text, int fontSize, float centerFromBottom)
        {
            TMP_Text label = CreateText(
                tile, "Label", text, fontSize, FontStyles.Bold, InkColor, TextAlignmentOptions.Midline,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, centerFromBottom), sizeDelta: new Vector2(0f, fontSize + 12f));
            Outline(label, Color.white);
            return label;
        }

        /// <summary>
        /// 하단 임무 바 — 내비바 바로 위에 한 줄. 바 전체가 버튼이고(누르면 그 건물로 카메라가 간다),
        /// 오른쪽 "받기"는 그 안의 별도 버튼이다. UGUI는 겹친 버튼 중 맨 위 것만 클릭을 받으므로
        /// "받기"를 눌러도 바의 이동이 같이 일어나지 않는다.
        /// </summary>
        private static RectTransform CreateQuestBar(
            RectTransform canvas, out Button barButton, out TMP_Text title, out TMP_Text progress, out Button claimButton)
        {
            // 양피지 띠(좌우 말림만 고정하는 3-slice) — 시안 v5: 미션 바는 양피지 재질.
            RectTransform bar = CreatePanel(
                canvas, "QuestBar",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, QuestBarBottom), sizeDelta: new Vector2(-48f, QuestBarHeight),
                skin: "parchment");
            bool parchment = Skin("parchment") != null;

            barButton = bar.gameObject.AddComponent<Button>();
            barButton.targetGraphic = bar.GetComponent<Image>();

            // 두루마리 아이콘은 양피지 높이 안에 꽉 차게(시안 v9: 100 정사각).
            bool scroll = AddIcon(bar, "icon_quest", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f),
                QuestBarHeight) != null;
            float textLeft = scroll ? 136f : 28f;
            const float claimRight = 44f, claimW = 176f;
            float textRight = claimRight + claimW + 16f;

            title = CreateText(
                bar, "TitleText", "", 28, FontStyles.Bold, parchment ? InkColor : TextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(textLeft, -12f), sizeDelta: new Vector2(-textLeft - textRight, 42f));

            progress = CreateText(
                bar, "ProgressText", "", 22, FontStyles.Normal, parchment ? InkDimColor : DimTextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0f, 0f),
                anchoredPos: new Vector2(textLeft, 12f), sizeDelta: new Vector2(-textLeft - textRight, 34f));

            claimButton = CreateButton(
                bar, "ClaimButton", "받기", out _,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f), pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-claimRight, 0f), sizeDelta: new Vector2(claimW, 72f), labelFontSize: 30);

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
            // 서로 탭을 가로챈다. 높이는 BuildingInfoPanelView가 내용에 맞춰 줄인다 — 여기 값은 처음 크기일 뿐.
            // 아래쪽 피벗이라 줄어들 때 위쪽이 내려온다(임무 바와의 간격은 그대로).
            RectTransform panel = CreatePanel(
                canvas, "BuildingInfoPanel",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, QuestBarBottom + QuestBarHeight + 16f), sizeDelta: new Vector2(680f, 300f));

            // 안쪽 여백 28 — 스킨 테두리가 화면에서 14px(28 / SkinScale)이라 글자가 금테에 붙지 않게.
            const float pad = 28f;

            // 34라서 제목 글꼴(명조)로 나온다 — CreateText의 TitleFontMinSize.
            nameText = CreateText(
                panel, "NameText", "", 34, FontStyles.Normal, TextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -22f), sizeDelta: new Vector2(-pad * 2f, 44f));

            statusText = CreateText(
                panel, "StatusText", "", 24, FontStyles.Normal, DimTextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -68f), sizeDelta: new Vector2(-pad * 2f, 34f));

            // 여러 줄 — 본부는 조건 줄 두세 개 + 비용 한 줄이 들어간다. 색 표시에 TMP 리치 텍스트를 쓴다.
            costText = CreateText(
                panel, "CostText", "", 22, FontStyles.Normal, TextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, BuildingInfoPanelView.CostTop), sizeDelta: new Vector2(-pad * 2f, 140f));
            costText.lineSpacing = 12f;

            messageText = CreateText(
                panel, "MessageText", "", 22, FontStyles.Normal, WarnColor, TextAlignmentOptions.BottomLeft,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, BuildingInfoPanelView.ButtonRowHeight), sizeDelta: new Vector2(-pad * 2f, 30f));

            repairButton = CreateButton(
                panel, "RepairButton", "복구", out repairLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-24f, 24f), sizeDelta: new Vector2(180f, 72f));

            collectButton = CreateButton(
                panel, "CollectButton", "수확", out collectLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-216f, 24f), sizeDelta: new Vector2(180f, 72f),
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
                    anchoredPos: new Vector2(24f + i * 160f, 24f), sizeDelta: new Vector2(148f, 72f),
                    fillColor: new Color(0.55f, 0.42f, 0.70f), labelFontSize: 22);

                string[] wings = { "icon_speedup_1m", "icon_speedup_5m", "icon_speedup_15m" };
                if (AddIcon(speedUpButtons[i].transform, wings[i], new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), 60f) != null)
                    speedUpLabels[i].margin = new Vector4(62f, 0f, 8f, 0f);

                speedUpLabels[i].fontStyle = FontStyles.Normal;
                speedUpLabels[i].lineSpacing = -8f;
            }

            return panel;
        }

        /// <summary>
        /// 하단 탭 줄. SLG 화면 하면 떠오르는 그 줄이다 — 탐색·영웅·동맹·상점은 아직 그 기능이 없어서
        /// 눌러도 토스트만 뜬다(<see cref="HudChromeView"/>). 시안 v6: 갈색 바 위에 크림 대리석 칸,
        /// 칸마다 3D 아이콘 + 아래 이름. 강조 탭은 없다(시안 확정 사항 — 외출도 다른 탭과 같은 모양).
        /// </summary>
        private static void CreateNavBar(
            RectTransform canvas,
            out Button battleButton, out Button exploreButton, out Button heroesButton, out Button allianceButton,
            out Button shopButton, out Button bagButton, out Button worldMapButton)
        {
            RectTransform bar = CreatePanel(
                canvas, "NavBar",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(0f, NavBarHeight), skin: "navbar");

            // 전선은 본부 Lv.99로 잠겨 있어 사실상 안 보인다(function-unlock.json) — 아이콘도 없다.
            battleButton = CreateNavButton(bar, "BattleButton", "전선", 0, null);
            exploreButton = CreateNavButton(bar, "ExploreButton", "탐색", 1, "icon_tab_explore");
            heroesButton = CreateNavButton(bar, "HeroesButton", "영웅", 2, "icon_tab_heroes");
            allianceButton = CreateNavButton(bar, "AllianceButton", "동맹", 3, "icon_tab_alliance");
            shopButton = CreateNavButton(bar, "ShopButton", "상점", 4, "icon_tab_shop");
            bagButton = CreateNavButton(bar, "BagButton", "가방", 5, "icon_tab_bag");
            worldMapButton = CreateNavButton(bar, "WorldMapButton", "외출", 6, "icon_tab_world");
        }

        /// <summary>탭 줄을 7칸으로 나눈 <paramref name="slot"/>번째 자리에 탭을 놓는다.</summary>
        private static Button CreateNavButton(RectTransform bar, string name, string label, int slot, string icon)
        {
            // 처음 배치일 뿐이다 — 열린 버튼만 남겨 고르게 다시 나누는 것은 HudChromeView가 한다.
            const int slotCount = 7;
            float min = (float)slot / slotCount;
            float max = (float)(slot + 1) / slotCount;

            // 시안 v9: 금테 대리석 타일(높이 164), 아이콘은 금테 안쪽 면을 꽉 채우고 이름은 그 아래쪽에 흰 테두리 글자로 겹친다.
            const float tileH = NavBarHeight - 12f;
            RectTransform tile = CreatePanel(
                bar, name,
                anchorMin: new Vector2(min, 0f), anchorMax: new Vector2(max, 1f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(-10f, -12f),
                skin: "frame_tab", fallbackColor: new Color(0.24f, 0.22f, 0.20f));
            Image bg = tile.GetComponent<Image>();
            FitTabFrame(bg, NavTileGold);
            Button button = MakeButton(bg);
            bool marble = Skin("frame_tab") != null;

            // 아이콘 크기는 칸 높이로 정한다 — 칸 폭은 열린 탭 수에 따라 바뀌지만(HudChromeView) 높이는 그대로다.
            bool hasIcon = icon != null && AddTileIcon(tile, icon, tileH, tileH, NavTileGold) != null;

            if (hasIcon)
            {
                CreateTileLabel(tile, label, 30, 24f);
            }
            else
            {
                CreateText(
                    tile, "Label", label, 30, FontStyles.Bold, marble ? InkColor : TextColor, TextAlignmentOptions.Midline,
                    anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                    anchoredPos: Vector2.zero, sizeDelta: Vector2.zero);
            }

            return button;
        }

        /// <summary>
        /// 오른쪽 위 원형 버튼(설정·우편·특가) — 금 링 대리석 판(frame_round_btn) 위에 그림. 그림은 링 안쪽을 꽉 채운다
        /// (지름의 0.84 — compose_v7.py). 그림이 아직 없으면 이름 글자를 쓴다.
        /// 레드닷은 넣지 않았다 — 알릴 것이 생기는 기능이 붙을 때 그 기능이 띄운다.
        /// </summary>
        private static Button CreateRoundButton(RectTransform canvas, string name, string icon, string label, Vector2 pos, float size)
        {
            Sprite ring = Skin("frame_round_btn") ?? Skin("round_button");
            RectTransform rect = CreateRect(
                canvas, name,
                anchorMin: new Vector2(1f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                anchoredPos: pos, sizeDelta: new Vector2(size, size));

            Image bg = rect.gameObject.AddComponent<Image>();
            if (ring != null)
            {
                bg.sprite = ring;
                bg.preserveAspect = true;
            }
            else
            {
                bg.sprite = RoundedPanelSprite;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0.24f, 0.22f, 0.20f);
            }

            Button button = MakeButton(bg);

            if (ring == null || AddIcon(rect, icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size * 0.84f) == null)
            {
                CreateText(rect, "Label", label, 18, FontStyles.Bold, ring != null ? InkColor : Color.white, TextAlignmentOptions.Midline,
                    anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                    anchoredPos: Vector2.zero, sizeDelta: Vector2.zero);
            }

            return button;
        }

        private static RectTransform CreateToast(RectTransform canvas, out TMP_Text toastText)
        {
            // 시안 v9 토스트: 금테 대리석 패널(frame_panel, 금테 22) + 짙은 갈색 글자.
            RectTransform panel = CreatePanel(
                canvas, "Toast",
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f), pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 300f), sizeDelta: new Vector2(900f, 220f), skin: "frame_panel");
            bool marble = Skin("frame_panel") != null;
            if (marble)
                panel.GetComponent<Image>().pixelsPerUnitMultiplier = 44f / 22f;

            // 금테(22) 안쪽에 가는 금선이 한 줄 더 있다 — 글자가 그 선에 닿지 않게 좌우 48씩 비운다.
            toastText = CreateText(
                panel, "Text", "", 28, FontStyles.Normal, marble ? InkColor : TextColor, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(marble ? -96f : -40f, marble ? -48f : 0f));

            return panel;
        }

        private const string IconFolder = "Assets/Art/UI/Icons/";
        private const string SkinFolder = "Assets/Art/UI/Skin/";

        /// <summary>아트 납품 아이콘(투명 PNG → 스프라이트). 아직 없으면 null — 그 자리는 글자만 남는다.</summary>
        internal static Sprite Icon(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(IconFolder + name + ".png");
        }

        /// <summary>
        /// 늘려 쓰는 바탕 조각(OlympusArt/ui/make_hud_skin.py). 9-slice 테두리는 UiIconPostprocessor가 박는다.
        /// 없으면 null — 부르는 쪽이 예전 회색 둥근 사각형으로 물러난다(아트 없이도 씬은 지어진다).
        /// </summary>
        internal static Sprite Skin(string name)
        {
            return name == null ? null : AssetDatabase.LoadAssetAtPath<Sprite>(SkinFolder + name + ".png");
        }

        /// <summary>부모 안 지정 위치에 아이콘을 하나 놓는다. 스프라이트가 없으면 아무것도 안 만든다.</summary>
        internal static Image AddIcon(Transform parent, string spriteName, Vector2 anchor, Vector2 pivot, Vector2 pos, float size)
        {
            return AddIcon(parent, spriteName, anchor, pivot, pos, new Vector2(size, size));
        }

        internal static Image AddIcon(Transform parent, string spriteName, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            Sprite sprite = Icon(spriteName);
            if (sprite == null)
                return null;

            Image image = AddImage(parent, "Icon_" + spriteName, sprite, anchor, pivot, pos, size);
            image.preserveAspect = true;
            return image;
        }

        internal static Image AddImage(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rect = CreateRect(parent, name, anchor, anchor, pivot, pos, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;   // 그림이 버튼 클릭을 가로채지 않게.
            return image;
        }

        /// <summary>그림 없는 빈 RectTransform — 묶음(자원 바·용사 줄)의 뿌리로 쓴다.</summary>
        internal static RectTransform CreateRect(
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
            return rect;
        }

        /// <summary>
        /// 바탕이 있는 패널. 기본 스킨은 짙은 반투명 판 + 얇은 금 테두리(panel_dark) — 밝은 글자를 그대로 쓰는
        /// 정보 패널·토스트·월드맵·전투 화면이 같이 쓰므로, 밝은 바탕(panel_cream 등)은 부르는 쪽이 골라야 한다.
        /// </summary>
        internal static RectTransform CreatePanel(
            Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta,
            string skin = "panel_dark", Color? fallbackColor = null)
        {
            RectTransform rect = CreateRect(parent, name, anchorMin, anchorMax, pivot, anchoredPos, sizeDelta);
            Image bg = rect.gameObject.AddComponent<Image>();
            ApplySkin(bg, Skin(skin), fallbackColor ?? PanelBackground);
            return rect;
        }

        private static void ApplySkin(Image image, Sprite skin, Color fallbackColor)
        {
            if (skin == null)
            {
                image.sprite = RoundedPanelSprite;
                image.type = Image.Type.Sliced;
                image.color = fallbackColor;
                return;
            }

            image.sprite = skin;
            image.type = skin.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = SkinScale;
            image.color = Color.white;
        }

        /// <summary>
        /// 양 끝이 반원인 알약 그림(칩·초록 버튼)은 끝 반원이 칸 높이에 맞아야 둥글게 보인다 — 그림 높이를 칸 높이로
        /// 나눈 배수를 준다. 고정 배수(2)로 두면 높이가 72가 아닌 버튼에서 끝이 납작하거나 뾰족해진다.
        /// </summary>
        private static void FitCapsToHeight(Image image, float height)
        {
            if (image.sprite != null && image.type == Image.Type.Sliced && height > 0f)
                image.pixelsPerUnitMultiplier = image.sprite.rect.height / height;
        }

        /// <summary>탭 프레임(frame_tab)의 금테가 화면에서 <paramref name="gold"/> 두께가 되게 한다(시안: 작은 칸 15, 하단 탭 22).</summary>
        private static void FitTabFrame(Image image, float gold)
        {
            if (image.sprite != null && image.type == Image.Type.Sliced)
                image.pixelsPerUnitMultiplier = TabFrameBorderPx / gold;
        }

        /// <summary>짙은 초록 — 3D 초록 버튼 위 흰 글자의 테두리(시안 v9 "받기").</summary>
        private static readonly Color GreenButtonEdge = new Color(0.12f, 0.35f, 0.12f);

        /// <summary>
        /// 글자에 테두리를 두른다. 테두리는 글꼴 재질의 값이라 글자마다 재질을 새로 만들면(씬에 묻힌 재질)
        /// 다시 지을 때마다 쌓이고 diff도 못 읽는다 — 글꼴·색마다 재질 자산 하나를 만들어 같이 쓴다.
        /// 재질은 Assets/Art/Fonts/에 둔다(.mat은 SceneAutoRebuild 지문에 안 들어가서, 만들어도 재조립이 또 돌지 않는다).
        /// </summary>
        private static void Outline(TMP_Text text, Color color)
        {
            var font = text.font;
            if (font == null || font.material == null)
                return;

            string path = FontFolder + font.name + " Outline " + ColorUtility.ToHtmlStringRGB(color) + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(font.material) { name = font.name + " Outline " + ColorUtility.ToHtmlStringRGB(color) };
                AssetDatabase.CreateAsset(mat, path);
            }

            // 값은 매번 다시 넣는다 — 두께를 고치면 이미 있는 재질에도 반영되게.
            mat.shader = font.material.shader;
            mat.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, color);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, TextOutlineWidth);
            // 테두리는 글자 가장자리를 중심으로 안팎 반반 그려진다 — 같은 만큼 글자를 불려야 글자 속이 안 먹힌다
            // (0.4/0.2로 두었더니 명조 Lv 글자가 머리카락 굵기만 남았다).
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, TextOutlineWidth);
            ShaderUtilities.UpdateShaderRatios(mat);
            EditorUtility.SetDirty(mat);

            text.fontSharedMaterial = mat;
        }

        /// <summary>글자 테두리 두께(글꼴 재질 단위, 0~1). 시안은 22~30pt 글자에 3px 테두리다.</summary>
        private const float TextOutlineWidth = 0.25f;

        /// <summary>그림 하나를 버튼으로 — 누르면 살짝 어두워지고, 못 누를 때는 확실히 회색.</summary>
        private static Button MakeButton(Image target)
        {
            Button button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f);
            button.colors = colors;
            return button;
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
            bool title = fontSize >= TitleFontMinSize && _titleFont != null;
            TMP_FontAsset font = title ? _titleFont : _koreanFont;
            if (font != null)
                text.font = font;
            text.text = initialText;
            text.fontSize = fontSize;

            // 제목 글꼴은 이미 Bold 굵기다 — Bold 효과를 또 얹으면 획이 뭉개진다.
            text.fontStyle = title ? style & ~FontStyles.Bold : style;
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

            Color fill = fillColor ?? ClaimGreen;
            Image image = go.AddComponent<Image>();

            // 기본(초록) 버튼은 시안 v9의 3D 버튼 한 장(btn_green) — 이미 초록으로 칠해진 그림이라 색을 곱하지 않는다.
            // 다른 색 버튼은 그 색의 3D 그림이 아직 없어서 예전 방식(흰 바탕에 색 곱하기 + 금 테두리)을 쓴다.
            Sprite green = fill == ClaimGreen ? Skin("btn_green") : null;
            if (green != null)
            {
                ApplySkin(image, green, fill);
                FitCapsToHeight(image, sizeDelta.y);
            }
            else
            {
                // 바탕(btn_fill)은 흰 그림에 색을 곱하고, 금 테두리(btn_frame)는 색을 안 곱한 채 위에 얹는다 —
                // 한 장에 테두리까지 그려 두면 버튼 색을 바꿀 때 금색도 같이 물든다.
                Sprite fillSprite = Skin("btn_fill");
                ApplySkin(image, fillSprite, fill);
                image.color = fill;

                Sprite frame = Skin("btn_frame");
                if (fillSprite != null && frame != null)
                {
                    RectTransform frameRect = CreateRect(go.transform, "Frame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    Image frameImage = frameRect.gameObject.AddComponent<Image>();
                    ApplySkin(frameImage, frame, Color.white);
                    frameImage.raycastTarget = false;
                }
            }

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // 못 눌렀을 때(재료 부족)도 눈으로 구분되도록 색을 확실히 다르게 둔다.
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f);
            button.colors = colors;

            // 밝은 바탕(출정 버튼의 크림 등)에는 흰 글자가 안 읽힌다 — 짙은 잉크색으로 바꾼다.
            bool lightFill = fill.r * 0.30f + fill.g * 0.59f + fill.b * 0.11f > 0.65f;
            labelText = CreateText(
                go.transform, "Label", label, labelFontSize, FontStyles.Bold, lightFill ? InkColor : Color.white, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: Vector2.zero);

            // 3D 초록 버튼은 밝은 부분이 있어 흰 글자만으로는 묻힌다 — 시안처럼 짙은 초록 테두리를 두른다.
            if (green != null)
                Outline(labelText, GreenButtonEdge);

            return button;
        }
    }
}

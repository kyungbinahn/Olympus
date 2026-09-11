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

        /// <summary>
        /// 유니티가 기본 제공하는 둥근 모서리 9-slice 스프라이트. 실제 아트가 들어오기 전까지
        /// 패널·버튼이 각진 사각형보다는 나아 보이게 하는 임시 그레이박스 형태다 —
        /// 건물이 원시 큐브인 것과 같은 이유(<see cref="TerritoryPresenter"/> 참고).
        /// </summary>
        private static Sprite RoundedPanelSprite =>
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static TMP_FontAsset _koreanFont;

        public static void Build(TerritoryPresenter presenter)
        {
            if (presenter == null)
            {
                Debug.LogError("HUD를 배선할 TerritoryPresenter가 없습니다.");
                return;
            }

            _koreanFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            if (_koreanFont == null)
            {
                Debug.LogWarning(
                    "한글 폰트를 못 찾았습니다: " + KoreanFontPath +
                    " — TMP 기본 폰트(라틴 전용)로 대체되어 한글이 네모로 보일 수 있습니다.");
            }

            // 이전에 지었던 HUD가 있으면 통째로 밀고 새로 짓는다 — HUD 안에는 손으로
            // 맞출 값이 없으므로(전부 코드가 정하는 색·배치) 부분 갱신을 신경 쓸 이유가 없다.
            // 이렇게 해야 이 메서드를 몇 번을 다시 불러도 Canvas가 중복 생기지 않는다.
            DestroyIfExists("HUD");
            DestroyIfExists("EventSystem");

            CreateEventSystem();
            RectTransform canvas = CreateCanvas();

            CreateResourceBar(canvas, out TMP_Text woodText, out TMP_Text stoneText);
            RectTransform infoPanel = CreateBuildingInfoPanel(
                canvas,
                out TMP_Text nameText,
                out TMP_Text statusText,
                out TMP_Text costText,
                out TMP_Text messageText,
                out Button repairButton,
                out TMP_Text repairLabel);

            infoPanel.gameObject.SetActive(false);

            CreateNavBar(
                canvas,
                out Button exploreButton, out Button heroesButton, out Button allianceButton,
                out Button shopButton, out Button bagButton, out Button worldMapButton);

            Button settingsButton = CreateSettingsButton(canvas, out _);
            RectTransform toastPanel = CreateToast(canvas, out TMP_Text toastText);

            var controller = new GameObject("HudController");
            controller.transform.SetParent(canvas, false);

            var resourceView = controller.AddComponent<ResourceBarView>();
            var so1 = new SerializedObject(resourceView);
            TerritorySceneBuilder.Wire(so1, "_presenter", presenter);
            TerritorySceneBuilder.Wire(so1, "_woodText", woodText);
            TerritorySceneBuilder.Wire(so1, "_stoneText", stoneText);
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
            so2.ApplyModifiedPropertiesWithoutUndo();

            var toastView = controller.AddComponent<ToastView>();
            var so3 = new SerializedObject(toastView);
            TerritorySceneBuilder.Wire(so3, "_root", toastPanel.gameObject);
            TerritorySceneBuilder.Wire(so3, "_text", toastText);
            so3.ApplyModifiedPropertiesWithoutUndo();

            var chromeView = controller.AddComponent<HudChromeView>();
            var so4 = new SerializedObject(chromeView);
            TerritorySceneBuilder.Wire(so4, "_toast", toastView);
            TerritorySceneBuilder.Wire(so4, "_exploreButton", exploreButton);
            TerritorySceneBuilder.Wire(so4, "_heroesButton", heroesButton);
            TerritorySceneBuilder.Wire(so4, "_allianceButton", allianceButton);
            TerritorySceneBuilder.Wire(so4, "_shopButton", shopButton);
            TerritorySceneBuilder.Wire(so4, "_bagButton", bagButton);
            TerritorySceneBuilder.Wire(so4, "_worldMapButton", worldMapButton);
            TerritorySceneBuilder.Wire(so4, "_settingsButton", settingsButton);
            so4.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DestroyIfExists(string rootName)
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

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static RectTransform CreateCanvas()
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

        private static RectTransform CreateResourceBar(RectTransform canvas, out TMP_Text woodText, out TMP_Text stoneText)
        {
            RectTransform panel = CreatePanel(
                canvas, "ResourceBar",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(0f, 1f), pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(24f, -24f), sizeDelta: new Vector2(260f, 100f));

            woodText = CreateText(
                panel, "WoodText", "목재 0", 32, FontStyles.Normal, TextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -6f), sizeDelta: new Vector2(-32f, 40f));

            stoneText = CreateText(
                panel, "StoneText", "석재 0", 32, FontStyles.Normal, TextColor, TextAlignmentOptions.MidlineLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -50f), sizeDelta: new Vector2(-32f, 40f));

            return panel;
        }

        private static RectTransform CreateBuildingInfoPanel(
            RectTransform canvas,
            out TMP_Text nameText,
            out TMP_Text statusText,
            out TMP_Text costText,
            out TMP_Text messageText,
            out Button repairButton,
            out TMP_Text repairLabel)
        {
            // y=176 — 하단 내비바(높이 160) 위에 여유 16을 두고 앉는다. 내비바와 겹치면
            // 복구 버튼과 내비바 버튼이 같은 자리에서 서로 탭을 가로챈다.
            RectTransform panel = CreatePanel(
                canvas, "BuildingInfoPanel",
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 176f), sizeDelta: new Vector2(600f, 220f));

            nameText = CreateText(
                panel, "NameText", "", 36, FontStyles.Bold, TextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -16f), sizeDelta: new Vector2(-40f, 48f));

            statusText = CreateText(
                panel, "StatusText", "", 26, FontStyles.Normal, DimTextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -68f), sizeDelta: new Vector2(-40f, 40f));

            costText = CreateText(
                panel, "CostText", "", 24, FontStyles.Normal, DimTextColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -108f), sizeDelta: new Vector2(-40f, 36f));

            messageText = CreateText(
                panel, "MessageText", "", 22, FontStyles.Normal, WarnColor, TextAlignmentOptions.TopLeft,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f), pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -144f), sizeDelta: new Vector2(-40f, 32f));

            repairButton = CreateButton(
                panel, "RepairButton", "복구", out repairLabel,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-20f, 16f), sizeDelta: new Vector2(160f, 56f));

            return panel;
        }

        /// <summary>
        /// 하단 내비바. SLG 화면 하면 떠오르는 그 줄이다 — 탐색·영웅·동맹·상점·가방은
        /// 아직 그 기능이 없어서 눌러도 토스트만 뜬다(<see cref="HudChromeView"/>).
        /// 외출(월드맵)만 색을 다르게 둔 이유는 마일스톤 ③에서 실제로 연결될 자리라서다.
        /// </summary>
        private static void CreateNavBar(
            RectTransform canvas,
            out Button exploreButton, out Button heroesButton, out Button allianceButton,
            out Button shopButton, out Button bagButton, out Button worldMapButton)
        {
            RectTransform bar = CreatePanel(
                canvas, "NavBar",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f), pivot: new Vector2(0.5f, 0f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(0f, 160f));

            var neutral = new Color(0.24f, 0.22f, 0.20f);
            var highlight = new Color(0.85f, 0.65f, 0.25f);

            exploreButton = CreateNavButton(bar, "ExploreButton", "탐색", 0, neutral, out _);
            heroesButton = CreateNavButton(bar, "HeroesButton", "영웅", 1, neutral, out _);
            allianceButton = CreateNavButton(bar, "AllianceButton", "동맹", 2, neutral, out _);
            shopButton = CreateNavButton(bar, "ShopButton", "상점", 3, neutral, out _);
            bagButton = CreateNavButton(bar, "BagButton", "가방", 4, neutral, out _);
            worldMapButton = CreateNavButton(bar, "WorldMapButton", "외출", 5, highlight, out _);
        }

        /// <summary>내비바를 6칸으로 나눈 <paramref name="slot"/>번째 자리에 버튼을 놓는다.</summary>
        private static Button CreateNavButton(
            RectTransform bar, string name, string label, int slot, Color color, out TMP_Text labelText)
        {
            const int slotCount = 6;
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
                anchoredPos: new Vector2(0f, 300f), sizeDelta: new Vector2(760f, 90f));

            toastText = CreateText(
                panel, "Text", "", 28, FontStyles.Normal, TextColor, TextAlignmentOptions.Midline,
                anchorMin: Vector2.zero, anchorMax: Vector2.one, pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero, sizeDelta: new Vector2(-40f, 0f));

            return panel;
        }

        private static RectTransform CreatePanel(
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

        private static TMP_Text CreateText(
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

        private static Button CreateButton(
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

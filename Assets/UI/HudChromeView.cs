using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Progression;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game;
using Olympus.Game.Territory;

namespace Olympus.UI
{
    /// <summary>
    /// 하단 내비바 + 설정 버튼 — SLG 화면의 "껍데기"다.
    ///
    /// 버튼은 <c>function-unlock.json</c>이 정한 본부 레벨이 되어야 생긴다. 아직 안 열린 버튼을
    /// "준비중"으로 보여 주는 것보다 숨겼다가 레벨업 순간에 생기게 하는 편이 낫다 — 새 버튼이
    /// 생기는 것 자체가 보상이다(탑포스 Lv.4에서 HUD가 바뀌는 순간과 같은 장치).
    ///
    /// 전선·외출(월드맵)·가방만 실제로 동작한다. 나머지는 기능이 생기면 onClick 배선만 바꾸면 된다.
    /// </summary>
    public sealed class HudChromeView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private ToastView _toast;

        [SerializeField] private Button _battleButton;
        [SerializeField] private Button _exploreButton;
        [SerializeField] private Button _heroesButton;
        [SerializeField] private Button _allianceButton;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _bagButton;
        [SerializeField] private Button _worldMapButton;
        [SerializeField] private Button _settingsButton;

        [Tooltip("오른쪽 열(설정 아래) — 우편·특가. 자리만 잡아 둔 것이라 지금은 알림만 띄운다.")]
        [SerializeField] private Button _mailButton;
        [SerializeField] private Button _offerButton;

        [Tooltip("레벨업 알림을 띄워 두는 시간(초). 이야기 한 줄 + 새 건물 + 새 기능을 읽을 시간이다.")]
        [SerializeField] private float _levelUpToastSeconds = 5f;

        /// <summary>내비바 왼쪽부터의 순서 — 숨긴 버튼을 빼고 이 순서로 다시 늘어놓는다.</summary>
        private readonly List<KeyValuePair<string, Button>> _nav = new List<KeyValuePair<string, Button>>();

        private int _lastHqLevel = -1;

        private void Start()
        {
            Wire(_exploreButton, "탐색 — 준비중입니다");
            Wire(_heroesButton, "영웅 — 준비중입니다");
            Wire(_allianceButton, "동맹 — 준비중입니다");
            Wire(_shopButton, "상점 — 준비중입니다");
            Wire(_settingsButton, "설정 — 준비중입니다");
            Wire(_mailButton, "우편 — 준비중입니다");
            Wire(_offerButton, "특가 — 준비중입니다");

            if (_bagButton != null)
                _bagButton.onClick.AddListener(ShowBag);

            if (_worldMapButton != null)
                _worldMapButton.onClick.AddListener(SceneRouter.GoToWorld);

            if (_battleButton != null)
                _battleButton.onClick.AddListener(SceneRouter.GoToBattle);

            AddNav("battle", _battleButton);
            AddNav("explore", _exploreButton);
            AddNav("heroes", _heroesButton);
            AddNav("alliance", _allianceButton);
            AddNav("shop", _shopButton);
            AddNav("bag", _bagButton);
            AddNav("world_map", _worldMapButton);

            if (_presenter == null || _presenter.Runtime == null)
            {
                Debug.LogError("HudChromeView가 TerritoryPresenter에 배선되지 않았다 — 버튼을 전부 보여 준다.");
                return;
            }

            _presenter.Runtime.Store.Changed += OnStateChanged;

            // 시작 레벨은 알리지 않는다 — 켤 때마다 "본부 Lv.3 달성!"이 뜨면 안 된다.
            _lastHqLevel = _presenter.Runtime.HqLevel;
            ApplyUnlocks();
        }

        private void OnDestroy()
        {
            if (_presenter != null && _presenter.Runtime != null)
                _presenter.Runtime.Store.Changed -= OnStateChanged;
        }

        private void OnStateChanged(StateDelta delta)
        {
            if (delta.Buildings == null)
                return;

            int level = _presenter.Runtime.HqLevel;
            if (level == _lastHqLevel)
                return;

            bool rose = level > _lastHqLevel;
            _lastHqLevel = level;
            ApplyUnlocks();

            if (rose)
                AnnounceLevel(level);
        }

        private void AddNav(string featureId, Button button)
        {
            if (button != null)
                _nav.Add(new KeyValuePair<string, Button>(featureId, button));
        }

        /// <summary>열린 버튼만 남기고, 남은 버튼을 내비바 폭에 고르게 다시 나눈다 — 빈 칸이 생기면 고장 난 것처럼 보인다.</summary>
        private void ApplyUnlocks()
        {
            TerritoryRuntime rt = _presenter.Runtime;
            var visible = new List<Button>(_nav.Count);

            for (int i = 0; i < _nav.Count; i++)
            {
                bool on = rt.IsFeatureUnlocked(_nav[i].Key);
                _nav[i].Value.gameObject.SetActive(on);
                if (on)
                    visible.Add(_nav[i].Value);
            }

            for (int i = 0; i < visible.Count; i++)
            {
                var rect = (RectTransform)visible[i].transform;
                rect.anchorMin = new Vector2((float)i / visible.Count, rect.anchorMin.y);
                rect.anchorMax = new Vector2((float)(i + 1) / visible.Count, rect.anchorMax.y);
            }
        }

        /// <summary>레벨업 알림 — 이야기 한 줄, 새로 열린 건물, 새로 생긴 버튼.</summary>
        private void AnnounceLevel(int level)
        {
            TerritoryRuntime rt = _presenter.Runtime;
            LocalizedStrings strings = _presenter.Strings;
            var sb = new StringBuilder();

            sb.Append("본부 Lv.").Append(level).Append(" 달성!");

            HqLevelDef def;
            if (rt.Progression.TryGetHqLevel(level, out def) && !string.IsNullOrEmpty(def.StoryKey))
                sb.Append('\n').Append(strings.Get(def.StoryKey));

            List<BuildingDef> buildings = FeatureUnlockTable.BuildingsUnlockedAt(rt.Catalog, level);
            if (buildings.Count > 0)
            {
                sb.Append("\n새 건물: ");
                for (int i = 0; i < buildings.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(strings.Get(buildings[i].DisplayNameKey));
                }
            }

            List<string> features = rt.Progression.Features.UnlockedExactlyAt(level);
            if (features.Count > 0)
            {
                sb.Append("\n새 기능: ");
                for (int i = 0; i < features.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(strings.Get("feature." + features[i]));
                }
            }

            _toast.Show(sb.ToString(), _levelUpToastSeconds);
        }

        /// <summary>가방 — 지금은 가속 아이템뿐이라 목록을 알림으로 보여 준다. 가방 화면이 생기면 갈아끼운다.</summary>
        private void ShowBag()
        {
            TerritoryRuntime rt = _presenter.Runtime;
            var sb = new StringBuilder("가방");
            bool any = false;

            foreach (SpeedUpItemDef item in rt.Progression.SpeedUps)
            {
                long count = rt.State.Items.Get(item.ItemId);
                if (count <= 0)
                    continue;

                sb.Append('\n').Append(_presenter.Strings.Get(item.DisplayNameKey)).Append("  ×").Append(count);
                any = true;
            }

            if (!any)
                sb.Append(" — 비어 있습니다");

            _toast.Show(sb.ToString(), 3f);
        }

        private void Wire(Button button, string message)
        {
            if (button == null)
                return;

            button.onClick.AddListener(() => _toast.Show(message));
        }
    }
}

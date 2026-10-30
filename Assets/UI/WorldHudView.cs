using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Olympus.Core.Grid;
using Olympus.Core.World;
using Olympus.Game;
using Olympus.Game.World;

namespace Olympus.UI
{
    /// <summary>
    /// 월드맵 HUD — 고른 칸의 좌표와 거기 있는 것을 보여주고, 기지로 돌아가는 길을 준다.
    ///
    /// 지형·거점 이름을 코드에 둔 이유는 <see cref="ResourceLabels"/>와 같다 —
    /// 데이터 파일에서 늘어나는 목록(건물 21종)이 아니라 코드가 분기하는 고정 enum이라,
    /// 로컬라이즈 테이블까지 갈 만큼 자주 바뀌지 않는다.
    /// </summary>
    public sealed class WorldHudView : MonoBehaviour
    {
        [SerializeField] private WorldPresenter _presenter;

        [Header("뷰")]
        [SerializeField] private GameObject _infoRoot;
        [SerializeField] private TMP_Text _coordText;
        [SerializeField] private TMP_Text _contentText;
        [SerializeField] private Button _enterBaseButton;
        [SerializeField] private TMP_Text _enterBaseLabel;
        [SerializeField] private Button _backButton;
        [SerializeField] private TMP_Text _backLabel;

        private void Start()
        {
            if (_presenter == null)
            {
                Debug.LogError("WorldHudView가 WorldPresenter에 배선되지 않았다.");
                enabled = false;
                return;
            }

            _presenter.SelectionChanged += OnSelectionChanged;

            _enterBaseButton.onClick.AddListener(SceneRouter.GoToTerritory);
            _backButton.onClick.AddListener(SceneRouter.GoToTerritory);

            _backLabel.text = "기지로";
            _enterBaseLabel.text = "들어가기";

            _infoRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_presenter != null)
                _presenter.SelectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged(GridPos cell, bool selected)
        {
            if (!selected)
            {
                _infoRoot.SetActive(false);
                return;
            }

            _infoRoot.SetActive(true);

            // SLG의 월드 좌표 표기다 — 이 문자열로 남들에게 위치를 불러준다.
            _coordText.text = "X:" + cell.X + "  Y:" + cell.Y;
            _contentText.text = DescribeCell(cell);

            _enterBaseButton.gameObject.SetActive(_presenter.SelectedIsPlayerCity());
        }

        private string DescribeCell(GridPos cell)
        {
            WorldMap map = _presenter.World != null ? _presenter.World.Map : null;
            if (map == null)
                return "";

            WorldSite site;
            if (map.TryGetSite(cell, out site))
                return DescribeSite(site);

            return TerrainLabel(map.TerrainAt(cell));
        }

        private static string DescribeSite(WorldSite site)
        {
            switch (site.Kind)
            {
                case WorldSiteKind.PlayerCity:
                    return "내 기지";

                case WorldSiteKind.RuinedCity:
                    return "폐허 도시  Lv" + site.Level;

                case WorldSiteKind.MonsterCamp:
                    return "몬스터 야영지  Lv" + site.Level;

                case WorldSiteKind.ResourceNode:
                    return ResourceLabels.Of(site.Resource) + " 채집지  Lv" + site.Level;

                default:
                    return "";
            }
        }

        private static string TerrainLabel(WorldTerrain terrain)
        {
            switch (terrain)
            {
                case WorldTerrain.Water: return "바다 — 지나갈 수 없음";
                case WorldTerrain.Forest: return "숲";
                case WorldTerrain.Mountain: return "산 — 지나갈 수 없음";
                default: return "평원";
            }
        }
    }
}

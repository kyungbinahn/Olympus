using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Olympus.UI
{
    /// <summary>
    /// 하단 탭 한 칸 — 선택된 탭만 대리석 금테(frame_tab)를 두르고 아이콘이 커지며 글자가 진갈색으로 바뀐다
    /// (시안 v10 B안, 형님 선택 2026-10-29). 나머지 탭은 청동 바(tabbar_bg) 위에 아이콘과 크림 글자만 얹힌다.
    /// 크기·색은 조립기(HudSceneBuilder.CreateNavButton)가 넣는다 — 시안이 바뀌면 거기만 고친다.
    /// </summary>
    public sealed class NavTabView : MonoBehaviour
    {
        [SerializeField] private GameObject _frame;
        [SerializeField] private RectTransform _icon;
        [SerializeField] private TMP_Text _label;

        [Header("선택")]
        [SerializeField] private float _selectedIconSize = 132f;
        [SerializeField] private float _selectedIconTop = 12f;
        [SerializeField] private int _selectedFontSize = 30;
        [SerializeField] private Color _selectedColor = new Color(0.36f, 0.20f, 0.13f);
        [SerializeField] private Material _selectedMaterial;

        [Header("보통")]
        [SerializeField] private float _iconSize = 116f;
        [SerializeField] private float _iconTop = 16f;
        [SerializeField] private int _fontSize = 26;
        [SerializeField] private Color _color = new Color(250f / 255f, 238f / 255f, 214f / 255f);
        [SerializeField] private Material _material;

        public void SetSelected(bool selected)
        {
            if (_frame != null)
                _frame.SetActive(selected);

            if (_icon != null)
            {
                float size = selected ? _selectedIconSize : _iconSize;
                _icon.sizeDelta = new Vector2(size, size);
                _icon.anchoredPosition = new Vector2(0f, -(selected ? _selectedIconTop : _iconTop));
            }

            if (_label != null)
            {
                _label.fontSize = selected ? _selectedFontSize : _fontSize;
                _label.color = selected ? _selectedColor : _color;
                Material m = selected ? _selectedMaterial : _material;
                if (m != null)
                    _label.fontSharedMaterial = m;
            }
        }
    }
}

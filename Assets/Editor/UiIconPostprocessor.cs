using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// <c>Assets/Art/UI/</c> 아래 PNG는 UI 스프라이트로 가져온다 — 기본(텍스처)으로 들어오면 Image에 못 꽂는다.
    /// 아트 납품 규칙: 투명 PNG 256×256, 아이콘 안 글자 없음(라벨은 TMP가 쓴다).
    ///
    /// <c>Assets/Art/UI/Skin/</c>은 늘려 쓰는 바탕 조각(패널·띠·버튼 테두리)이라 9-slice 테두리를 여기서 박는다.
    /// 원본은 <c>D:\OlympusArt\ui\make_hud_skin.py</c>이고 테두리 값은 그 파일의 BORDERS와 같아야 한다 —
    /// 인스펙터 Sprite Editor로 손으로 맞추면 아트를 다시 뽑을 때마다 사라진다.
    /// </summary>
    internal sealed class UiIconPostprocessor : AssetPostprocessor
    {
        private const string UiFolder = "Assets/Art/UI/";
        private const string SkinFolder = "Assets/Art/UI/Skin/";

        /// <summary>(왼쪽, 아래, 오른쪽, 위) px — 2배 해상도 그림 기준. HUD는 pixelsPerUnitMultiplier 2로 쓴다.</summary>
        private static readonly Dictionary<string, Vector4> SkinBorders = new Dictionary<string, Vector4>
        {
            { "panel_cream", new Vector4(32, 32, 32, 32) },
            { "panel_dark", new Vector4(28, 28, 28, 28) },
            { "panel_stone", new Vector4(26, 26, 26, 26) },
            { "band_crimson", new Vector4(36, 36, 36, 36) },
            { "gauge_track", new Vector4(18, 18, 18, 18) },
            { "gauge_fill", new Vector4(12, 12, 12, 12) },
            { "btn_fill", new Vector4(28, 28, 28, 28) },
            { "btn_frame", new Vector4(28, 28, 28, 28) },
            { "parchment", new Vector4(30, 0, 30, 0) },
            { "navbar", new Vector4(0, 0, 0, 14) },
        };

        // 표를 바꾼 뒤에는 Skin 폴더를 우클릭 → Reimport. (GetVersion을 올리는 방법도 있지만 그러면
        // 이 후처리기가 보는 모든 텍스처 — 캐릭터·건물까지 — 를 다시 가져와서 몇 분씩 걸린다.)

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;   // 256 원본을 40px 남짓으로 줄여 쓴다 — 밉맵이 없으면 테두리가 지글거린다.
            importer.wrapMode = TextureWrapMode.Clamp;

            if (!assetPath.StartsWith(SkinFolder))
                return;

            // 바탕 조각은 거의 원본 크기로 늘려 쓴다 — 밉맵을 켜 두면 테두리 금선이 뭉개진다.
            importer.mipmapEnabled = false;

            Vector4 border;
            if (SkinBorders.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out border))
                importer.spriteBorder = border;
        }
    }
}

using UnityEditor;

namespace Olympus.Editor
{
    /// <summary>
    /// <c>Assets/Art/UI/</c> 아래 PNG는 UI 스프라이트로 가져온다 — 기본(텍스처)으로 들어오면 Image에 못 꽂는다.
    /// 아트 납품 규칙: 투명 PNG 256×256, 아이콘 안 글자 없음(라벨은 TMP가 쓴다).
    /// </summary>
    internal sealed class UiIconPostprocessor : AssetPostprocessor
    {
        private const string UiFolder = "Assets/Art/UI/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;   // 256 원본을 40px 남짓으로 줄여 쓴다 — 밉맵이 없으면 테두리가 지글거린다.
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        }
    }
}

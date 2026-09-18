using UnityEditor;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// 건물 모델(.fbx)·텍스처 임포트 설정을 코드로 고정한다.
    ///
    /// 왜 코드로 정하는가 — 인스펙터에서 손으로 고치면 모델이 21채로 늘었을 때 한 채라도
    /// 빠뜨리면 그 한 채만 조용히 어긋난다. 설정은 코드가 정한다.
    ///
    /// <b>머티리얼은 여기서 만들지 않는다.</b> fbx가 들고 오는 머티리얼에 두 번 기댔다가
    /// 두 번 다 텍스처가 안 붙었다(2026-09-14·16). 지금은
    /// <see cref="TerritorySceneBuilder"/>가 .mat 에셋을 만들어 직접 입힌다 —
    /// 임포터 콜백이 언제 도는지에 매달리지 않고, 결과가 눈에 보이는 파일로 남는다.
    /// </summary>
    public sealed class BuildingModelPostprocessor : AssetPostprocessor
    {
        private const string BuildingFolder = "Assets/Art/Buildings/";

        /// <summary>
        /// ⚠️ 이 설정을 고치면 이 번호를 올려야 한다.
        ///
        /// Unity는 포스트프로세서 코드가 바뀌었다고 이미 임포트된 에셋을 다시 임포트하지
        /// 않는다 — 이 번호가 바뀔 때만 다시 한다. 실제로 밟았다(2026-09-14):
        /// 설정을 고쳤는데 21종이 전부 옛 설정 그대로였고, 텍스처가 안 붙은 채
        /// 경고조차 안 났다(콜백 자체가 안 돌았으니까).
        /// </summary>
        public override uint GetVersion() => 3;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(BuildingFolder))
                return;

            var importer = (ModelImporter)assetImporter;

            // fbx가 들고 오는 머티리얼은 쓰지 않는다.
            //
            // 두 번 기댔다가 두 번 다 실패했다(2026-09-14·16) — 임포터 콜백이 돌지 않아
            // 텍스처가 안 붙었고, 실패했다는 신호조차 없었다(콜백이 안 돌았으니 경고도 없다).
            // 지금은 TerritorySceneBuilder가 .mat 에셋을 만들어 직접 입힌다. 눈에 보이는
            // 파일이라 확인할 수 있고, 임포터가 언제 무엇을 하는지에 매달리지 않는다.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            // 건물은 움직이지 않는다. 애니메이션·리그를 끄면 임포트도 빨라지고
            // 씬에 쓸데없는 Animator가 붙지 않는다.
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;

            // 탭 판정은 지면 평면 교차로 한다 — 콜라이더가 필요 없다.
            importer.addCollider = false;

            // 조명이 형태를 읽어 주도록 법선은 파일 값을 그대로 쓴다.
            importer.importNormals = ModelImporterNormals.Import;
        }

        /// <summary>
        /// 건물 텍스처 임포트 설정.
        ///
        /// 원본은 2048²이지만 게임에는 1024²로 올린다 — 건물이 화면에서 300px 안팎으로
        /// 보이므로 2048은 눈에 보이는 이득 없이 메모리만 네 배 먹는다
        /// (2048² 압축 약 2.7MB × 21채 = 57MB vs 1024² 약 0.7MB × 21채 = 14MB).
        ///
        /// 원본을 줄이지 않고 임포트에서 줄이는 이유 — 나중에 근접 연출 같은 데서 더 큰
        /// 텍스처가 필요해지면 여기 숫자만 바꾸면 된다. 소스를 줄여 버리면 아트에서 다시 뽑아야 한다.
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BuildingFolder))
                return;

            var importer = (TextureImporter)assetImporter;

            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.mipmapEnabled = true;

            // 베이스컬러는 눈에 보이는 색이다 — sRGB로 읽어야 한다.
            importer.sRGBTexture = true;

            // 알파 채널이 없다(노멀맵·ORM 없이 컬러 한 장뿐인 구성이다).
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
        }

    }
}

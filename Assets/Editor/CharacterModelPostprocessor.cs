using UnityEditor;

namespace Olympus.Editor
{
    /// <summary>
    /// <c>Assets/Art/Characters/</c> 아래 FBX의 가져오기 규칙. 병사 몸과 동작 모음(Quaternius)이 같은 뼈대를 쓴다.
    ///
    /// - <b>Humanoid</b>로 가져온다 — 몸과 동작이 다른 파일이어도 Unity가 사람 뼈대로 이어 준다(리타깃).
    ///   Generic으로 두면 뼈 이름이 정확히 같아야 하고, 나중에 다른 몸(그리스 병사)을 넣을 때 동작을 못 물려받는다.
    /// - 머티리얼은 FBX에서 만들지 않는다 — 건물에서 두 번 텍스처가 안 붙은 일이 있어서
    ///   (<c>BuildingModelEntry</c> 주석) 우리가 만든 .mat을 직접 입힌다(<see cref="BattleSoldierSetup"/>).
    /// - 이름에 <c>Loop</c>가 든 동작은 반복으로 표시한다. 기본값이 "한 번"이라 걷다가 멈춘 채로 미끄러진다.
    /// </summary>
    internal sealed class CharacterModelPostprocessor : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Art/Characters/";

        /// <summary>아트 납품 규칙: 소품 파일 이름은 <c>prop_</c>로 시작한다.</summary>
        internal static bool IsProp(string path) => System.IO.Path.GetFileName(path).StartsWith("prop_");

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CharacterFolder))
                return;

            var importer = (ModelImporter)assetImporter;

            // 손에 드는 소품(창·방패)은 뼈대가 없다 — Humanoid로 가져오면 아바타를 못 만들어 오류가 난다.
            if (IsProp(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CharacterFolder) || IsProp(assetPath))
                return;

            var importer = (ModelImporter)assetImporter;

            // 이미 손으로 나눈 클립이 있으면 건드리지 않는다.
            if (importer.clipAnimations != null && importer.clipAnimations.Length > 0)
                return;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool loop = clips[i].name.Contains("Loop") || clips[i].takeName.Contains("Loop");
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;

                // 제자리 동작으로 — 위치는 시뮬레이션이 정한다. 동작이 몸을 옮기면 칸이 어긋난다.
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalPositionXZ = true;
                clips[i].lockRootHeightY = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = true;
            }

            importer.clipAnimations = clips;
        }
    }
}

using UnityEditor;
using UnityEngine;
using Olympus.Game.Territory;

namespace Olympus.Editor
{
    /// <summary>
    /// 세이브 파일을 다루는 에디터 메뉴.
    ///
    /// 왜 필요한가 — 진행이 저장되기 시작하면 "처음부터"를 볼 방법이 없어진다.
    /// 초반 밸런스는 반복해서 봐야 하는데, 파일 위치를 매번 찾아 들어가는 건 번거롭다.
    /// </summary>
    internal static class SaveMenu
    {
        [MenuItem("Olympus/디버그/저장 파일 지우기", false, 200)]
        private static void DeleteSave()
        {
            if (!SaveStore.Exists)
            {
                EditorUtility.DisplayDialog("저장 파일 지우기", "저장된 진행이 없습니다.\n\n" + SaveStore.Path, "확인");
                return;
            }

            bool ok = EditorUtility.DisplayDialog(
                "저장 파일 지우기",
                "저장된 진행을 지웁니다. 다음 플레이는 처음부터 시작합니다.\n\n" + SaveStore.Path,
                "지우기", "취소");

            if (!ok)
                return;

            SaveStore.Delete();
            Debug.Log("저장 파일을 지웠습니다: " + SaveStore.Path);
        }

        /// <summary>
        /// 건물 에셋을 강제로 다시 임포트한다.
        ///
        /// 왜 필요한가 — Unity는 임포트 설정 코드(<c>BuildingModelPostprocessor</c>)가
        /// 바뀌어도 이미 임포트된 에셋을 다시 임포트하지 않는다. 보통은 그 클래스의
        /// <c>GetVersion()</c>을 올려서 해결하지만, 그걸 잊었거나 임포트가 꼬였을 때
        /// 손으로 한 번 돌릴 수단이 있어야 한다.
        /// </summary>
        [MenuItem("Olympus/디버그/건물 에셋 다시 임포트", false, 210)]
        private static void ReimportBuildings()
        {
            const string folder = "Assets/Art/Buildings";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning("폴더가 없습니다: " + folder);
                return;
            }

            AssetDatabase.ImportAsset(folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("건물 에셋을 다시 임포트했습니다: " + folder);
        }

        [MenuItem("Olympus/디버그/저장 파일 위치 열기", false, 201)]
        private static void RevealSave()
        {
            Debug.Log("세이브 경로: " + SaveStore.Path + (SaveStore.Exists ? " (있음)" : " (아직 없음)"));
            EditorUtility.RevealInFinder(SaveStore.Path);
        }
    }
}

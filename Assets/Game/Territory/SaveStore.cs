using System;
using System.IO;
using UnityEngine;
using Olympus.Core.Save;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 세이브 파일을 기기에 읽고 쓴다.
    ///
    /// 여기에는 판단이 없다 — 형식과 병합 규칙은 전부 Core(<see cref="SaveMapper"/>)에 있고,
    /// 이 클래스는 JsonUtility와 파일 시스템만 다룬다. 그래야 "어떻게 복원하는가"가
    /// Unity 없이 시험된다.
    ///
    /// 저장 위치는 <c>Application.persistentDataPath</c>다 — 앱 업데이트로 지워지지 않고
    /// 플랫폼마다 알아서 맞는 자리를 준다. <c>Assets/</c> 아래에 쓰면 안 된다
    /// (빌드된 앱에는 그 폴더가 없다).
    /// </summary>
    public static class SaveStore
    {
        private const string FileName = "olympus-save.json";

        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists => File.Exists(Path);

        /// <summary>
        /// 저장을 읽는다. 파일이 없거나 깨졌으면 false — 진행이 없는 것으로 보고
        /// 처음부터 시작한다. 깨진 파일 때문에 게임이 아예 안 뜨는 것보다 낫다.
        /// </summary>
        public static bool TryLoad(out SaveFile save)
        {
            save = null;

            try
            {
                if (!File.Exists(Path))
                    return false;

                string json = File.ReadAllText(Path);
                save = JsonUtility.FromJson<SaveFile>(json);

                if (save == null)
                    return false;

                if (save.version > SaveFile.CurrentVersion)
                {
                    // 더 새 판으로 저장된 파일이다(앱을 되돌려 설치한 경우 등).
                    // 덮어쓰지 말고 그냥 무시한다 — 진행을 지우는 쪽이 더 나쁘다.
                    Debug.LogWarning(
                        "세이브가 더 새 형식입니다(파일 v" + save.version +
                        " > 앱 v" + SaveFile.CurrentVersion + "). 읽지 않고 새로 시작합니다.");
                    save = null;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("세이브를 읽지 못했습니다: " + e.Message + "\n경로: " + Path);
                save = null;
                return false;
            }
        }

        /// <summary>
        /// 저장한다. 임시 파일에 먼저 쓰고 바꿔치기한다 — 쓰는 도중에 앱이 죽어도
        /// 기존 세이브가 반쯤 덮인 채로 남지 않는다.
        /// </summary>
        public static void Save(SaveFile save)
        {
            if (save == null)
                return;

            try
            {
                string json = JsonUtility.ToJson(save, true);
                string temp = Path + ".tmp";

                File.WriteAllText(temp, json);

                if (File.Exists(Path))
                    File.Delete(Path);

                File.Move(temp, Path);
            }
            catch (Exception e)
            {
                Debug.LogError("세이브를 쓰지 못했습니다: " + e.Message + "\n경로: " + Path);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch (Exception e)
            {
                Debug.LogError("세이브를 지우지 못했습니다: " + e.Message);
            }
        }
    }
}

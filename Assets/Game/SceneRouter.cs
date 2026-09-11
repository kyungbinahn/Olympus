using UnityEngine.SceneManagement;

namespace Olympus.Game
{
    /// <summary>
    /// 화면(씬) 사이를 오가는 유일한 통로. 씬 이름 문자열이 코드 곳곳에 흩어지면
    /// 씬 이름을 바꾸는 순간 조용히 깨진다(Unity는 없는 씬을 부르면 경고만 낸다).
    ///
    /// ⚠️ 여기 이름을 바꾸면 Build Settings의 씬 목록도 함께 고쳐야 한다 —
    /// 목록에 없는 씬은 빌드에서 로드되지 않는다.
    /// </summary>
    public static class SceneRouter
    {
        public const string TerritoryScene = "Territory";
        public const string WorldScene = "World";

        /// <summary>기지 화면으로.</summary>
        public static void GoToTerritory()
        {
            SceneManager.LoadScene(TerritoryScene);
        }

        /// <summary>월드맵으로.</summary>
        public static void GoToWorld()
        {
            SceneManager.LoadScene(WorldScene);
        }
    }
}

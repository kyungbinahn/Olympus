using Olympus.Core.Territory;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 기지 상태를 씬보다 오래 붙잡아 두는 자리.
    ///
    /// 왜 필요한가 — 상태를 <see cref="TerritoryPresenter"/>(뷰)가 Awake에서 만들고 있었다.
    /// 씬을 다시 로드하면 새 상태가 생기므로 기지 → 월드 → 기지로 돌아오면 복구해 둔
    /// 건물이 전부 폐허로 되돌아갔다(2026-09-14에 밟았다).
    /// <b>상태는 화면의 소유물이 아니다</b> — 화면은 왔다 갔다 하지만 진행은 남아야 한다.
    ///
    /// 이 자리는 나중에 서버가 들어올 자리이기도 하다. 서버가 붙으면 상태의 출처만
    /// 여기서 바뀌고 프리젠터는 그대로다 — 지금 델타 한 경로로 모아 둔 이유가 그것이다.
    ///
    /// ⚠️ 앱을 껐다 켜면 사라진다. 기기에 남기는 저장(세이브 파일)은 아직 없다.
    ///    에디터에서도 플레이를 멈추면 도메인이 리로드되면서 같이 날아간다.
    /// </summary>
    public static class TerritorySession
    {
        public static TerritoryRuntime Runtime { get; private set; }

        public static bool HasRuntime => Runtime != null;

        /// <summary>이 런타임을 세션이 들고 간다. 기지에 처음 들어올 때 한 번 부른다.</summary>
        public static void Adopt(TerritoryRuntime runtime)
        {
            Runtime = runtime;
        }

        /// <summary>
        /// 세션을 버리고 다음 진입 때 처음부터 다시 시작하게 한다.
        /// 디버그·시험용이다 — 게임 규칙으로 진행을 지우는 경로가 아니다.
        /// </summary>
        public static void Clear()
        {
            if (Runtime != null)
                Runtime.Dispose();

            Runtime = null;
        }
    }
}

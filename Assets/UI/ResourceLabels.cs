using Olympus.Core.State;

namespace Olympus.UI
{
    /// <summary>자원 종류의 화면 표기. 문구 자체는 코드에 있지만, 세계관 이름이 아니라
    /// 그리스·로마 자원 네 가지 고정 축이라 로컬라이즈 테이블까지 갈 만큼 자주 바뀌지 않는다.</summary>
    internal static class ResourceLabels
    {
        public static string Of(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Wood: return "목재";
                case ResourceKind.Stone: return "석재";
                case ResourceKind.Food: return "식량";
                case ResourceKind.Gold: return "금";
                default: return kind.ToString();
            }
        }
    }
}

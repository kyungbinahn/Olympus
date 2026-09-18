using System;
using System.Collections.Generic;

namespace Olympus.Core.Save
{
    /// <summary>
    /// 기기에 남기는 진행 상황.
    ///
    /// <b>여기에는 "진행"만 넣고 "설정값"은 넣지 않는다.</b> 건설 비용·걸리는 시간·
    /// 풋프린트 같은 것은 <c>building-defs.json</c>에서 매번 다시 읽는다. 그래야
    /// 기획이 숫자를 고쳐도 이미 저장된 진행이 안 깨진다 — 지금처럼 데이터가 임시값인
    /// 동안에는 이 구분이 특히 중요하다. 비용을 세이브에 적어 두면 밸런스를 만질 때마다
    /// 세이브 마이그레이션이 따라붙는다.
    ///
    /// 필드가 평평하고 public인 것은 Unity의 JsonUtility가 그 형태만 읽기 때문이다.
    /// 그래도 이 파일은 Core에 있다 — 형식과 병합 규칙은 판단이고, 판단은 시험이 걸리는
    /// 곳에 있어야 한다. Unity에 묶이는 것은 파일을 읽고 쓰는 부분뿐이다(SaveStore).
    /// </summary>
    [Serializable]
    public sealed class SaveFile
    {
        /// <summary>형식 판번호. 나중에 형태가 바뀌면 이 값으로 갈아타기를 판단한다.</summary>
        public int version = CurrentVersion;

        public long savedAtUnixMs;

        public List<SavedResource> resources = new List<SavedResource>();
        public List<SavedSlot> slots = new List<SavedSlot>();

        public const int CurrentVersion = 1;
    }

    [Serializable]
    public sealed class SavedResource
    {
        /// <summary>
        /// <c>ResourceKind</c>의 이름. 숫자가 아니라 이름으로 두는 이유 —
        /// 나중에 enum에 항목을 끼워 넣어도 저장된 값의 뜻이 바뀌지 않는다.
        /// </summary>
        public string kind;

        public long amount;
    }

    [Serializable]
    public sealed class SavedSlot
    {
        public int slotId;

        /// <summary>
        /// 그 자리에 있던 건물 종류. 레이아웃이 바뀌어 같은 자리에 다른 건물이 서게 되면
        /// 이 값이 달라지고, 그때는 저장을 버리고 새로 시작한다(엉뚱한 건물의 진행을
        /// 물려받으면 조용히 이상해진다).
        /// </summary>
        public string defId;

        /// <summary><c>BuildingPhase</c>의 이름.</summary>
        public string phase;

        public int level;

        /// <summary>
        /// 공사 완료 시각(유닉스 밀리초). 남은 시간이 아니라 끝나는 시각이라
        /// 앱을 꺼 둔 동안에도 공사가 진행된다.
        /// </summary>
        public long constructionEndsAtUnixMs;
    }
}

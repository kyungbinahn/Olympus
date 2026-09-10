using System;
using Olympus.Core.State;
using Olympus.Core.Time;

namespace Olympus.Core.Territory
{
    /// <summary>
    /// 기지 로직 한 벌을 묶어 서로 배선한다.
    ///
    /// 왜 이 클래스가 필요한가 — 처음에는 뷰(프레젠터)가 스토어·건설·복구도를 각각 들고
    /// 필요할 때 <see cref="RestorationMap.Recompute"/>를 부르게 했다. 그러다
    /// **즉시 완성(건설시간 0) 경로에서 재계산을 빠뜨려 녹지가 안 뜨는 버그**가 났다.
    /// 타이머 만료 경로에만 재계산을 붙였기 때문이다.
    ///
    /// 부르는 걸 잊을 수 있는 배선은 결국 잊힌다. 그래서 상태 변경 통지에 재계산을
    /// 묶어 두고, 뷰는 읽기만 하게 한다. 이 배선이 Core에 있으므로 Unity 없이 시험된다 —
    /// 실제로 위 버그를 재현하는 시험이 <c>TerritoryRuntimeTests</c>에 있다.
    /// </summary>
    public sealed class TerritoryRuntime
    {
        public GameState State { get; }
        public StateStore Store { get; }
        public BuildingCatalog Catalog { get; }
        public ConstructionService Construction { get; }
        public RestorationMap Restoration { get; }
        public IClock Clock { get; }

        public TerritoryRuntime(
            GameState state,
            BuildingCatalog catalog,
            IClock clock,
            float restorationRadius = 2f)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));

            Store = new StateStore(state);
            Construction = new ConstructionService(Store, catalog, clock);
            Restoration = new RestorationMap(state.BaseOccupancy.Bounds, restorationRadius);

            // 건물이 바뀌면 무조건 녹지를 다시 센다. 어느 경로로 바뀌었는지 따지지 않는다 —
            // 경로별로 재계산을 붙이는 방식이 위 버그의 원인이었다.
            // Recompute는 결과가 같으면 Version을 올리지 않으므로 자주 불러도 싸다.
            Store.Changed += OnStateChanged;
        }

        private void OnStateChanged(StateDelta delta)
        {
            if (delta.Buildings == null)
                return;

            Restoration.Recompute(State.Buildings, Catalog);
        }

        /// <summary>
        /// 레이아웃대로 자리를 폐허로 세운다. 기지에 들어갈 때 한 번 부른다.
        /// </summary>
        public void Initialize(BaseLayout layout)
        {
            Construction.InitializeFromLayout(layout);

            // 레이아웃에 처음부터 완성 상태인 자리가 생길 수 있으므로 한 번 맞춰 둔다.
            Restoration.Recompute(State.Buildings, Catalog);
        }

        /// <summary>
        /// 시간이 지난 공사를 완료로 넘긴다. 매 프레임 불러도 된다.
        /// 완료된 자리 수를 돌려준다.
        /// </summary>
        public int Tick()
        {
            return Construction.CompleteFinished();
        }

        public void Dispose()
        {
            Store.Changed -= OnStateChanged;
        }
    }
}

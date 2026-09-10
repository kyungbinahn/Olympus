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

        /// <summary>
        /// 녹지 확산 반경을 바꾸고 즉시 다시 센다.
        /// 플레이 중에 인스펙터로 값을 돌려 보며 눈으로 정할 수 있게 하려고 둔 것이다.
        /// </summary>
        public void SetRestorationRadius(float radius)
        {
            if (Restoration.Radius == radius)
                return;

            Restoration.Radius = radius;
            Restoration.Recompute(State.Buildings, Catalog);
        }

        /// <summary>
        /// 진행 중인 공사를 전부 즉시 완료시킨다. 완료한 수를 돌려준다.
        ///
        /// 디버그용이다 — 화면이 어떻게 보이는지 판단하려면 건물마다 건설 시간을
        /// 기다릴 수 없다. 게임 규칙이 아니므로 UI에 노출하지 않는다.
        /// </summary>
        public int DebugCompleteAllConstruction()
        {
            var delta = new StateDelta();
            int count = 0;

            foreach (BuildingInstance b in State.Buildings)
            {
                if (b.Phase != BuildingPhase.Constructing)
                    continue;

                delta.WithBuilding(BuildingChange.Update(
                    b.Id, b.Anchor, BuildingPhase.Complete, b.Level + 1, 0L));
                count++;
            }

            if (count > 0)
                Store.Apply(delta);

            return count;
        }

        /// <summary>
        /// 모든 폐허를 비용·시간 없이 즉시 복구한다. 복구한 수를 돌려준다.
        ///
        /// 디버그용이다 — 전부 복구된 최종 화면(녹지가 어디까지 번지는지)을
        /// 바로 보기 위한 것이다. 비용을 건너뛰므로 게임 규칙이 아니다.
        /// </summary>
        public int DebugRestoreAll()
        {
            var delta = new StateDelta();
            int count = 0;

            foreach (BuildingInstance b in State.Buildings)
            {
                if (b.Phase == BuildingPhase.Complete)
                    continue;

                delta.WithBuilding(BuildingChange.Update(
                    b.Id, b.Anchor, BuildingPhase.Complete, 1, 0L));
                count++;
            }

            if (count > 0)
                Store.Apply(delta);

            return count;
        }

        public void Dispose()
        {
            Store.Changed -= OnStateChanged;
        }
    }
}

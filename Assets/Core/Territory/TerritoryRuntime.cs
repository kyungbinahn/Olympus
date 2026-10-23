using System;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Progression;
using Olympus.Core.Save;
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

        public ProgressionConfig Progression { get; }
        public UpgradeService Upgrades { get; }
        public ProductionService Production { get; }
        public SpeedUpService SpeedUps { get; }
        public QuestService Quests { get; }
        public BattleService Battles { get; }
        public TrainingService Training { get; }
        public GiantThreatService Threats { get; }
        public RaidService Raids { get; }

        public TerritoryRuntime(
            GameState state,
            BuildingCatalog catalog,
            IClock clock,
            float restorationRadius = 2f,
            ProgressionConfig progression = null,
            BattleConfig battle = null,
            ChapterConfig chapter = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Progression = progression ?? ProgressionConfig.Empty;

            Store = new StateStore(state);
            Construction = new ConstructionService(Store, catalog, clock, Progression);
            Upgrades = new UpgradeService(Store, catalog, clock, Progression);
            Production = new ProductionService(Store, catalog, clock);
            SpeedUps = new SpeedUpService(Store, clock, Progression);
            Quests = new QuestService(Store, Progression);
            Battles = new BattleService(Store, battle, Progression);
            Training = new TrainingService(Store, clock, chapter, Progression);
            Threats = new GiantThreatService(Store, clock, chapter, Progression, Battles);
            Training.IsBlocked = () => Threats.AnyArrived;
            Raids = new RaidService(Store, clock, chapter, Progression, Battles);
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

        /// <summary>본부 레벨(본부 건물의 레벨). 본부가 폐허면 0.</summary>
        public int HqLevel => ProgressionRules.HqLevel(State, Progression);

        public bool IsFeatureUnlocked(string featureId) => Progression.Features.IsUnlocked(featureId, HqLevel);

        /// <summary>
        /// 레이아웃대로 자리를 폐허로 세우고 시작 자원·아이템을 준다. 처음 시작할 때 한 번 부른다.
        /// </summary>
        public void Initialize(BaseLayout layout)
        {
            Construction.InitializeFromLayout(layout);

            var starting = new StateDelta().WithResources(Progression.StartingResources);
            for (int i = 0; i < Progression.StartingItems.Count; i++)
            {
                ItemGrant g = Progression.StartingItems[i];
                starting.WithItem(CountChange.Delta(g.ItemId, g.Amount));
            }

            Store.Apply(starting);

            // 레이아웃에 처음부터 완성 상태인 자리가 생길 수 있으므로 한 번 맞춰 둔다.
            Restoration.Recompute(State.Buildings, Catalog);
        }

        /// <summary>
        /// 저장된 진행으로 기지를 세운다. <paramref name="save"/>가 null이면
        /// <see cref="Initialize"/>와 같게 전부 폐허로 시작한다.
        ///
        /// 레이아웃 검증을 똑같이 지난다 — 세이브가 있다고 데이터 실수를 건너뛰면 안 된다.
        /// </summary>
        public SaveLoadReport InitializeFromSave(BaseLayout layout, SaveFile save)
        {
            Construction.ValidateOrThrow(layout);

            SaveLoadReport report;
            StateDelta delta = SaveMapper.ToDelta(save, layout, Catalog, out report);

            Store.Apply(delta);
            Restoration.Recompute(State.Buildings, Catalog);

            return report;
        }

        /// <summary>지금 진행을 저장 형태로 뜬다.</summary>
        public SaveFile CaptureSave()
        {
            return SaveMapper.Capture(State, Clock.NowUnixMs);
        }

        /// <summary>
        /// 시간이 지난 공사를 완료로 넘긴다. 매 프레임 불러도 된다.
        /// 완료된 자리 수를 돌려준다.
        /// </summary>
        public int Tick()
        {
            int completed = Construction.CompleteFinished();

            // 훈련 완료와 거인 등장도 같은 박자로 — 화면마다 따로 부르면 어느 화면에서는 안 불리는 일이 생긴다(§4 함정).
            Training.CompleteFinished();
            Threats.Tick();
            Raids.Tick();

            return completed;
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
            long now = Clock.NowUnixMs;
            int count = 0;

            foreach (BuildingInstance b in State.Buildings)
            {
                if (b.Phase != BuildingPhase.Constructing)
                    continue;

                // 규칙과 같은 완성 경로를 탄다 — 디버그로 끝낸 본부 레벨업에서 보상이 빠지면
                // "디버그로 보면 가속권이 안 들어온다"를 버그로 착각하게 된다.
                ProgressionRules.AppendCompletion(delta, b, b.Level + 1, now, Progression);
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
            long now = Clock.NowUnixMs;
            int count = 0;

            foreach (BuildingInstance b in State.Buildings)
            {
                if (b.Phase == BuildingPhase.Complete)
                    continue;

                delta.WithBuilding(BuildingChange.Update(
                    b.Id, b.Anchor, BuildingPhase.Complete, b.Level > 0 ? b.Level : 1, 0L, now));
                count++;
            }

            if (count > 0)
                Store.Apply(delta);

            return count;
        }

        /// <summary>
        /// 자원을 비용 없이 준다. 디버그용 — 곡선을 끝까지 따라가 보지 않고 Lv.4·5 화면을
        /// 바로 보려는 것이다. 게임 규칙이 아니다.
        /// </summary>
        public void DebugGrantResources(long amount)
        {
            var delta = new StateDelta();

            foreach (ResourceKind kind in Enum.GetValues(typeof(ResourceKind)))
            {
                delta.WithResource(ResourceChange.Delta(kind, amount));
            }

            Store.Apply(delta);
        }

        /// <summary>다가오는 거인을 지금 도착시킨다(아직 안 보이면 먼저 보이게). 화면 확인용.</summary>
        public bool DebugThreatArriveNow()
        {
            Threats.Tick();
            ThreatDef t = Threats.Current;
            if (t == null || Threats.Describe(t).Phase == ThreatPhase.Hidden)
                return false;

            Store.Apply(new StateDelta().WithCounter(
                CountChange.Absolute(ChapterKeys.ThreatArrives(t.ThreatId), Clock.NowUnixMs)));
            return true;
        }

        /// <summary>다음 도적단을 지금 걸어오게 한다(몇 초 뒤 도착). 습격 규칙이 없거나 본부가 안 열렸으면 false.</summary>
        public bool DebugRaidSoon()
        {
            Raids.Tick();
            if (Raids.Def == null || Raids.Describe().Phase == RaidPhase.Off)
                return false;

            long soon = Clock.NowUnixMs + System.Math.Max(3000L, Raids.Def.ApproachMs / 2);
            Store.Apply(new StateDelta()
                .WithCounter(CountChange.Absolute(RaidService.NextKey, soon))
                .WithCounter(CountChange.Absolute(RaidService.EngagedKey, 0)));
            return true;
        }

        public void DebugGrantTroops(int count)
        {
            if (Training.Def == null || count <= 0)
                return;

            Store.Apply(new StateDelta()
                .WithCounter(CountChange.Delta(ChapterKeys.Troops(Training.Def.UnitId), count))
                .WithCounter(CountChange.Delta(ChapterKeys.Trained, count)));
        }

        public void Dispose()
        {
            Store.Changed -= OnStateChanged;
        }
    }
}

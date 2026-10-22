using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Battle;
using Olympus.Core.Territory;
using Olympus.Game.Territory;

namespace Olympus.Game.Battle
{
    /// <summary>
    /// 병사 한 종류에 붙일 실제 모델. 비워 두면 기본 도형 병사로 선다.
    ///
    /// 모델에 <see cref="Animator"/>가 있으면 세 신호를 보낸다 — <c>Speed</c>(float, 걸으면 1),
    /// <c>Attack</c>(trigger), <c>Die</c>(trigger). 애니메이터 컨트롤러가 이 이름을 쓰면 된다.
    /// 없으면 도형 병사처럼 흔들기·찌르기·넘어지기로 흉내 낸다.
    /// </summary>
    /// <summary>소품을 어디에 어떻게 드나. 아트 납품 규칙: 소품의 "앞"(창끝·방패 앞면)이 +Z, 원점이 쥐는 점.</summary>
    public enum PropSocket
    {
        /// <summary>오른손으로 쥐고, T포즈에서 창끝이 정면을 향한다.</summary>
        SpearRightHand = 0,

        /// <summary>왼 팔뚝에 끼우고, T포즈에서 앞면이 위를 본다 — 팔을 내리면 바깥을 본다.</summary>
        ShieldLeftForearm = 1,
    }

    [System.Serializable]
    public sealed class BattlePropEntry
    {
        public GameObject model;
        public Material material;

        [Tooltip("적 진영일 때 입힐 머티리얼(팀색이 다른 판). 비우면 위의 것을 쓴다.")]
        public Material enemyMaterial;

        public PropSocket socket;
    }

    [System.Serializable]
    public sealed class BattleUnitModelEntry
    {
        public string unitId;
        public GameObject model;

        [Tooltip("이 병사의 동작. 비우면 모델에 붙어 온 것을 쓴다.")]
        public RuntimeAnimatorController controller;

        [Tooltip("입힐 머티리얼. 아래 적 머티리얼이 비어 있으면 진영 색(파랑/붉은 갈색)을 몸 전체에 덧칠한다.")]
        public Material material;

        [Tooltip("적 진영일 때 입힐 머티리얼. 채우면 덧칠하지 않는다 — 팀색이 마스크 영역(볏·망토)에만 " +
                 "구워진 판을 진영마다 따로 쓰는 것이다. 위의 머티리얼이 아군 판이 된다.")]
        public Material enemyMaterial;

        [Tooltip("손에 드는 것(창·방패).")]
        public List<BattlePropEntry> props = new List<BattlePropEntry>();

        [Tooltip("모델 크기 보정. 1이면 병사 몸 높이(반지름 × 4.5)에 맞춘다.")]
        public float scale = 1f;

        [Tooltip("사람 뼈대(Humanoid) 모델은 어깨 위치로 정면을 재서 저절로 돌린다 — 이 값은 그 위에 더하는 손 보정(도)이다. " +
                 "뼈대가 없는 모델은 이 값만 쓴다.")]
        public float yaw = 0f;
    }

    public enum BattlePhase
    {
        /// <summary>양 진영이 서 있고 "전투 시작"을 기다린다.</summary>
        Ready = 0,
        Fighting = 1,
        Result = 2,

        /// <summary>스테이지를 다 깼다.</summary>
        NoStage = 3,

        /// <summary>전선이 아직 안 열렸다(본부 레벨).</summary>
        Locked = 4,
    }

    /// <summary>
    /// 전선 화면을 조립하고 돌린다. 판단(누가 이기나·보상)은 전부 Core(<see cref="BattleService"/>)에 있다.
    ///
    /// 화면은 Core의 시뮬레이션을 한 걸음(50ms)씩 돌리며 그리기만 한다. 끝나면 결과 적용은
    /// <see cref="BattleService.Fight"/>가 같은 입력으로 다시 끝까지 돌려서 한다 — 시뮬레이션에
    /// 무작위가 없어서 둘이 같다(시험이 그걸 확인한다). 화면이 결과를 들고 가 적용하지 않는 이유는,
    /// 그러면 "화면이 이겼다고 말하면 이긴 것"이 되어 서버가 붙을 때 그 길을 다 막아야 하기 때문이다.
    /// </summary>
    public sealed class BattlePresenter : MonoBehaviour
    {
        [Header("재질")]
        [SerializeField] private Material _playerMaterial;
        [SerializeField] private Material _enemyMaterial;
        [SerializeField] private Material _skinMaterial;
        [SerializeField] private Material _bronzeMaterial;
        [SerializeField] private Material _woodMaterial;
        [SerializeField] private Material _flashMaterial;
        [SerializeField] private Material _hpBackMaterial;
        [SerializeField] private Material _hpPlayerMaterial;
        [SerializeField] private Material _hpEnemyMaterial;

        [Header("실제 모델 (비우면 기본 도형 병사)")]
        [SerializeField] private List<BattleUnitModelEntry> _unitModels = new List<BattleUnitModelEntry>();

        [Tooltip("실제 모델에 덧칠할 진영 색. 몸 텍스처 위에 곱해진다.")]
        [SerializeField] private Color _playerTint = new Color(0.55f, 0.70f, 1f);
        [SerializeField] private Color _enemyTint = new Color(1f, 0.55f, 0.45f);

        [Header("연출")]
        [Tooltip("화살이 날아가는 시간(초). 결과와 무관한 볼거리다.")]
        [SerializeField] private float _arrowSeconds = 0.3f;

        private TerritoryRuntime _runtime;
        private Camera _camera;
        private Transform _unitRoot;

        private readonly Dictionary<int, BattleUnitView> _views = new Dictionary<int, BattleUnitView>();
        private readonly List<Arrow> _arrows = new List<Arrow>();

        private float _accumulatedMs;

        public BattlePhase Phase { get; private set; }
        public StageDef Stage { get; private set; }
        public BattlePreview Preview { get; private set; }
        public BattleSimulation Simulation { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public TerritoryRuntime Runtime => _runtime;

        /// <summary>1 또는 2.</summary>
        public int Speed { get; private set; } = 1;

        /// <summary>단계가 바뀔 때마다 알린다. HUD가 패널을 갈아 끼운다.</summary>
        public event System.Action PhaseChanged;

        private sealed class Arrow
        {
            public Transform Transform;
            public Vector3 From;
            public Vector3 To;
            public float StartedAt;
        }

        private void Awake()
        {
            // 전투는 기지 세션의 군대·진도로 싸운다. 기지를 거치지 않고 이 씬을 바로 틀었으면 기지로 돌린다.
            if (!TerritorySession.HasRuntime)
            {
                Debug.Log("전투 씬은 기지에서 들어와야 합니다 — 기지 씬으로 이동합니다.");
                SceneRouter.GoToTerritory();
                enabled = false;
                return;
            }

            _runtime = TerritorySession.Runtime;
            _camera = Camera.main;
            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(transform, false);

            BattleService battles = _runtime.Battles;

            if (!battles.IsUnlocked)
            {
                Phase = BattlePhase.Locked;
                return;
            }

            Stage = battles.NextStage;
            if (Stage == null)
            {
                Phase = BattlePhase.NoStage;
                return;
            }

            Preview = battles.Preview(Stage.StageId);
            Simulation = battles.CreateSimulation(Stage.StageId);
            BuildViews();
            Phase = BattlePhase.Ready;
        }

        public void StartBattle()
        {
            if (Phase != BattlePhase.Ready || !Preview.CanFight)
                return;

            Phase = BattlePhase.Fighting;
            PhaseChanged?.Invoke();
        }

        public void ToggleSpeed()
        {
            Speed = Speed == 1 ? 2 : 1;
            PhaseChanged?.Invoke();
        }

        /// <summary>끝까지 바로 돌린다. 쓰러짐 연출은 이어지도록 사건을 그대로 흘려보낸다.</summary>
        public void Skip()
        {
            if (Phase != BattlePhase.Fighting)
                return;

            while (!Simulation.IsFinished)
                StepOnce();

            Finish();
        }

        public void Leave() => SceneRouter.GoToTerritory();

        /// <summary>다음 스테이지 — 씬을 다시 연다. 군대가 새 진도에서 다시 짜인다.</summary>
        public void NextStage() => SceneRouter.GoToBattle();

        private void Update()
        {
            if (Simulation == null)
                return;

            if (Phase == BattlePhase.Fighting)
            {
                _accumulatedMs += Time.deltaTime * 1000f * Speed;

                while (_accumulatedMs >= BattleSimulation.StepMs && !Simulation.IsFinished)
                {
                    _accumulatedMs -= BattleSimulation.StepMs;
                    StepOnce();
                }

                if (Simulation.IsFinished)
                    Finish();
            }

            float t = Phase == BattlePhase.Fighting ? Mathf.Clamp01(_accumulatedMs / BattleSimulation.StepMs) : 1f;

            for (int i = 0; i < Simulation.Units.Count; i++)
            {
                BattleUnit u = Simulation.Units[i];
                BattleUnitView view;
                if (_views.TryGetValue(u.Id, out view) && view.Root.gameObject.activeSelf)
                    view.Render(u, t, _camera);
            }

            UpdateArrows();
        }

        private void StepOnce()
        {
            Simulation.Step();

            for (int i = 0; i < Simulation.Units.Count; i++)
            {
                BattleUnit u = Simulation.Units[i];
                BattleUnitView view;
                if (_views.TryGetValue(u.Id, out view))
                    view.OnStep(u, 0f);
            }

            IReadOnlyList<BattleEvent> events = Simulation.LastEvents;
            for (int i = 0; i < events.Count; i++)
            {
                BattleEvent e = events[i];
                BattleUnitView source, target;
                _views.TryGetValue(e.SourceId, out source);
                _views.TryGetValue(e.TargetId, out target);

                if (e.Kind == BattleEventKind.Attack)
                {
                    if (source != null)
                        source.OnAttack();
                    if (target != null)
                        target.OnHit();

                    BattleUnit attacker;
                    if (Simulation.TryGetUnit(e.SourceId, out attacker) && attacker.Def.IsRanged && source != null && target != null)
                        LaunchArrow(source.Root.position, target.Root.position);
                }
                else if (e.Kind == BattleEventKind.Death && target != null)
                {
                    target.OnDeath();
                }
            }
        }

        private void Finish()
        {
            if (Phase != BattlePhase.Fighting)
                return;

            Outcome = _runtime.Battles.Fight(Stage.StageId);

            if (Outcome.Won != Simulation.PlayerWon)
            {
                // 일어나면 안 된다 — 시뮬레이션에 무작위나 순서 의존이 끼어들었다는 뜻이다.
                Debug.LogError("화면의 전투 결과와 적용된 결과가 다릅니다. 시뮬레이션이 결정론을 잃었습니다.");
            }

            if (Outcome.Won && TerritorySession.SaveEnabled)
                SaveStore.Save(_runtime.CaptureSave());

            Phase = BattlePhase.Result;
            PhaseChanged?.Invoke();
        }

        // ---------- 병사 모습 ----------

        private void BuildViews()
        {
            for (int i = 0; i < Simulation.Units.Count; i++)
            {
                BattleUnit u = Simulation.Units[i];
                _views[u.Id] = CreateView(u);
            }
        }

        private BattleUnitView CreateView(BattleUnit u)
        {
            bool player = u.Team == BattleTeam.Player;
            float r = u.Radius;
            float height = r * 4.5f;

            var root = new GameObject((player ? "P" : "E") + u.Id + "_" + u.Def.UnitId).transform;
            root.SetParent(_unitRoot, false);
            root.rotation = Quaternion.LookRotation(new Vector3(u.FacingX, 0f, u.FacingZ));

            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            Animator animator = null;
            Color? tint = null;
            BattleUnitModelEntry entry = ModelFor(u.Def.UnitId);

            // 모델 배율은 키에 곱한다 — 체력바도 같이 올라가야 거인 머리 속에 묻히지 않는다.
            if (entry != null && entry.scale > 0.01f)
                height *= entry.scale;

            if (entry != null)
            {
                animator = BuildModelSoldier(body, entry, height, player);

                // 진영별 판이 있으면 이미 팀색이 볏·망토에만 구워져 있다 — 몸 전체를 덧칠하지 않는다.
                if (entry.enemyMaterial == null)
                    tint = player ? _playerTint : _enemyTint;
            }
            else
            {
                BuildPrimitiveSoldier(body, u, player, r, height);
            }

            // 체력바 — 머리 위, 카메라를 향한다.
            float barWidth = Mathf.Max(0.8f, r * 2.2f);
            var bar = new GameObject("HpBar").transform;
            bar.SetParent(root, false);
            bar.localPosition = new Vector3(0f, height + 0.35f, 0f);
            Part(bar, PrimitiveType.Cube, _hpBackMaterial, Vector3.zero, new Vector3(barWidth, 0.12f, 0.02f));
            Transform fill = Part(bar, PrimitiveType.Cube, player ? _hpPlayerMaterial : _hpEnemyMaterial,
                new Vector3(0f, 0f, -0.01f), new Vector3(barWidth, 0.1f, 0.02f));
            bar.gameObject.SetActive(false);

            return new BattleUnitView(u.Id, root, body, animator, _flashMaterial, tint, bar, fill, barWidth,
                new Vector3(u.X, 0f, u.Z));
        }

        /// <summary>
        /// 실제 모델을 세운다. 키를 병사 몸 높이에 맞추고(거인은 크게), 머티리얼을 직접 입히고,
        /// 동작이 몸을 옮기지 않게 한다 — 위치는 시뮬레이션이 정한다.
        /// </summary>
        private Animator BuildModelSoldier(Transform body, BattleUnitModelEntry entry, float height, bool player)
        {
            GameObject model = Instantiate(entry.model, body);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = model.AddComponent<Animator>();

            FaceForward(model.transform, animator, body);
            model.transform.localRotation = Quaternion.Euler(0f, entry.yaw, 0f) * model.transform.localRotation;

            Material bodyMaterial = !player && entry.enemyMaterial != null ? entry.enemyMaterial : entry.material;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (bodyMaterial != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterial = bodyMaterial;
            }

            // 모델 원래 키를 재서 맞춘다 — 모델마다 단위(미터/센티)가 달라도 같은 키로 선다.
            // 소품을 붙이기 전에 잰다(창이 머리 위로 솟으면 키가 창 길이로 잡힌다).
            float modelHeight = 0f;
            for (int i = 0; i < renderers.Length; i++)
                modelHeight = Mathf.Max(modelHeight, renderers[i].bounds.max.y - body.position.y);

            // 소품은 키를 맞추기 전에, 모델이 아직 T포즈(가져온 그대로)일 때 붙인다 — 아트가 소켓을
            // "T포즈에서 어디·어느 방향"으로 정했다. 뼈에 붙인 뒤 모델을 키우면 소품도 같이 커진다.
            AttachProps(model.transform, animator, entry, body, modelHeight, player);

            if (modelHeight > 0.01f)
                model.transform.localScale *= height / modelHeight;

            if (entry.controller != null)
                animator.runtimeAnimatorController = entry.controller;

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        /// <summary>
        /// 모델의 정면을 어깨 위치로 재서 <paramref name="frame"/>의 정면(적 쪽)에 맞춘다.
        /// 정면 = 오른 어깨 − 왼 어깨 방향 × 위. 파일마다 정면 축(+Z/-Z)이 달라서 숫자로 맞췄더니 두 번 뒤로 걸었다
        /// (Quaternius -Z, 아트 납품물은 Blender 내보내기 축 때문에 또 반대) — 그래서 재서 돌린다.
        /// </summary>
        private static void FaceForward(Transform model, Animator animator, Transform frame)
        {
            if (animator == null || !animator.isHuman)
                return;

            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (left == null || right == null)
                return;

            Vector3 across = right.position - left.position;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                return;

            Vector3 facing = Vector3.Cross(across, Vector3.up);
            Vector3 want = frame.forward;
            want.y = 0f;

            float angle = Vector3.SignedAngle(facing, want, Vector3.up);
            model.rotation = Quaternion.AngleAxis(angle, Vector3.up) * model.rotation;
        }

        /// <summary>
        /// 소품을 손·팔뚝 뼈에 붙인다. 위치·방향을 T포즈 월드 기준으로 맞춘 뒤 뼈의 자식으로 넣는다(월드 자세 유지) —
        /// 그러면 뼈 축이 어떻게 생겼든 상관없다. 아트 쪽이 Blender 뼈 로컬 숫자로 준 소켓 값을 그대로 옮기면
        /// FBX 축 변환 때문에 틀어지므로 쓰지 않는다.
        /// </summary>
        private static void AttachProps(Transform model, Animator animator, BattleUnitModelEntry entry,
            Transform frame, float modelHeight, bool player)
        {
            if (entry.props == null || entry.props.Count == 0)
                return;

            if (animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning(entry.unitId + ": 휴머노이드 아바타가 없어 소품을 붙이지 못했습니다(가져오기 설정 확인).");
                return;
            }

            // 소켓 거리는 키 1.95m 모델 기준 — 모델 단위가 달라도 같은 비율로.
            float k = modelHeight > 0.01f ? modelHeight / 1.95f : 1f;

            for (int i = 0; i < entry.props.Count; i++)
            {
                BattlePropEntry p = entry.props[i];
                if (p == null || p.model == null)
                    continue;

                Transform bone;
                Vector3 position;
                Quaternion rotation;

                if (p.socket == PropSocket.SpearRightHand)
                {
                    bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    if (bone == null) continue;

                    position = bone.position;
                    rotation = Quaternion.LookRotation(frame.forward, frame.up);
                }
                else
                {
                    bone = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    if (bone == null || hand == null) continue;

                    Vector3 armDir = (hand.position - bone.position).normalized;
                    Vector3 face = frame.up;

                    // 방패 위쪽 = 팔꿈치 쪽(손 반대). 팔을 내리면 문양이 똑바로 선다.
                    Vector3 up = Vector3.ProjectOnPlane(-armDir, face).normalized;
                    position = bone.position + armDir * 0.117f * k + face * 0.05f * k;
                    rotation = Quaternion.LookRotation(face, up);
                }

                // 크기는 건드리지 않는다 — 몸과 같은 내보내기 도구에서 나와 단위가 이미 같다.
                // 아래 SetParent가 월드 크기를 지키고, 그 뒤 몸을 키우면 소품도 같이 커진다.
                GameObject prop = Instantiate(p.model, position, rotation);

                Material m = !player && p.enemyMaterial != null ? p.enemyMaterial : p.material;
                if (m != null)
                {
                    foreach (Renderer r in prop.GetComponentsInChildren<Renderer>())
                        r.sharedMaterial = m;
                }

                prop.transform.SetParent(bone, true);
            }
        }

        /// <summary>
        /// 도형으로 짓는 기본 병사. 몸통(진영 색) + 머리 + 종류별 소품 하나 — 멀리서 실루엣으로
        /// 종류가 구분되게 하는 것이 목적이다(창·방패 / 활 / 몽둥이 / 외눈).
        /// </summary>
        private void BuildPrimitiveSoldier(Transform body, BattleUnit u, bool player, float r, float height)
        {
            Material team = player ? _playerMaterial : _enemyMaterial;
            float torso = height * 0.72f;

            Part(body, PrimitiveType.Capsule, team, new Vector3(0f, torso * 0.5f, 0f), new Vector3(r * 2f, torso * 0.5f, r * 2f));
            Part(body, PrimitiveType.Sphere, _skinMaterial, new Vector3(0f, torso + r * 0.75f, 0f), Vector3.one * r * 1.5f);

            switch (u.Def.UnitId)
            {
                case "hoplite":
                    // 둥근 청동 방패(왼쪽 앞) + 긴 창(오른쪽, 앞으로 기울임).
                    Transform shield = Part(body, PrimitiveType.Cylinder, _bronzeMaterial,
                        new Vector3(-r * 0.6f, torso * 0.55f, r * 0.9f), new Vector3(r * 1.9f, 0.04f, r * 1.9f));
                    shield.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Transform spear = Part(body, PrimitiveType.Cube, _woodMaterial,
                        new Vector3(r * 0.9f, torso * 0.7f, r * 0.5f), new Vector3(0.06f, height * 1.1f, 0.06f));
                    spear.localRotation = Quaternion.Euler(25f, 0f, 0f);
                    break;

                case "archer":
                    Part(body, PrimitiveType.Cube, _woodMaterial,
                        new Vector3(r * 0.4f, torso * 0.6f, r * 1.0f), new Vector3(0.05f, height * 0.75f, 0.12f));
                    break;

                case "giant_brute":
                    Transform club = Part(body, PrimitiveType.Cube, _woodMaterial,
                        new Vector3(r * 1.1f, torso * 0.6f, r * 0.6f), new Vector3(r * 0.45f, height * 0.6f, r * 0.45f));
                    club.localRotation = Quaternion.Euler(35f, 0f, 0f);
                    break;

                case "giant_slinger":
                    Part(body, PrimitiveType.Sphere, _woodMaterial,
                        new Vector3(r * 1.0f, torso * 0.8f, r * 0.6f), Vector3.one * r * 0.5f);
                    break;

                case "cyclops":
                    // 외눈 — 얼굴 앞에 흰 공 하나.
                    Part(body, PrimitiveType.Sphere, _flashMaterial,
                        new Vector3(0f, torso + r * 0.8f, r * 0.65f), Vector3.one * r * 0.55f);
                    Transform big = Part(body, PrimitiveType.Cube, _woodMaterial,
                        new Vector3(r * 1.1f, torso * 0.6f, r * 0.5f), new Vector3(r * 0.4f, height * 0.7f, r * 0.4f));
                    big.localRotation = Quaternion.Euler(30f, 0f, 0f);
                    break;
            }
        }

        private static Transform Part(Transform parent, PrimitiveType type, Material material, Vector3 localPos, Vector3 localScale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());   // 탭 판정이 없다 — 충돌체는 비용만 든다.

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            return go.transform;
        }

        private BattleUnitModelEntry ModelFor(string unitId)
        {
            for (int i = 0; i < _unitModels.Count; i++)
            {
                BattleUnitModelEntry e = _unitModels[i];
                if (e != null && e.model != null && e.unitId == unitId)
                    return e;
            }

            return null;
        }

        // ---------- 화살 ----------

        private void LaunchArrow(Vector3 from, Vector3 to)
        {
            Arrow arrow = null;
            for (int i = 0; i < _arrows.Count; i++)
            {
                if (!_arrows[i].Transform.gameObject.activeSelf)
                {
                    arrow = _arrows[i];
                    break;
                }
            }

            if (arrow == null)
            {
                arrow = new Arrow { Transform = Part(transform, PrimitiveType.Cube, _woodMaterial, Vector3.zero, new Vector3(0.05f, 0.05f, 0.6f)) };
                _arrows.Add(arrow);
            }

            arrow.From = from + Vector3.up * 1.2f;
            arrow.To = to + Vector3.up * 1.0f;
            arrow.StartedAt = Time.time;
            arrow.Transform.gameObject.SetActive(true);
        }

        private void UpdateArrows()
        {
            float duration = Mathf.Max(0.05f, _arrowSeconds / Speed);

            for (int i = 0; i < _arrows.Count; i++)
            {
                Arrow a = _arrows[i];
                if (!a.Transform.gameObject.activeSelf)
                    continue;

                float k = (Time.time - a.StartedAt) / duration;
                if (k >= 1f)
                {
                    a.Transform.gameObject.SetActive(false);
                    continue;
                }

                // 포물선 — 높이를 사인으로 얹는다.
                Vector3 p = Vector3.Lerp(a.From, a.To, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.5f;
                Vector3 next = Vector3.Lerp(a.From, a.To, k + 0.05f) + Vector3.up * Mathf.Sin((k + 0.05f) * Mathf.PI) * 1.5f;

                a.Transform.position = p;
                if ((next - p).sqrMagnitude > 0.0001f)
                    a.Transform.rotation = Quaternion.LookRotation(next - p);
            }
        }
    }
}

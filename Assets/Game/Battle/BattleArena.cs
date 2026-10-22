using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Battle;

namespace Olympus.Game.Battle
{
    /// <summary>
    /// 전투 한 판을 <b>그리고 재생하는</b> 부분. 무엇과 싸우는지·결과를 어떻게 적용하는지는 모른다 —
    /// 그건 부르는 쪽(전선 씬의 <see cref="BattlePresenter"/>, 기지의 거인 사건)이 정한다.
    ///
    /// 떼어 낸 이유 — 1장 "300 vs 1"은 전선 씬이 아니라 <b>기지 화면 안</b>(성 밖 협곡)에서 벌어진다.
    /// 병사 모델·동작·화살 연출을 두 벌 만들면 한쪽만 고쳐진다(정면 맞춤이 그렇게 두 번 빗나갔다).
    ///
    /// 병사 좌표는 이 컴포넌트의 <b>로컬</b>이다 — 기지에서는 이 오브젝트를 협곡에 놓고 돌려서
    /// 아군(-Z)이 성 쪽, 적(+Z)이 바깥을 보게 한다.
    /// </summary>
    public sealed class BattleArena : MonoBehaviour
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

        [Header("성능")]
        [Tooltip("한 진영에서 동시에 그리는 병사 수. 계산은 전원(300)이 하고, 그림은 앞줄부터 이만큼만 —\n" +
                 "앞에서 쓰러지면 뒤에서 한 명씩 이어서 그린다. 300명을 다 그리면 무겁다(2026-10-22 형님 확인).")]
        [SerializeField, Range(10, 300)] private int _maxVisiblePerTeam = 50;

        [Header("연출")]
        [Tooltip("화살이 날아가는 시간(초). 결과와 무관한 볼거리다.")]
        [SerializeField] private float _arrowSeconds = 0.3f;

        private Camera _camera;
        private Transform _unitRoot;

        private readonly Dictionary<int, BattleUnitView> _views = new Dictionary<int, BattleUnitView>();
        private readonly List<Arrow> _arrows = new List<Arrow>();

        private float _accumulatedMs;

        /// <summary>아직 안 그린 병사 번호 — 앞줄(적에 가까운 쪽)부터. 그린 병사가 쓰러지면 여기서 꺼내 그린다.</summary>
        private readonly List<int> _waitingPlayer = new List<int>();
        private readonly List<int> _waitingEnemy = new List<int>();

        public BattleSimulation Simulation { get; private set; }

        /// <summary>재생 중인가. 끝나면 false가 되고 <see cref="Finished"/>가 한 번 울린다.</summary>
        public bool Playing { get; private set; }

        /// <summary>1 또는 2.</summary>
        public int Speed { get; set; } = 1;

        public event System.Action Finished;

        private sealed class Arrow
        {
            public Transform Transform;
            public Vector3 From;
            public Vector3 To;
            public float StartedAt;
        }

        private void Awake()
        {
            EnsureRoot();
        }

        private void EnsureRoot()
        {
            if (_unitRoot != null)
                return;

            _unitRoot = new GameObject("Units").transform;
            _unitRoot.SetParent(transform, false);
        }

        /// <summary>새 판을 세운다(아직 움직이지 않는다). 앞 판의 병사는 치운다.</summary>
        public void Show(BattleSimulation simulation)
        {
            Clear();
            Simulation = simulation;
            _camera = Camera.main;
            BuildViews();
        }

        public void Play()
        {
            if (Simulation == null || Simulation.IsFinished)
                return;

            _accumulatedMs = 0f;
            Playing = true;
        }

        /// <summary>끝까지 바로 돌린다. 쓰러짐 연출은 이어지도록 사건을 그대로 흘려보낸다.</summary>
        public void SkipToEnd()
        {
            if (!Playing)
                return;

            while (!Simulation.IsFinished)
                StepOnce();

            End();
        }

        public void Clear()
        {
            EnsureRoot();
            Playing = false;
            Simulation = null;

            foreach (BattleUnitView v in _views.Values)
            {
                if (v.Root != null)
                    Destroy(v.Root.gameObject);
            }

            _views.Clear();
            _waitingPlayer.Clear();
            _waitingEnemy.Clear();

            for (int i = 0; i < _arrows.Count; i++)
                _arrows[i].Transform.gameObject.SetActive(false);
        }

        private void End()
        {
            Playing = false;
            Finished?.Invoke();
        }

        private void Update()
        {
            if (Simulation == null)
                return;

            if (Playing)
            {
                _accumulatedMs += Time.deltaTime * 1000f * Speed;

                while (_accumulatedMs >= BattleSimulation.StepMs && !Simulation.IsFinished)
                {
                    _accumulatedMs -= BattleSimulation.StepMs;
                    StepOnce();
                }

                if (Simulation.IsFinished)
                    End();
            }

            if (_camera == null)
                _camera = Camera.main;

            float t = Playing ? Mathf.Clamp01(_accumulatedMs / BattleSimulation.StepMs) : 1f;

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

            Refill();
        }

        /// <summary>
        /// 싸우지 않는 구경용 병사 하나(기지 밖에서 걸어오는 거인 등). 전투 병사와 같은 모델·정면 맞춤을 쓴다.
        /// 모델이 없으면 도형 병사로 세운다(animator는 null).
        /// </summary>
        public GameObject SpawnDisplay(string unitId, bool player, float radius, Transform parent, out Animator animator)
        {
            animator = null;
            BattleUnitModelEntry entry = ModelFor(unitId);

            var root = new GameObject("Display_" + unitId).transform;
            root.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root, false);

            // 모델이 없으면 도형 병사로 — 전투 화면과 같다. 없다고 안 그리면 "도적이 안 보인다"가 된다(2026-10-22).
            if (entry == null)
            {
                BuildPrimitiveSoldier(body, unitId, player, radius, radius * 4.5f);
                return root.gameObject;
            }

            float height = radius * 4.5f * (entry.scale > 0.01f ? entry.scale : 1f);
            animator = BuildModelSoldier(body, entry, height, player);
            return root.gameObject;
        }

        // ---------- 병사 모습 ----------

        private void BuildViews()
        {
            var order = new List<BattleUnit>(Simulation.Units);

            // 앞줄부터 — 가운데선(Z=0)에 가까울수록 앞이다. 같으면 번호 순(매번 같은 순서).
            order.Sort((a, b) =>
            {
                int c = System.Math.Abs(a.Z).CompareTo(System.Math.Abs(b.Z));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });

            for (int i = 0; i < order.Count; i++)
                (order[i].Team == BattleTeam.Player ? _waitingPlayer : _waitingEnemy).Add(order[i].Id);

            Refill();
        }

        /// <summary>살아서 그려지는 병사가 한도보다 적으면 기다리는 병사를 앞에서부터 그린다.</summary>
        private void Refill()
        {
            Refill(BattleTeam.Player, _waitingPlayer);
            Refill(BattleTeam.Enemy, _waitingEnemy);
        }

        private void Refill(BattleTeam team, List<int> waiting)
        {
            if (waiting.Count == 0)
                return;

            int shown = 0;
            foreach (KeyValuePair<int, BattleUnitView> pair in _views)
            {
                BattleUnit u;
                if (Simulation.TryGetUnit(pair.Key, out u) && u.Team == team && u.IsAlive)
                    shown++;
            }

            while (shown < _maxVisiblePerTeam && waiting.Count > 0)
            {
                int id = waiting[0];
                waiting.RemoveAt(0);

                BattleUnit u;
                if (!Simulation.TryGetUnit(id, out u) || !u.IsAlive)
                    continue;

                _views[id] = CreateView(u);
                shown++;
            }
        }

        private BattleUnitView CreateView(BattleUnit u)
        {
            bool player = u.Team == BattleTeam.Player;
            float r = u.Radius;
            float height = r * 4.5f;

            var root = new GameObject((player ? "P" : "E") + u.Id + "_" + u.Def.UnitId).transform;
            root.SetParent(_unitRoot, false);
            root.localRotation = Quaternion.LookRotation(new Vector3(u.FacingX, 0f, u.FacingZ));

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
                BuildPrimitiveSoldier(body, u.Def.UnitId, player, r, height);
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

            // 동작이 붙은 뒤에 한 번 더 잰다. Humanoid는 동작을 틀 때 몸의 정면을 Unity가 뼈대에서 다시 계산해
            // 돌려 세우는데, 거인처럼 구부정하고 팔이 앞으로 나온 몸은 그 계산이 반대로 나와 기본 자세(T포즈)에서
            // 맞춰 둔 정면이 첫 프레임에 뒤집혔다(2026-10-22 — 아트 쪽이 파일은 호플리테스와 같다고 확인).
            if (animator.runtimeAnimatorController != null && animator.isHuman)
            {
                animator.Update(0f);
                FaceForward(model.transform, animator, body);
            }

            animator.applyRootMotion = false;

            // 화면 밖이면 뼈 계산을 쉰다 — 협곡 뒤쪽은 카메라에 안 잡힐 때가 많다.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
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

            // 1순위는 발끝(발목 → 발가락) — 몸 생김새 그대로라 좌우 이름표가 뒤바뀌어도 틀리지 않는다.
            // 어깨(오른쪽 − 왼쪽 × 위)만 봤더니 거인이 뒤돌아 섰다(2026-10-22): 내보내기에서 좌우가 뒤집힌 파일은
            // 어깨로 재면 정확히 반대가 나온다. 발끝이 없을 때만 어깨로 잰다.
            Vector3 toes = Toes(animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes)
                         + Toes(animator, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            Vector3 shoulders = Shoulders(animator);

            Vector3 facing = toes.sqrMagnitude > 1e-6f ? toes : shoulders;
            if (facing.sqrMagnitude < 1e-6f)
                return;

            if (toes.sqrMagnitude > 1e-6f && shoulders.sqrMagnitude > 1e-6f && Vector3.Dot(toes, shoulders) < 0f)
                Debug.LogWarning(model.name + ": 발끝과 어깨가 가리키는 정면이 반대입니다 — 좌우 뼈 이름이 뒤바뀐 파일입니다. " +
                                 "발끝 기준으로 세웠지만, 왼손·오른손 소품과 동작이 거울처럼 보일 수 있습니다(아트 쪽 확인 필요).");

            Vector3 want = frame.forward;
            want.y = 0f;

            float angle = Vector3.SignedAngle(facing, want, Vector3.up);
            model.rotation = Quaternion.AngleAxis(angle, Vector3.up) * model.rotation;
        }

        private static Vector3 Toes(Animator animator, HumanBodyBones foot, HumanBodyBones toes)
        {
            Transform f = animator.GetBoneTransform(foot);
            Transform t = animator.GetBoneTransform(toes);
            if (f == null || t == null)
                return Vector3.zero;

            Vector3 d = t.position - f.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero;
        }

        private static Vector3 Shoulders(Animator animator)
        {
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (left == null || right == null)
                return Vector3.zero;

            Vector3 across = right.position - left.position;
            across.y = 0f;
            return across.sqrMagnitude > 1e-8f ? Vector3.Cross(across, Vector3.up).normalized : Vector3.zero;
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
        private void BuildPrimitiveSoldier(Transform body, string unitId, bool player, float r, float height)
        {
            Material team = player ? _playerMaterial : _enemyMaterial;
            float torso = height * 0.72f;

            Part(body, PrimitiveType.Capsule, team, new Vector3(0f, torso * 0.5f, 0f), new Vector3(r * 2f, torso * 0.5f, r * 2f));
            Part(body, PrimitiveType.Sphere, _skinMaterial, new Vector3(0f, torso + r * 0.75f, 0f), Vector3.one * r * 1.5f);

            switch (unitId)
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

                case "bandit":
                    // 도적 — 짙은 두건 + 짧은 칼. 전용 모델이 오기 전까지 호플리테스(창·방패)와 실루엣으로 갈린다.
                    Part(body, PrimitiveType.Sphere, _woodMaterial,
                        new Vector3(0f, torso + r * 0.85f, -r * 0.1f), Vector3.one * r * 1.7f);
                    Transform knife = Part(body, PrimitiveType.Cube, _bronzeMaterial,
                        new Vector3(r * 0.9f, torso * 0.55f, r * 0.7f), new Vector3(0.05f, height * 0.3f, 0.05f));
                    knife.localRotation = Quaternion.Euler(60f, 0f, 0f);
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

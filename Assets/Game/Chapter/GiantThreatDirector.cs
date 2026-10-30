using UnityEngine;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Game.Battle;
using Olympus.Game.Territory;

namespace Olympus.Game.Chapter
{
    /// <summary>
    /// 1장 "300 용사"를 <b>기지 화면 안에서</b> 보여 준다 — 성 밖에서 걸어오는 거인, 협곡, 300 vs 1.
    /// 탑포스처럼 전선 씬으로 넘어가지 않는다(형님 결정, 2026-10-21). 판단은 전부 Core
    /// (<see cref="GiantThreatService"/>)에 있고, 여기는 그 상태를 보고 그리기만 한다.
    ///
    /// 자리 잡기: 기지 가운데에서 <see cref="_approachDirection"/> 쪽으로
    /// 성 모서리 → 협곡(전투 자리) → 거인 출발점이 한 줄로 선다. 기본은 화면 위쪽(카메라 yaw 45°의 정면).
    /// </summary>
    public sealed class GiantThreatDirector : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private BattleArena _arena;

        [Header("자리")]
        [Tooltip("거인이 오는 방향(기지 가운데 기준, XZ). 화면 위쪽이 (1, 1)이다.")]
        [SerializeField] private Vector2 _approachDirection = new Vector2(1f, 1f);

        [Tooltip("진형 맨 뒷줄과 성 모서리 사이 여유(m). 협곡 자리는 진형 깊이(줄 수 × 간격)에서 저절로 정해진다 —\n" +
                 "숫자로 박아 뒀더니 마을에서 너무 멀었다(2026-10-22, 44m).")]
        [SerializeField] private float _canyonGap = 1f;

        [Tooltip("협곡 가운데에서 거인이 처음 보이는 곳까지(m).")]
        [SerializeField] private float _approachLength = 32f;

        [Tooltip("진형 폭 바깥으로 절벽까지 남기는 여유(m, 한쪽). 좁아야 '소수로 막는 천연 요새'로 읽힌다.")]
        [SerializeField] private float _canyonSideMargin = 3f;

        private float _canyonDistance;
        private float _canyonWidth;

        [Tooltip("거인이 도착한 뒤 저절로 싸움이 시작되기까지(초). 경고를 읽을 시간.")]
        [SerializeField] private float _arrivalWarningSeconds = 3f;

        [Header("재질 (비우면 회색)")]
        [SerializeField] private Material _canyonFloorMaterial;
        [SerializeField] private Material _canyonWallMaterial;

        private Vector3 _dir;
        private Vector3 _canyonCenter;
        private Vector3 _farPoint;
        private Vector3 _arrivePoint;

        private GameObject _giant;
        private Animator _giantAnimator;
        private string _giantUnitId;
        private float _arrivedAt = -1f;

        public bool Fighting { get; private set; }
        public ThreatOutcome LastOutcome { get; private set; }

        /// <summary>싸움이 시작될 때·끝날 때. HUD가 듣는다.</summary>
        public event System.Action BattleStarted;
        public event System.Action<ThreatDef, ThreatOutcome> BattleEnded;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                Debug.LogWarning("GiantThreatDirector: TerritoryPresenter가 없습니다.");
                enabled = false;
                return;
            }

            LayOut();
            BuildCanyon();

            if (_arena != null)
            {
                _arena.transform.position = _canyonCenter;
                _arena.transform.rotation = Quaternion.LookRotation(_dir);
                _arena.Finished += OnArenaFinished;
            }
        }

        private void OnDestroy()
        {
            if (_arena != null)
                _arena.Finished -= OnArenaFinished;
        }

        private void LayOut()
        {
            TerritoryGround ground = _presenter.Ground;
            GridRect b = ground.Bounds;
            float cell = ground.Grid.CellSize;

            float minX = (b.MinX - 0.5f) * cell, maxX = (b.MaxXExclusive - 0.5f) * cell;
            float minZ = (b.MinY - 0.5f) * cell, maxZ = (b.MaxYExclusive - 0.5f) * cell;
            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            _dir = new Vector3(_approachDirection.x, 0f, _approachDirection.y);
            if (_dir.sqrMagnitude < 0.0001f) _dir = Vector3.forward;
            _dir.Normalize();

            // 기지(정사각형) 가장자리까지 — 방향에 따라 변·모서리 어느 쪽에 닿는지 다르다.
            float halfX = (maxX - minX) * 0.5f, halfZ = (maxZ - minZ) * 0.5f;
            float toEdge = Mathf.Min(
                Mathf.Abs(_dir.x) > 0.001f ? halfX / Mathf.Abs(_dir.x) : float.MaxValue,
                Mathf.Abs(_dir.z) > 0.001f ? halfZ / Mathf.Abs(_dir.z) : float.MaxValue);

            // 진형 깊이: 목표 수를 한 줄 수로 나눈 줄 수 × 간격 + 처음 서는 거리. 300명·24열·1m면 약 17m.
            float depth = 10f, width = 20f;
            ThreatDef t = _presenter.Runtime.Threats.Current;
            if (t != null)
            {
                int perRow = t.RowWidth > 0 ? t.RowWidth : 6;
                float spacing = t.Spacing > 0f ? t.Spacing : 1.3f;
                float start = t.StartDistance > 0f ? t.StartDistance : 7f;
                depth = Mathf.Ceil(t.GoalTroops / (float)perRow) * spacing + start;
                width = perRow * spacing;
            }

            _canyonDistance = depth + _canyonGap;
            _canyonWidth = width + _canyonSideMargin * 2f;

            Vector3 edge = center + _dir * toEdge;
            _canyonCenter = edge + _dir * _canyonDistance;
            _arrivePoint = _canyonCenter + _dir * 8f;
            _farPoint = _canyonCenter + _dir * _approachLength;

            TerritoryCamera cam = _presenter.CameraRig;
            if (cam != null)
            {
                cam.ExtendBounds(_canyonCenter);
                cam.ExtendBounds(_arrivePoint);
            }
        }

        /// <summary>
        /// 협곡 그레이박스 — 바닥 띠 + 양쪽 절벽. 아트가 오기 전까지 "좁은 길목"이 읽히게만 한다.
        /// 성 모서리에서 거인 출발점까지 이어 놓아 거인이 허공을 걷지 않게 한다.
        /// </summary>
        private void BuildCanyon()
        {
            var root = new GameObject("Canyon").transform;
            root.SetParent(transform, false);

            float length = _canyonDistance + _approachLength + 10f;
            Vector3 mid = _canyonCenter + _dir * ((_approachLength - _canyonDistance) * 0.5f);
            Quaternion rot = Quaternion.LookRotation(_dir);

            Transform floor = Box(root, "Floor", _canyonFloorMaterial, new Color(0.55f, 0.48f, 0.36f));
            floor.SetPositionAndRotation(mid + Vector3.down * 0.06f, rot);
            floor.localScale = new Vector3(_canyonWidth, 0.1f, length);

            Vector3 right = Vector3.Cross(Vector3.up, _dir);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform wall = Box(root, side < 0 ? "WallLeft" : "WallRight", _canyonWallMaterial, new Color(0.40f, 0.36f, 0.33f));
                wall.SetPositionAndRotation(mid + right * side * (_canyonWidth * 0.5f + 3f) + Vector3.up * 4f, rot);
                wall.localScale = new Vector3(6f, 8f, length);
            }
        }

        private static Transform Box(Transform parent, string name, Material material, Color fallback)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);

            Renderer r = go.GetComponent<Renderer>();
            if (material != null)
                r.sharedMaterial = material;
            else
                r.material.color = fallback;

            return go.transform;
        }

        private void Update()
        {
            GiantThreatService threats = _presenter.Runtime.Threats;
            ThreatDef t = threats.Current;

            if (Fighting)
                return;

            if (t == null)
            {
                SetGiantVisible(false);
                return;
            }

            ThreatView v = threats.Describe(t);
            switch (v.Phase)
            {
                case ThreatPhase.Hidden:
                case ThreatPhase.Defeated:
                    SetGiantVisible(false);
                    _arrivedAt = -1f;
                    break;

                case ThreatPhase.Approaching:
                    EnsureGiant(t);
                    SetGiantVisible(true);
                    PlaceGiant(Vector3.Lerp(_farPoint, _arrivePoint, v.Progress), true);
                    _arrivedAt = -1f;
                    break;

                case ThreatPhase.Arrived:
                    EnsureGiant(t);
                    SetGiantVisible(true);
                    PlaceGiant(_arrivePoint, false);

                    // 도착하면 가진 병사로 맞선다 — 잠깐 경고를 보여 주고 시작한다.
                    if (_arrivedAt < 0f)
                        _arrivedAt = Time.time;
                    else if (Time.time - _arrivedAt >= _arrivalWarningSeconds)
                        Dispatch();
                    break;
            }
        }

        /// <summary>도착까지 남은 경고 시간(초). 도착하지 않았으면 -1.</summary>
        public float ArrivalCountdown => _arrivedAt < 0f ? -1f : Mathf.Max(0f, _arrivalWarningSeconds - (Time.time - _arrivedAt));

        private void EnsureGiant(ThreatDef t)
        {
            string unitId = t.Enemies[0].UnitId;
            if (_giant != null && _giantUnitId == unitId)
                return;

            if (_giant != null)
                Destroy(_giant);

            _giantUnitId = unitId;
            float radius = 1.6f;
            UnitDef def;
            if (_presenter.Runtime.Battles.Config.TryGetUnit(unitId, out def))
                radius = def.Radius * (t.EnemyScale > 0f ? t.EnemyScale : 1f);

            if (_arena != null)
                _giant = _arena.SpawnDisplay(unitId, false, radius, transform, out _giantAnimator);

            // 모델이 없으면 큰 회색 기둥이라도 — 무엇이 오는지는 보여야 한다.
            if (_giant == null)
            {
                _giant = new GameObject("Giant");
                _giant.transform.SetParent(transform, false);
                Transform body = Box(_giant.transform, "Body", null, new Color(0.5f, 0.5f, 0.55f));
                body.localScale = new Vector3(radius * 2f, radius * 4.5f, radius * 1.4f);
                body.localPosition = new Vector3(0f, radius * 2.25f, 0f);
            }
        }

        private void SetGiantVisible(bool visible)
        {
            if (_giant != null && _giant.activeSelf != visible)
                _giant.SetActive(visible);
        }

        private void PlaceGiant(Vector3 position, bool walking)
        {
            _giant.transform.position = position;
            _giant.transform.rotation = Quaternion.LookRotation(-_dir);

            if (_giantAnimator != null && _giantAnimator.runtimeAnimatorController != null)
                _giantAnimator.SetFloat(SpeedParam, walking ? 1f : 0f);
        }

        /// <summary>카메라를 거인 쪽으로. 임무 바("거인을 무찌르자")와 상단 띠를 누르면 온다.</summary>
        public void FocusGiant()
        {
            TerritoryCamera cam = _presenter.CameraRig;
            if (cam == null)
                return;

            Vector3 target = _giant != null && _giant.activeSelf ? _giant.transform.position : _arrivePoint;
            cam.ExtendBounds(target);
            cam.FocusOn(Vector3.Lerp(_canyonCenter, target, 0.5f));
        }

        /// <summary>출정 — 300을 채웠거나 거인이 도착했을 때. 협곡에 진형을 세우고 재생한다.</summary>
        public bool Dispatch()
        {
            if (Fighting || _arena == null)
                return false;

            GiantThreatService threats = _presenter.Runtime.Threats;
            ThreatDef t = threats.Current;
            if (t == null || threats.CanDispatch(t.ThreatId) != ThreatRejection.None)
                return false;

            Fighting = true;
            SetGiantVisible(false);

            _arena.Show(threats.CreateSimulation(t.ThreatId));
            _arena.Play();

            TerritoryCamera cam = _presenter.CameraRig;
            if (cam != null)
            {
                cam.FocusOn(_canyonCenter - _dir * 8f);
                cam.SetDistance(45f);
            }

            BattleStarted?.Invoke();
            return true;
        }

        public void ToggleSpeed()
        {
            if (_arena != null)
                _arena.Speed = _arena.Speed == 1 ? 2 : 1;
        }

        public int Speed => _arena != null ? _arena.Speed : 1;

        public void Skip()
        {
            if (Fighting && _arena != null)
                _arena.SkipToEnd();
        }

        public BattleSimulation Simulation => _arena != null ? _arena.Simulation : null;

        private void OnArenaFinished()
        {
            if (!Fighting)
                return;

            GiantThreatService threats = _presenter.Runtime.Threats;
            ThreatDef t = threats.Current;
            bool shownWon = _arena.Simulation.PlayerWon;

            LastOutcome = threats.Resolve(t.ThreatId);
            if (LastOutcome.Won != shownWon)
                Debug.LogError("화면의 전투 결과와 적용된 결과가 다릅니다. 시뮬레이션이 결정론을 잃었습니다.");

            if (TerritorySession.SaveEnabled)
                SaveStore.Save(_presenter.Runtime.CaptureSave());

            Fighting = false;
            _arrivedAt = -1f;
            BattleEnded?.Invoke(t, LastOutcome);

            // 쓰러진 병사가 넘어지는 걸 보고 나서 치운다.
            Invoke(nameof(ClearArena), 4f);
        }

        private void ClearArena()
        {
            if (!Fighting && _arena != null)
                _arena.Clear();
        }
    }
}

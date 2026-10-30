using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Battle;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Game.Battle;
using Olympus.Game.Territory;

namespace Olympus.Game.Chapter
{
    /// <summary>
    /// 도적 습격을 기지 화면에 보여 준다 — 성 밖에서 도적단이 걸어오고, 도착하면 마을 수비대가 성 가장자리에서 맞선다.
    /// 탑포스처럼 기지 둘레에서 늘 무언가 싸우는 느낌을 내는 것이 목적이다. 판단은 Core(<see cref="RaidService"/>).
    ///
    /// 카메라를 끌고 가지 않는다 — 작은 싸움마다 화면을 빼앗으면 건설을 방해한다. 알림만 띄우고, 누르면 간다.
    /// </summary>
    public sealed class RaidDirector : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private BattleArena _arena;
        [SerializeField] private GiantThreatDirector _giant;

        [Tooltip("성 가장자리에서 싸움터 가운데까지(m).")]
        [SerializeField] private float _fieldDistance = 7f;

        [Tooltip("도적단이 처음 보이는 곳(싸움터에서 바깥으로, m).")]
        [SerializeField] private float _approachLength = 25f;

        [SerializeField] private Material _fieldMaterial;

        /// <summary>습격 순서대로 도는 방향(기지 가운데 기준 XZ). 거인이 오는 위쪽(1,1)은 비워 둔다.</summary>
        private static readonly Vector2[] Directions =
        {
            new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f), new Vector2(-1f, 0.3f), new Vector2(0.3f, -1f),
        };

        private Vector3 _center;
        private float _halfX, _halfZ;
        private readonly Dictionary<int, Transform> _fields = new Dictionary<int, Transform>();
        private readonly List<GameObject> _approaching = new List<GameObject>();
        private int _approachingNumber = -1;

        public bool Fighting { get; private set; }

        /// <summary>도적단이 보이기 시작했다 / 싸움이 끝났다. HUD가 알림을 띄운다.</summary>
        public event System.Action<RaidView> RaidSpotted;
        public event System.Action<RaidOutcome> RaidEnded;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null)
            {
                enabled = false;
                return;
            }

            TerritoryGround ground = _presenter.Ground;
            GridRect b = ground.Bounds;
            float cell = ground.Grid.CellSize;
            float minX = (b.MinX - 0.5f) * cell, maxX = (b.MaxXExclusive - 0.5f) * cell;
            float minZ = (b.MinY - 0.5f) * cell, maxZ = (b.MaxYExclusive - 0.5f) * cell;
            _center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            _halfX = (maxX - minX) * 0.5f;
            _halfZ = (maxZ - minZ) * 0.5f;

            if (_arena != null)
                _arena.Finished += OnArenaFinished;

            // 앱이 꺼져 있던 사이 Core가 결판낸 습격도 결과는 알린다.
            _presenter.Runtime.Raids.AutoResolved += OnAutoResolved;
        }

        private void OnDestroy()
        {
            if (_arena != null)
                _arena.Finished -= OnArenaFinished;
            if (_presenter != null && _presenter.Runtime != null)
                _presenter.Runtime.Raids.AutoResolved -= OnAutoResolved;
        }

        private Vector3 Dir(int number)
        {
            Vector2 d = Directions[((number % Directions.Length) + Directions.Length) % Directions.Length];
            return new Vector3(d.x, 0f, d.y).normalized;
        }

        private Vector3 FieldCenter(int number)
        {
            Vector3 dir = Dir(number);
            float toEdge = Mathf.Min(
                Mathf.Abs(dir.x) > 0.001f ? _halfX / Mathf.Abs(dir.x) : float.MaxValue,
                Mathf.Abs(dir.z) > 0.001f ? _halfZ / Mathf.Abs(dir.z) : float.MaxValue);
            return _center + dir * (toEdge + _fieldDistance);
        }

        private void Update()
        {
            if (Fighting)
                return;

            RaidService raids = _presenter.Runtime.Raids;
            RaidView v = raids.Describe();

            switch (v.Phase)
            {
                case RaidPhase.Approaching:
                    ShowApproach(v);
                    break;

                case RaidPhase.Arrived:
                    // 협곡에서 거인과 싸우는 중이면 기다린다 — 한 화면에 싸움 둘은 읽히지 않는다.
                    if (_giant == null || !_giant.Fighting)
                        StartFight(v);
                    break;

                default:
                    ClearApproach();
                    break;
            }
        }

        private void ShowApproach(RaidView v)
        {
            if (_approachingNumber != v.Number)
            {
                ClearApproach();
                _approachingNumber = v.Number;
                EnsureField(v.Number);
                SpawnApproach(v);
                RaidSpotted?.Invoke(v);
            }

            Vector3 dir = Dir(v.Number);
            Vector3 field = FieldCenter(v.Number);
            Vector3 from = field + dir * _approachLength;
            Vector3 to = field + dir * 4f;
            Vector3 right = Vector3.Cross(Vector3.up, dir);

            for (int i = 0; i < _approaching.Count; i++)
            {
                float side = (i - (_approaching.Count - 1) * 0.5f) * 1.2f;
                float back = (i % 2) * 0.8f;
                Transform t = _approaching[i].transform;
                t.position = Vector3.Lerp(from, to, v.Progress) + right * side + dir * back;
                t.rotation = Quaternion.LookRotation(-dir);
            }
        }

        private void SpawnApproach(RaidView v)
        {
            if (_arena == null || v.Wave == null)
                return;

            for (int g = 0; g < v.Wave.Enemies.Count; g++)
            {
                EnemyGroup group = v.Wave.Enemies[g];
                UnitDef def;
                float radius = _presenter.Runtime.Battles.Config.TryGetUnit(group.UnitId, out def) ? def.Radius : 0.4f;

                for (int k = 0; k < group.Count; k++)
                {
                    Animator animator;
                    GameObject go = _arena.SpawnDisplay(group.UnitId, false, radius, transform, out animator, _approaching.Count);
                    if (go == null)
                        continue;

                    if (animator != null && animator.runtimeAnimatorController != null)
                        animator.SetFloat(SpeedParam, 1f);
                    _approaching.Add(go);
                }
            }
        }

        private void ClearApproach()
        {
            for (int i = 0; i < _approaching.Count; i++)
                Destroy(_approaching[i]);
            _approaching.Clear();
            _approachingNumber = -1;
        }

        /// <summary>싸움터 바닥 — 성 밖은 땅이 없어서 허공에서 싸우게 된다. 방향마다 한 번만 깐다.</summary>
        private void EnsureField(int number)
        {
            int slot = ((number % Directions.Length) + Directions.Length) % Directions.Length;
            if (_fields.ContainsKey(slot))
                return;

            Vector3 dir = Dir(number);
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "RaidField_" + slot;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(
                FieldCenter(number) + dir * (_approachLength * 0.5f - 4f) + Vector3.down * 0.06f,
                Quaternion.LookRotation(dir));
            go.transform.localScale = new Vector3(14f, 0.1f, _approachLength + 14f);

            Renderer r = go.GetComponent<Renderer>();
            if (_fieldMaterial != null)
                r.sharedMaterial = _fieldMaterial;
            else
                r.material.color = new Color(0.55f, 0.48f, 0.36f);

            _fields[slot] = go.transform;
        }

        private void StartFight(RaidView v)
        {
            if (_arena == null)
                return;

            RaidService raids = _presenter.Runtime.Raids;
            if (!raids.Engage())
                return;

            ClearApproach();
            EnsureField(v.Number);

            Vector3 dir = Dir(v.Number);
            _arena.transform.position = FieldCenter(v.Number);
            _arena.transform.rotation = Quaternion.LookRotation(dir);

            TerritoryCamera cam = _presenter.CameraRig;
            if (cam != null)
                cam.ExtendBounds(FieldCenter(v.Number));

            _arena.Show(raids.CreateSimulation());
            _arena.Play();
            Fighting = true;
        }

        /// <summary>알림을 누르면 싸움터로.</summary>
        public void FocusRaid()
        {
            TerritoryCamera cam = _presenter.CameraRig;
            if (cam == null)
                return;

            int number = Fighting || _approachingNumber < 0 ? _presenter.Runtime.Raids.Describe().Number : _approachingNumber;
            Vector3 field = FieldCenter(number);
            cam.ExtendBounds(field);
            cam.FocusOn(field);
        }

        private void OnArenaFinished()
        {
            if (!Fighting)
                return;

            bool shownWon = _arena.Simulation.PlayerWon;
            RaidOutcome o = _presenter.Runtime.Raids.Resolve();
            if (o.Resolved && o.Won != shownWon)
                Debug.LogError("도적 습격: 화면의 결과와 적용된 결과가 다릅니다. 시뮬레이션이 결정론을 잃었습니다.");

            if (TerritorySession.SaveEnabled)
                SaveStore.Save(_presenter.Runtime.CaptureSave());

            Fighting = false;
            RaidEnded?.Invoke(o);
            Invoke(nameof(ClearArena), 3f);
        }

        private void OnAutoResolved(RaidOutcome o)
        {
            ClearApproach();
            RaidEnded?.Invoke(o);
        }

        private void ClearArena()
        {
            if (!Fighting && _arena != null)
                _arena.Clear();
        }
    }
}

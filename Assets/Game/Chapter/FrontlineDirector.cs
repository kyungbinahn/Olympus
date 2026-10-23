using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Chapter;
using Olympus.Core.Grid;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game.Battle;
using Olympus.Game.Territory;

namespace Olympus.Game.Chapter
{
    /// <summary>
    /// 성문 밖 상시 전선을 그린다 — 뒷문으로 들어온 장정이 병영을 거쳐 성문 밖으로 나가고, 가운데서 적과 끊임없이 맞붙고,
    /// 전선 끝에는 체력바 달린 적 기지가 서 있다(탑포스 첫 화면, 형님 관찰 2026-10-23).
    ///
    /// 판단은 전부 Core(<see cref="FrontlineService"/>) — 기지 체력·무너짐·전리품. 여기 싸움은 <b>볼거리</b>다:
    /// 누가 언제 쓰러지는지는 화력(속도)에 맞춰 박자만 맞춘다. 결과와 무관하니 결정론일 필요가 없다.
    /// 적은 성 안으로 들어오지 않는다. 밀릴 때는 맞붙는 자리가 성문 쪽으로 당겨진다.
    /// </summary>
    public sealed class FrontlineDirector : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private BattleArena _arena;
        [SerializeField] private GiantThreatDirector _giant;

        [Header("자리")]
        [Tooltip("전선 방향(기지 가운데 기준 XZ). 거인이 오는 협곡 쪽과 같다 — 전선은 한 방향.")]
        [SerializeField] private Vector2 _direction = new Vector2(1f, 1f);

        [Tooltip("성문(기지 모서리)에서 적 기지까지(m).")]
        [SerializeField] private float _outpostDistance = 52f;

        [Tooltip("다음 기지는 이만큼 더 멀리 선다(m).")]
        [SerializeField] private float _outpostStep = 6f;

        [Header("인원")]
        [SerializeField] private int _maxOurs = 8;
        [SerializeField] private int _maxTheirs = 6;
        [SerializeField] private float _walkSpeed = 3.2f;

        [SerializeField] private Material _outpostWoodMaterial;
        [SerializeField] private Material _outpostStoneMaterial;
        [SerializeField] private Material _hpBackMaterial;
        [SerializeField] private Material _hpFillMaterial;

        private Vector3 _center, _dir, _right, _gate, _backGate;
        private Transform _root;

        private Transform _outpost;
        private Transform _hpFill;
        private string _outpostId;
        private float _outpostSinkStart = -1f;
        private Transform _sinking;

        private readonly List<Walker> _walkers = new List<Walker>();
        private readonly List<Fighter> _ours = new List<Fighter>();
        private readonly List<Fighter> _theirs = new List<Fighter>();

        private float _nextRecruit;
        private float _nextOurDeath;
        private float _nextTheirDeath;
        private float _nextTheirSpawn;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int DieParam = Animator.StringToHash("Die");
        private static readonly int VariantParam = Animator.StringToHash("Variant");

        /// <summary>뒷문 → 병영 → 성문 → 전선을 걷는 장정.</summary>
        private sealed class Walker
        {
            public GameObject Go;
            public Animator Animator;
            public readonly List<Vector3> Path = new List<Vector3>();
            public int Next;
        }

        /// <summary>맞붙는 자리에 선 병사.</summary>
        private sealed class Fighter
        {
            public GameObject Go;
            public Animator Animator;
            public Vector3 Slot;
            public float NextSwing;
            public float DiedAt = -1f;
            public bool Arrived;
        }

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null || _presenter.Runtime.Frontline.Def == null || _arena == null)
            {
                enabled = false;
                return;
            }

            _root = new GameObject("Frontline").transform;
            _root.SetParent(transform, false);

            TerritoryGround ground = _presenter.Ground;
            GridRect b = ground.Bounds;
            float cell = ground.Grid.CellSize;
            float minX = (b.MinX - 0.5f) * cell, maxX = (b.MaxXExclusive - 0.5f) * cell;
            float minZ = (b.MinY - 0.5f) * cell, maxZ = (b.MaxYExclusive - 0.5f) * cell;
            _center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            _dir = new Vector3(_direction.x, 0f, _direction.y).normalized;
            _right = Vector3.Cross(Vector3.up, _dir);
            float halfX = (maxX - minX) * 0.5f, halfZ = (maxZ - minZ) * 0.5f;
            float toEdge = Mathf.Min(
                Mathf.Abs(_dir.x) > 0.001f ? halfX / Mathf.Abs(_dir.x) : float.MaxValue,
                Mathf.Abs(_dir.z) > 0.001f ? halfZ / Mathf.Abs(_dir.z) : float.MaxValue);
            _gate = _center + _dir * toEdge;
            _backGate = _center - _dir * toEdge;

            _presenter.Runtime.Frontline.OutpostDestroyed += OnOutpostDestroyed;

            TerritoryCamera cam = _presenter.CameraRig;
            if (cam != null)
                cam.ExtendBounds(OutpostPosition(0));
        }

        private void OnDestroy()
        {
            if (_presenter != null && _presenter.Runtime != null)
                _presenter.Runtime.Frontline.OutpostDestroyed -= OnOutpostDestroyed;
        }

        private Vector3 OutpostPosition(int index) => _gate + _dir * (_outpostDistance + index * _outpostStep);

        private void Update()
        {
            FrontView v = _presenter.Runtime.Frontline.Describe();
            bool hidden = _giant != null && _giant.Fighting;   // 협곡 300 vs 1 동안은 그 싸움 하나만 보인다.
            if (_root.gameObject.activeSelf == hidden)
                _root.gameObject.SetActive(!hidden);
            if (hidden)
                return;

            UpdateOutpost(v);
            UpdateSinking();

            if (!v.HasOutpost)
                return;

            UpdateRecruits(v);
            UpdateWalkers();
            UpdateFight(v);
        }

        // ---------- 적 기지 ----------

        private void UpdateOutpost(FrontView v)
        {
            string id = v.HasOutpost ? v.Outpost.OutpostId : null;
            if (id != _outpostId)
            {
                if (_outpost != null && _sinking == null)
                {
                    _sinking = _outpost;
                    _outpostSinkStart = Time.time;
                }
                else if (_outpost != null)
                {
                    Destroy(_outpost.gameObject);
                }

                _outpost = null;
                _outpostId = id;
                if (v.HasOutpost)
                    BuildOutpost(v);
            }

            if (_hpFill != null)
            {
                float left = 1f - v.Fraction;
                _hpFill.localScale = new Vector3(Mathf.Max(0.001f, left) * 6f, 0.35f, 0.05f);
                _hpFill.localPosition = new Vector3(-(1f - left) * 3f, 0f, -0.03f);
            }

            if (_outpost != null)
            {
                Camera cam = Camera.main;
                Transform bar = _hpFill != null ? _hpFill.parent : null;
                if (cam != null && bar != null)
                    bar.rotation = cam.transform.rotation;
            }
        }

        /// <summary>그레이박스 적 기지 — 울타리 고리 + 망루. 도적은 나무, 거인은 돌로 크게. 아트가 오면 모델로 바꾼다.</summary>
        private void BuildOutpost(FrontView v)
        {
            bool giant = v.Outpost.EnemyUnitId != null && !v.Outpost.EnemyUnitId.StartsWith("bandit");
            float size = giant ? 7f : 4.5f;
            Material wall = giant ? _outpostStoneMaterial : _outpostWoodMaterial;
            Color fallback = giant ? new Color(0.45f, 0.42f, 0.40f) : new Color(0.45f, 0.32f, 0.20f);

            _outpost = new GameObject("Outpost_" + v.Outpost.OutpostId).transform;
            _outpost.SetParent(_root, false);
            _outpost.position = OutpostPosition(v.Index);
            _outpost.rotation = Quaternion.LookRotation(-_dir);

            const int posts = 14;
            for (int i = 0; i < posts; i++)
            {
                float a = i * Mathf.PI * 2f / posts;
                Transform p = Prim(PrimitiveType.Cube, _outpost, wall, fallback);
                p.localPosition = new Vector3(Mathf.Cos(a) * size, size * 0.18f, Mathf.Sin(a) * size);
                p.localScale = new Vector3(size * 0.42f, size * 0.36f, 0.5f);
                p.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
            }

            Transform tower = Prim(PrimitiveType.Cylinder, _outpost, wall, fallback);
            tower.localPosition = new Vector3(0f, size * 0.55f, 0f);
            tower.localScale = new Vector3(size * 0.5f, size * 0.55f, size * 0.5f);

            Transform roof = Prim(PrimitiveType.Cube, _outpost, wall, fallback * 0.8f);
            roof.localPosition = new Vector3(0f, size * 1.15f, 0f);
            roof.localScale = new Vector3(size * 0.7f, 0.3f, size * 0.7f);

            var bar = new GameObject("HpBar").transform;
            bar.SetParent(_outpost, false);
            bar.localPosition = new Vector3(0f, size * 1.6f, 0f);
            Transform back = Prim(PrimitiveType.Cube, bar, _hpBackMaterial, new Color(0.1f, 0.1f, 0.1f));
            back.localScale = new Vector3(6.2f, 0.45f, 0.04f);
            _hpFill = Prim(PrimitiveType.Cube, bar, _hpFillMaterial, new Color(0.85f, 0.25f, 0.2f));

            TerritoryCamera cam = _presenter.CameraRig;
            if (cam != null)
                cam.ExtendBounds(_outpost.position);
        }

        private void UpdateSinking()
        {
            if (_sinking == null)
                return;

            float k = (Time.time - _outpostSinkStart) / 2.5f;
            _sinking.position += Vector3.down * Time.deltaTime * 3f;
            _sinking.localRotation = Quaternion.Euler(k * 12f, 0f, k * 8f) * Quaternion.LookRotation(-_dir);
            if (k >= 1f)
            {
                Destroy(_sinking.gameObject);
                _sinking = null;
            }
        }

        private void OnOutpostDestroyed(OutpostDef o)
        {
            // 무너지면 맞붙던 적은 쓰러지고, 다음 기지 쪽에서 새로 나온다.
            for (int i = 0; i < _theirs.Count; i++)
                Kill(_theirs[i]);
        }

        /// <summary>카메라를 맞붙는 자리와 적 기지 사이로. HUD 전선 띠·임무 바가 부른다.</summary>
        public void FocusOutpost()
        {
            TerritoryCamera cam = _presenter.CameraRig;
            if (cam == null)
                return;

            FrontView v = _presenter.Runtime.Frontline.Describe();
            Vector3 target = v.HasOutpost ? Vector3.Lerp(Clash(v), OutpostPosition(v.Index), 0.5f) : _gate;
            cam.ExtendBounds(target);
            cam.FocusOn(target);
        }

        // ---------- 징집: 뒷문 → 병영 → 성문 ----------

        private void UpdateRecruits(FrontView v)
        {
            if (Time.time < _nextRecruit || _walkers.Count >= 6)
                return;

            // 막사가 서야 장정이 모인다 — 그 전에는 도적만 성문 앞을 서성인다(약탈이 일상인 세상).
            Vector3 barracksFront;
            if (!TryBuildingFront("barracks", out barracksFront))
                return;

            // 화력이 클수록 자주 들어온다(1.5~6초). 밀리고 있어도 징집은 계속된다 — 숫자로 이기지 못할 뿐.
            _nextRecruit = Time.time + Mathf.Clamp(6f - v.Power / 120f, 1.5f, 6f);

            Animator animator;
            GameObject go = _arena.SpawnDisplay("hoplite", true, 0.4f, _root, out animator, _walkers.Count);
            if (go == null)
                return;

            var w = new Walker { Go = go, Animator = animator };
            w.Path.Add(_backGate - _dir * 2f);
            w.Path.Add(_backGate + _dir * 2f);

            Vector3 barracks;
            if (TryBuildingFront("barracks", out barracks))
                w.Path.Add(barracks);
            Vector3 yard;
            if (TryBuildingFront("training_ground", out yard))
                w.Path.Add(yard);

            w.Path.Add(_gate - _dir * 1.5f);
            w.Path.Add(_gate + _dir * 4f);

            go.transform.position = w.Path[0];
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat(SpeedParam, 1f);
            _walkers.Add(w);
        }

        private bool TryBuildingFront(string defId, out Vector3 point)
        {
            point = Vector3.zero;
            TerritoryRuntime rt = _presenter.Runtime;
            foreach (BuildingInstance b in rt.State.Buildings)
            {
                BuildingDef def;
                if (b.DefId != defId || b.IsRuined || !rt.Catalog.TryGet(b.DefId, out def))
                    continue;

                Vector3 c = _presenter.Ground.FootprintCenterWorld(b.Anchor, def.Footprint, 0f);
                point = c - Vector3.forward * (def.Footprint.Height * _presenter.Ground.Grid.CellSize * 0.5f + 0.8f);
                return true;
            }

            return false;
        }

        private void UpdateWalkers()
        {
            for (int i = _walkers.Count - 1; i >= 0; i--)
            {
                Walker w = _walkers[i];
                if (w.Next >= w.Path.Count)
                {
                    // 성문을 나섰다 — 자리가 비면 전선에 들어가고, 아니면 성 밖으로 사라진다(행군해 나간 것으로 본다).
                    Fighter f = _ours.Count < _maxOurs ? NewFighter(w.Go, w.Animator, OurSlot(_ours.Count)) : null;
                    if (f != null)
                        _ours.Add(f);
                    else
                        Destroy(w.Go);

                    _walkers.RemoveAt(i);
                    continue;
                }

                Vector3 target = w.Path[w.Next];
                Vector3 pos = w.Go.transform.position;
                Vector3 to = target - pos;
                to.y = 0f;
                float step = _walkSpeed * Time.deltaTime;

                if (to.magnitude <= step)
                {
                    w.Go.transform.position = target;
                    w.Next++;
                    continue;
                }

                w.Go.transform.position = pos + to.normalized * step;
                w.Go.transform.rotation = Quaternion.Slerp(w.Go.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
            }
        }

        // ---------- 맞붙는 자리 ----------

        /// <summary>맞붙는 자리 — 밀리면 성문 쪽으로, 깎고 있으면 적 기지 쪽으로.</summary>
        private Vector3 Clash(FrontView v)
        {
            float t = v.Stalled || v.Waiting ? 0.18f : 0.5f;
            return Vector3.Lerp(_gate + _dir * 6f, OutpostPosition(v.Index) - _dir * 8f, t);
        }

        private Vector3 OurSlot(int i) => Offset(i, -1.2f);
        private Vector3 TheirSlot(int i) => Offset(i, 1.2f);

        private Vector3 Offset(int i, float side)
        {
            float lateral = ((i % 4) - 1.5f) * 1.3f;
            float depth = (i / 4) * 1.1f;
            return _right * lateral + _dir * (side * (1f + depth));
        }

        private Fighter NewFighter(GameObject go, Animator animator, Vector3 slot)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat(SpeedParam, 1f);
            return new Fighter { Go = go, Animator = animator, Slot = slot, NextSwing = Time.time + Random.Range(0.2f, 1.2f) };
        }

        private void UpdateFight(FrontView v)
        {
            Vector3 clash = Clash(v);

            // 적 보충 — 적 기지 쪽에서 걸어 나온다.
            if (_theirs.Count < _maxTheirs && Time.time >= _nextTheirSpawn && !v.Waiting)
            {
                _nextTheirSpawn = Time.time + 1.2f;
                Animator animator;
                string enemy = string.IsNullOrEmpty(v.Outpost.EnemyUnitId) ? "bandit" : v.Outpost.EnemyUnitId;
                float radius = enemy.StartsWith("bandit") ? 0.4f : 0.9f;
                GameObject go = _arena.SpawnDisplay(enemy, false, radius, _root, out animator, _theirs.Count + (int)Time.time);
                if (go != null)
                {
                    go.transform.position = OutpostPosition(v.Index) - _dir * 5f + _right * Random.Range(-2f, 2f);
                    _theirs.Add(NewFighter(go, animator, TheirSlot(_theirs.Count)));
                }
            }

            // 쓰러지는 박자 — 깎는 속도가 빠를수록 적이 자주, 밀릴수록 우리가 자주 쓰러진다.
            float rate = v.Rate;
            if (Time.time >= _nextTheirDeath && CountAlive(_theirs) > 0 && rate > 0f)
            {
                _nextTheirDeath = Time.time + Mathf.Clamp(300f / rate, 0.8f, 6f);
                KillOne(_theirs);
            }

            if (Time.time >= _nextOurDeath && CountAlive(_ours) > 0)
            {
                _nextOurDeath = Time.time + (v.Stalled ? 1.6f : 5f);
                KillOne(_ours);
            }

            Drive(_ours, clash, v);
            Drive(_theirs, clash, v);
            Sweep(_ours);
            Sweep(_theirs);
        }

        private void Drive(List<Fighter> list, Vector3 clash, FrontView v)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Fighter f = list[i];
                if (f.DiedAt >= 0f)
                    continue;

                Vector3 target = clash + f.Slot;
                Vector3 pos = f.Go.transform.position;
                Vector3 to = target - pos;
                to.y = 0f;
                float step = _walkSpeed * Time.deltaTime;

                if (to.magnitude > 0.15f)
                {
                    f.Go.transform.position = pos + to.normalized * Mathf.Min(step, to.magnitude);
                    f.Go.transform.rotation = Quaternion.Slerp(f.Go.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
                    if (f.Arrived && f.Animator != null && f.Animator.runtimeAnimatorController != null)
                        f.Animator.SetFloat(SpeedParam, 1f);
                    f.Arrived = false;
                    continue;
                }

                if (!f.Arrived)
                {
                    f.Arrived = true;
                    if (f.Animator != null && f.Animator.runtimeAnimatorController != null)
                        f.Animator.SetFloat(SpeedParam, 0f);
                }

                // 마주 보고 휘두른다.
                Vector3 face = clash - f.Go.transform.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f)
                    f.Go.transform.rotation = Quaternion.Slerp(f.Go.transform.rotation, Quaternion.LookRotation(face), Time.deltaTime * 6f);

                if (Time.time >= f.NextSwing && f.Animator != null && f.Animator.runtimeAnimatorController != null)
                {
                    f.Animator.SetInteger(VariantParam, Random.Range(0, 2));
                    f.Animator.SetTrigger(AttackParam);
                    f.NextSwing = Time.time + Random.Range(1.0f, 1.8f);
                }
            }
        }

        private static int CountAlive(List<Fighter> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].DiedAt < 0f && list[i].Arrived) n++;
            return n;
        }

        private void KillOne(List<Fighter> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].DiedAt < 0f && list[i].Arrived)
                {
                    Kill(list[i]);
                    return;
                }
            }
        }

        private static void Kill(Fighter f)
        {
            if (f.DiedAt >= 0f)
                return;

            f.DiedAt = Time.time;
            if (f.Animator != null && f.Animator.runtimeAnimatorController != null)
                f.Animator.SetTrigger(DieParam);
            else
                f.Go.transform.rotation *= Quaternion.Euler(0f, 0f, 85f);
        }

        /// <summary>쓰러진 병사를 2초 뒤 치우고 남은 병사의 자리를 다시 매긴다.</summary>
        private void Sweep(List<Fighter> list)
        {
            bool removed = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].DiedAt >= 0f && Time.time - list[i].DiedAt > 2f)
                {
                    Destroy(list[i].Go);
                    list.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
                return;

            bool ours = list == _ours;
            for (int i = 0; i < list.Count; i++)
                list[i].Slot = ours ? OurSlot(i) : TheirSlot(i);
        }

        private static Transform Prim(PrimitiveType type, Transform parent, Material material, Color fallback)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);

            Renderer r = go.GetComponent<Renderer>();
            if (material != null)
                r.sharedMaterial = material;
            else
                r.material.color = fallback;

            return go.transform;
        }
    }
}

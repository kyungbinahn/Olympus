using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.Territory;

namespace Olympus.Game.Territory
{
    /// <summary>성벽 키트 한 단계(아트 L1~L5) — 직선·이음 기둥·성문. 성문이 없는 단계는 그 자리를 비운다.</summary>
    [System.Serializable]
    public sealed class WallKitEntry
    {
        [Tooltip("이 키트를 쓰는 성벽 단계(walls.json의 hqLevel).")]
        public int hqLevel;
        public GameObject straight;
        public GameObject post;
        public GameObject gate;

        [Tooltip("조각마다 텍스처가 따로다(아트 키트가 조각별로 생성됨).")]
        public Material straightMaterial;
        public Material postMaterial;
        public Material gateMaterial;

        [Tooltip("조각 앞뒤를 뒤집는다 — 버팀목·발판 같은 것이 성 안쪽에 오게(아트 키트는 앞뒤 규칙이 없다).")]
        public bool flip;
    }

    /// <summary>
    /// 본부 레벨마다 넓어지는 성벽을 그린다(형님 "벽 제작이 레벨별로 끝났는데 본부 레벨별로 계속 기지 확장되는 연출", 2026-10-27).
    ///
    /// 어디에 몇 조각이 서는지는 Core(<see cref="WallRing"/>)가, 레벨별 크기는 <c>walls.json</c>이 정한다 — 여기는 모델을 세우고
    /// 레벨이 오를 때 옛 성벽이 가라앉고 새 성벽이 성문에서부터 양쪽으로 솟는 연출만 한다.
    /// 본부 레벨은 상태에서 매 프레임 읽는다 — 레벨업 경로(임무·디버그·세이브 불러오기)마다 갱신을 붙이면 하나를 빠뜨린다(CLAUDE.md §4).
    /// </summary>
    public sealed class TerritoryWallView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private TextAsset _walls;
        [SerializeField] private List<WallKitEntry> _kits = new List<WallKitEntry>();

        [Header("연출")]
        [Tooltip("옛 성벽이 가라앉는 시간(초).")]
        [SerializeField] private float _sinkSeconds = 0.8f;

        [Tooltip("성문에서 반대쪽 모서리까지 새 성벽이 솟아 퍼지는 시간(초).")]
        [SerializeField] private float _sweepSeconds = 2.4f;

        [Tooltip("조각 하나가 땅에서 솟는 시간(초).")]
        [SerializeField] private float _riseSeconds = 0.45f;

        private WallConfig _config;
        private Vector3 _center;
        private WallTier _shown;
        private Transform _ring;
        private Transform _sinking;
        private float _sinkStart;
        private readonly List<(Transform piece, float delay, float baseY)> _rising = new List<(Transform, float, float)>();
        private float _riseStart = -1f;

        private Vector3 _gateCenter;
        private Vector3 _gateOut;
        private bool _hasGate;

        /// <summary>
        /// 지금 성벽의 성문 — 장정이 지나는 안쪽·바깥쪽 점. 성벽이 아직 없으면(본부 폐허) 거짓.
        /// </summary>
        public bool TryGetGate(out Vector3 inside, out Vector3 outside)
        {
            inside = _gateCenter - _gateOut * 2.5f;
            outside = _gateCenter + _gateOut * 2.5f;
            return _hasGate;
        }

        private void Start()
        {
            if (_presenter == null || _presenter.Runtime == null || _walls == null)
            {
                enabled = false;
                return;
            }

            _config = JsonUtility.FromJson<WallFile>(_walls.text).ToConfig();
            List<string> problems = _config.Validate();
            if (problems.Count > 0)
                throw new System.InvalidOperationException("walls.json: " + string.Join(" / ", problems));

            // 가운데 = 기지 칸 범위의 가운데(RealData 시험과 같은 계산 — 짝수 기지는 칸 중심이 정수라 -0.5).
            GridRect b = _presenter.Ground.Bounds;
            float cell = _presenter.Ground.Grid.CellSize;
            _center = _presenter.Ground.transform.TransformPoint(new Vector3(
                (b.MinX + b.MaxXExclusive - 1) * 0.5f * cell, 0f, (b.MinY + b.MaxYExclusive - 1) * 0.5f * cell));

            Show(_config.ForHq(_presenter.Runtime.HqLevel), false);
        }

        private void Update()
        {
            WallTier want = _config.ForHq(_presenter.Runtime.HqLevel);
            if (want != _shown)
                Show(want, true);

            Animate();
        }

        private void Show(WallTier tier, bool animate)
        {
            // 옛 성벽 — 연출이면 가라앉히고, 아니면 바로 치운다. 가라앉는 중에 또 오르면 그건 바로 치운다.
            if (_sinking != null)
                Destroy(_sinking.gameObject);
            _sinking = null;
            if (_ring != null)
            {
                if (animate)
                {
                    _sinking = _ring;
                    _sinkStart = Time.time;
                }
                else
                {
                    Destroy(_ring.gameObject);
                }
            }

            _ring = null;
            _rising.Clear();
            _shown = tier;
            _hasGate = false;
            if (tier == null)
                return;

            WallKitEntry kit = KitFor(tier.HqLevel);
            if (kit == null || kit.straight == null || kit.post == null)
            {
                Debug.LogWarning("성벽 Lv." + tier.HqLevel + " 키트(직선·기둥)가 비어 있어 성벽을 세우지 않습니다.");
                return;
            }

            _ring = new GameObject("Walls_Lv" + tier.HqLevel).transform;
            _ring.SetParent(transform, false);

            float gateLength = 0f;
            if (kit.gate != null)
            {
                Transform probe = Spawn(kit, kit.gate, Vector3.zero, 0f);
                Vector3 c;
                Vector2 half;
                if (BuildingModelPlacement.TryMeasureGroundRect(probe, 0f, out c, out half))
                    gateLength = half.x * 2f;
                DestroyImmediate(probe.gameObject);
            }
            int gateUnits = Mathf.Max(1, Mathf.CeilToInt(gateLength / _config.Unit - 0.01f));

            var center = new WorldXZ(_center.x, _center.z);
            List<WallPiece> pieces = WallRing.Layout(center, tier.HalfSize, _config.Unit, gateUnits);

            Vector3 gatePos = _center;
            foreach (WallPiece p in pieces)
            {
                if (p.Kind == WallPieceKind.Gate)
                    gatePos = new Vector3(p.Center.X, _center.y, p.Center.Z);
            }

            float far = 0f;
            var placed = new List<(Transform, float)>();
            foreach (WallPiece p in pieces)
            {
                var pos = new Vector3(p.Center.X, _center.y, p.Center.Z);
                if (p.Kind == WallPieceKind.Gate)
                {
                    _gateCenter = pos;
                    _gateOut = Quaternion.Euler(0f, p.YawDegrees, 0f) * Vector3.forward;
                    _hasGate = true;
                    if (kit.gate == null)
                        continue;   // 이 단계 성문 모델이 없다 — 길만 열어 둔다(양 끝 기둥은 이미 섰다)

                    placed.Add((Spawn(kit, kit.gate, pos, p.YawDegrees), 0f));

                    // 성문이 잘린 대각선보다 짧으면 양 끝 틈을 기둥으로 덮는다.
                    Vector3 along = Quaternion.Euler(0f, p.YawDegrees, 0f) * Vector3.right;
                    for (int s = -1; s <= 1; s += 2)
                        placed.Add((Spawn(kit, kit.post, pos + along * (s * gateLength * 0.5f), p.YawDegrees), 0f));
                    continue;
                }

                GameObject model = p.Kind == WallPieceKind.Post ? kit.post : kit.straight;
                float dist = (pos - gatePos).magnitude;
                far = Mathf.Max(far, dist);
                placed.Add((Spawn(kit, model, pos, p.YawDegrees), dist));
            }

            if (!animate)
                return;

            // 성문에서 둘레를 따라 양쪽으로 — 거리에 비례해 늦게 솟는다. 처음엔 땅 밑에 숨겨 둔다.
            foreach ((Transform t, float dist) in placed)
            {
                float delay = far > 0.01f ? dist / far * _sweepSeconds : 0f;
                float baseY = t.localPosition.y;
                t.localPosition += Vector3.down * 8f;
                _rising.Add((t, delay, baseY));
            }
            _riseStart = Time.time;
        }

        private void Animate()
        {
            if (_sinking != null)
            {
                float k = (Time.time - _sinkStart) / Mathf.Max(0.01f, _sinkSeconds);
                _sinking.localPosition = Vector3.down * (k * k * 7f);
                if (k >= 1f)
                {
                    Destroy(_sinking.gameObject);
                    _sinking = null;
                }
            }

            if (_riseStart < 0f || _rising.Count == 0)
                return;

            // 옛 성벽이 거의 가라앉은 뒤에 솟기 시작한다.
            float now = Time.time - _riseStart - _sinkSeconds * 0.6f;
            bool done = true;
            foreach ((Transform t, float delay, float baseY) in _rising)
            {
                float k = Mathf.Clamp01((now - delay) / Mathf.Max(0.01f, _riseSeconds));
                if (k < 1f)
                    done = false;
                // 살짝 넘쳤다가 앉는다(땅에서 밀고 올라오는 느낌).
                float e = 1f - Mathf.Pow(1f - k, 3f);
                float over = Mathf.Sin(k * Mathf.PI) * 0.25f;
                Vector3 lp = t.localPosition;
                lp.y = baseY + Mathf.Lerp(-8f, 0f, e) + over;
                t.localPosition = lp;
            }

            if (done)
            {
                foreach ((Transform t, float _, float baseY) in _rising)
                {
                    Vector3 lp = t.localPosition;
                    lp.y = baseY;
                    t.localPosition = lp;
                }
                _rising.Clear();
                _riseStart = -1f;
            }
        }

        private Transform Spawn(WallKitEntry kit, GameObject model, Vector3 position, float yaw)
        {
            Material m = model == kit.gate ? kit.gateMaterial : model == kit.post ? kit.postMaterial : kit.straightMaterial;
            return Spawn(kit, model, m, position, yaw);
        }

        private Transform Spawn(WallKitEntry kit, GameObject model, Material material, Vector3 position, float yaw)
        {
            // 래퍼만 돌린다 — 모델 루트의 축 보정 회전(Blender FBX X -90°)은 그대로 둔다(BuildingModelPlacement 주석).
            var wrapper = new GameObject(model.name).transform;
            wrapper.SetParent(_ring != null ? _ring : transform, false);
            wrapper.position = position;
            wrapper.rotation = Quaternion.Euler(0f, yaw + (kit.flip ? 180f : 0f), 0f);

            GameObject instance = Instantiate(model, wrapper);
            if (material != null)
            {
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = material;
            }
            return wrapper;
        }

        private WallKitEntry KitFor(int hqLevel)
        {
            WallKitEntry best = null;
            foreach (WallKitEntry k in _kits)
            {
                if (k != null && k.hqLevel <= hqLevel && (best == null || k.hqLevel > best.hqLevel))
                    best = k;
            }
            return best;
        }
    }
}

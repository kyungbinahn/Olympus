using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Grid;
using Olympus.Core.State;
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
    /// 모양은 그 레벨까지 열리는 건물을 따라 도는 둘레다(Core <see cref="WallRing"/>, 규칙 <c>walls.json</c>) — 여기는 모델을 세우고,
    /// 레벨이 오를 때 옛 성벽이 가라앉고 새 성벽이 성문에서부터 양쪽으로 솟는 연출과, 지면 셰이더가 성 밖을 그을린 땅·용암으로
    /// 칠하도록 성벽 안팎 거리장을 굽는 일만 한다. 거리장도 옛→새를 같은 시간에 섞어서 땅이 성벽과 함께 넓어진다.
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

        [Header("성 밖 지면")]
        [Tooltip("성벽 안팎 거리장 한 텍셀(m). 작을수록 성벽 선이 또렷하고 굽는 데 오래 걸린다.")]
        [SerializeField] private float _fieldTexel = 0.5f;

        [Tooltip("거리장이 덮는 범위(마을 가운데에서 한쪽으로, m). 밖은 셰이더가 '더 멀다'로 이어 그린다.")]
        [SerializeField] private float _fieldHalfSize = 64f;

        private static readonly WorldXZ FrontDirection = new WorldXZ(1f, 1f);   // 전선(협곡·도적 소굴)이 뻗은 쪽

        private static readonly int FieldOldId = Shader.PropertyToID("_OlympusWallFieldOld");
        private static readonly int FieldNewId = Shader.PropertyToID("_OlympusWallFieldNew");
        private static readonly int FieldRectId = Shader.PropertyToID("_OlympusWallFieldRect");
        private static readonly int FieldBlendId = Shader.PropertyToID("_OlympusWallBlend");
        private static readonly int RoadId = Shader.PropertyToID("_OlympusWallRoad");

        private WallConfig _config;
        private Vector3 _center;
        private Texture2D _fieldOld;
        private Texture2D _fieldNew;
        private byte[] _fieldPixels;
        private float _fieldStart = -1f;
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
            problems.AddRange(_config.CheckAgainst(_presenter.Runtime.Progression));
            if (problems.Count > 0)
                throw new System.InvalidOperationException("walls.json: " + string.Join(" / ", problems));

            // 가운데 = 기지 칸 범위의 가운데(RealData 시험과 같은 계산 — 짝수 기지는 칸 중심이 정수라 -0.5).
            GridRect b = _presenter.Ground.Bounds;
            float cell = _presenter.Ground.Grid.CellSize;
            _center = _presenter.Ground.transform.TransformPoint(new Vector3(
                (b.MinX + b.MaxXExclusive - 1) * 0.5f * cell, 0f, (b.MinY + b.MaxYExclusive - 1) * 0.5f * cell));

            Show(_config.Current(_presenter.Runtime.State, _presenter.Runtime.Progression), false);
        }

        private void OnDestroy()
        {
            // 전역에 남겨 두면 다음 씬(월드맵·전투)의 같은 지면 셰이더가 지워진 텍스처를 읽는다.
            Shader.SetGlobalVector(FieldRectId, Vector4.zero);
            Shader.SetGlobalVector(RoadId, Vector4.zero);
            if (_fieldOld != null)
                Destroy(_fieldOld);
            if (_fieldNew != null)
                Destroy(_fieldNew);
        }

        private void Update()
        {
            // 영토 단계 = 무찌른 적(도적 소굴·거인). 거인을 이긴 순간 넓어진다 — 본부 공사를 기다리지 않는다.
            WallTier want = _config.Current(_presenter.Runtime.State, _presenter.Runtime.Progression);
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
            {
                Shader.SetGlobalVector(FieldRectId, Vector4.zero);   // 성벽이 없으면(본부 폐허) 성 밖 효과도 없다
                return;
            }

            var slots = new List<(string, GridPos)>();
            foreach (BuildingInstance b in _presenter.Runtime.State.Buildings)
                slots.Add((b.DefId, b.Anchor));
            List<GroundPatch> village = WallRing.VillagePatches(slots, _presenter.Runtime.Catalog, _presenter.Ground.Grid, tier.HqLevel);
            WallPlan plan = WallRing.Plan(village, _config, tier.GateLength, FrontDirection);
            if (plan == null)
                return;

            BakeField(plan, animate);

            _gateCenter = new Vector3(plan.GateCenter.X, _center.y, plan.GateCenter.Z);
            _gateOut = Quaternion.Euler(0f, plan.GateYawDegrees, 0f) * Vector3.forward;
            _hasGate = true;
            Shader.SetGlobalVector(RoadId, new Vector4(_gateCenter.x, _gateCenter.z, _gateOut.x, _gateOut.z));

            WallKitEntry kit = KitFor(tier.HqLevel);
            if (kit == null || kit.straight == null || kit.post == null)
            {
                Debug.LogWarning("성벽 Lv." + tier.HqLevel + " 키트(직선·기둥)가 비어 있어 성벽을 세우지 않습니다.");
                return;
            }

            _ring = new GameObject("Walls_Lv" + tier.HqLevel).transform;
            _ring.SetParent(transform, false);

            float gateModelLength = 0f;
            if (kit.gate != null)
            {
                Transform probe = Spawn(kit, kit.gate, Vector3.zero, 0f);
                Vector3 c;
                Vector2 half;
                if (BuildingModelPlacement.TryMeasureGroundRect(probe, 0f, out c, out half))
                    gateModelLength = half.x * 2f;
                DestroyImmediate(probe.gameObject);
                if (Mathf.Abs(gateModelLength - tier.GateLength) > 0.5f)
                    Debug.LogWarning("성벽 Lv." + tier.HqLevel + " 성문 모델 길이 " + gateModelLength.ToString("0.0") + "m ≠ walls.json gateLength " +
                                     tier.GateLength + "m — 성문이 늘거나 줄어 보입니다. walls.json을 모델에 맞추세요.");
            }

            float far = 0f;
            var placed = new List<(Transform, float)>();
            foreach (WallPiece p in plan.Pieces)
            {
                var pos = new Vector3(p.Center.X, _center.y, p.Center.Z);
                float dist = (pos - _gateCenter).magnitude;
                far = Mathf.Max(far, dist);

                Transform t;
                if (p.Kind == WallPieceKind.Gate)
                {
                    if (kit.gate == null)
                        continue;   // 이 단계 성문 모델이 없다 — 길만 열어 둔다(양 끝 기둥은 둘레 기둥이 선다)
                    t = Spawn(kit, kit.gate, pos, p.YawDegrees);
                    if (gateModelLength > 0.01f)
                        Stretch(t, p.Span / gateModelLength);
                }
                else if (p.Kind == WallPieceKind.Post)
                {
                    t = Spawn(kit, kit.post, pos, p.YawDegrees);
                }
                else
                {
                    t = Spawn(kit, kit.straight, pos, p.YawDegrees);
                    Stretch(t, p.Span / _config.Unit);
                }
                placed.Add((t, dist));
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

        /// <summary>조각을 길이 방향(래퍼 로컬 X)으로만 늘이거나 줄인다 — 둘레 구간이 조각 길이의 배수가 아니라서.</summary>
        private static void Stretch(Transform wrapper, float factor)
        {
            wrapper.localScale = new Vector3(factor, 1f, 1f);
        }

        /// <summary>
        /// 성벽 안팎 거리장을 굽는다. 연출이면 지금 보이는 거리장을 '옛'으로 돌리고 새로 구운 것과 섞기 시작한다.
        /// </summary>
        private void BakeField(WallPlan plan, bool animate)
        {
            int size = Mathf.Max(8, Mathf.CeilToInt(_fieldHalfSize * 2f / _fieldTexel));
            if (_fieldNew == null || _fieldNew.width != size)
            {
                if (_fieldOld != null) Destroy(_fieldOld);
                if (_fieldNew != null) Destroy(_fieldNew);
                _fieldOld = CreateField(size);
                _fieldNew = CreateField(size);
                _fieldPixels = new byte[size * size];
                animate = false;
            }

            if (animate)
            {
                Texture2D t = _fieldOld;
                _fieldOld = _fieldNew;
                _fieldNew = t;
            }

            var corner = new WorldXZ(_center.x - _fieldHalfSize, _center.z - _fieldHalfSize);
            WallField.Bake(_fieldPixels, size, size, corner, _fieldTexel, plan.Outline);
            _fieldNew.SetPixelData(_fieldPixels, 0);
            _fieldNew.Apply(false, false);
            if (!animate)
            {
                _fieldOld.SetPixelData(_fieldPixels, 0);
                _fieldOld.Apply(false, false);
            }

            float world = size * _fieldTexel;
            Shader.SetGlobalTexture(FieldOldId, _fieldOld);
            Shader.SetGlobalTexture(FieldNewId, _fieldNew);
            Shader.SetGlobalVector(FieldRectId, new Vector4(corner.X, corner.Z, 1f / world, 1f / world));
            Shader.SetGlobalFloat(FieldBlendId, animate ? 0f : 1f);
            _fieldStart = animate ? Time.time : -1f;
        }

        private static Texture2D CreateField(int size)
        {
            return new Texture2D(size, size, TextureFormat.R8, false, true)
            {
                name = "OlympusWallField",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        private void Animate()
        {
            if (_fieldStart >= 0f)
            {
                // 땅은 새 성벽이 솟는 동안 같이 넓어진다(옛 성벽이 가라앉는 시간 뒤부터).
                float k = Mathf.Clamp01((Time.time - _fieldStart - _sinkSeconds * 0.6f) / Mathf.Max(0.01f, _sweepSeconds + _riseSeconds));
                Shader.SetGlobalFloat(FieldBlendId, k * k * (3f - 2f * k));
                if (k >= 1f)
                    _fieldStart = -1f;
            }

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

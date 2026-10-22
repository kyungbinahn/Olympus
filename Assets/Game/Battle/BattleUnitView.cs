using UnityEngine;
using Olympus.Core.Battle;

namespace Olympus.Game.Battle
{
    /// <summary>
    /// 병사 한 명의 모습. 위치·생사는 Core의 <see cref="BattleUnit"/>을 읽기만 한다.
    ///
    /// 애니메이션 없이 "움직이는 것처럼" 보이게 하는 장치 네 가지 — 걸을 때 위아래로 흔들기,
    /// 칠 때 앞으로 찌르기, 맞을 때 하얗게 번쩍, 죽을 때 옆으로 넘어져 가라앉기.
    /// 실제 애니메이션 모델이 들어오면 <see cref="Animator"/>에 같은 순간을 알려 준다
    /// (Speed float / Attack trigger / Die trigger) — 그때는 위 흉내를 끈다.
    /// </summary>
    internal sealed class BattleUnitView
    {
        private const float BobHeight = 0.12f;
        private const float BobSpeed = 14f;
        private const float LungeDistance = 0.45f;
        private const float LungeDecay = 9f;
        private const float FlashSeconds = 0.08f;
        private const float FallSeconds = 0.45f;
        private const float SinkSeconds = 1.2f;
        private const float DeathAnimSeconds = 2.4f;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int DieParam = Animator.StringToHash("Die");
        private static readonly int VariantParam = Animator.StringToHash("Variant");

        private int _attacks;

        public readonly int UnitId;
        public readonly Transform Root;

        /// <summary>흔들기·찌르기·넘어지기를 거는 자리. 루트는 위치·방향만 맡는다.</summary>
        private readonly Transform _body;
        private readonly Renderer[] _renderers;
        private readonly Material[] _baseMaterials;
        private readonly Material _flashMaterial;
        private readonly Animator _animator;

        /// <summary>
        /// 실제 모델일 때 진영 색. 도형 병사는 재질 자체가 진영 색이라 null이다.
        /// 모델은 재질을 바꾸지 않고 색만 덧칠한다(번쩍임도 색으로) — 텍스처가 날아가지 않게.
        /// </summary>
        private readonly Color? _tint;
        private readonly MaterialPropertyBlock _block;
        private bool _lastFlash;

        private readonly Transform _hpBar;
        private readonly Transform _hpFill;
        private readonly float _hpWidth;

        private Vector3 _from;
        private Vector3 _to;
        private float _lunge;
        private float _flashUntil;
        private float _diedAt = -1f;
        private float _phase;

        public BattleUnitView(int unitId, Transform root, Transform body, Animator animator,
            Material flashMaterial, Color? tint, Transform hpBar, Transform hpFill, float hpWidth, Vector3 start)
        {
            UnitId = unitId;
            Root = root;
            _body = body;
            _animator = animator;
            _flashMaterial = flashMaterial;
            _tint = tint;
            _hpBar = hpBar;
            _hpFill = hpFill;
            _hpWidth = hpWidth;

            _renderers = body.GetComponentsInChildren<Renderer>();
            _baseMaterials = new Material[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _baseMaterials[i] = _renderers[i].sharedMaterial;

            if (_tint.HasValue)
            {
                _block = new MaterialPropertyBlock();
                ApplyTint(_tint.Value);
            }

            _from = _to = start;
            root.localPosition = start;

            // 병사마다 동작 빠르기·시작 위치를 조금씩 다르게. 번호로 정해서 매번 같다.
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                float k = ((unitId * 37) % 100) / 100f;
                _animator.speed = 0.88f + 0.24f * k;
                _animator.Update(0f);
                _animator.Play(0, 0, k);
            }
            _phase = unitId * 0.7f;   // 다 같이 발맞춰 흔들리면 기계처럼 보인다.
        }

        /// <summary>시뮬레이션이 한 걸음 나아갔다 — 지금 자리에서 새 자리로 보간을 다시 건다.</summary>
        public void OnStep(BattleUnit unit, float y)
        {
            _from = Root.localPosition;
            _to = new Vector3(unit.X, y, unit.Z);
        }

        public void OnAttack()
        {
            _lunge = 1f;
            if (_animator != null)
            {
                // 공격 동작을 번갈아 — 전원이 같은 동작을 같은 박자로 하면 기계처럼 보인다(2026-10-22).
                _animator.SetInteger(VariantParam, (UnitId + _attacks++) % 2);
                _animator.SetTrigger(AttackParam);
            }
        }

        public void OnHit()
        {
            _flashUntil = Time.time + FlashSeconds;
        }

        public void OnDeath()
        {
            if (_diedAt >= 0f)
                return;

            _diedAt = Time.time;
            if (_hpBar != null)
                _hpBar.gameObject.SetActive(false);

            if (_animator != null)
                _animator.SetTrigger(DieParam);
        }

        /// <param name="t">이번 걸음 안에서 어디쯤인가(0~1). 걸음(50ms)보다 화면이 자주 그려지므로 사이를 메운다.</param>
        public void Render(BattleUnit unit, float t, Camera camera)
        {
            float now = Time.time;

            Root.localPosition = Vector3.Lerp(_from, _to, t);

            Vector3 facing = new Vector3(unit.FacingX, 0f, unit.FacingZ);
            if (facing.sqrMagnitude > 0.0001f && _diedAt < 0f)
                Root.localRotation = Quaternion.Slerp(Root.localRotation, Quaternion.LookRotation(facing), Time.deltaTime * 10f);

            if (_animator != null)
            {
                _animator.SetFloat(SpeedParam, unit.IsMoving ? 1f : 0f);
            }
            else
            {
                Vector3 local = Vector3.zero;

                if (unit.IsMoving && _diedAt < 0f)
                    local.y = Mathf.Abs(Mathf.Sin(now * BobSpeed + _phase)) * BobHeight;

                _lunge = Mathf.MoveTowards(_lunge, 0f, Time.deltaTime * LungeDecay);
                local.z = Mathf.Sin(_lunge * Mathf.PI) * LungeDistance;

                if (_diedAt >= 0f)
                {
                    float k = Mathf.Clamp01((now - _diedAt) / FallSeconds);
                    float sink = Mathf.Clamp01((now - _diedAt - FallSeconds) / SinkSeconds);
                    _body.localRotation = Quaternion.Euler(0f, 0f, 85f * k * (UnitId % 2 == 0 ? 1f : -1f));
                    local.y = -sink * 1.5f;
                }

                _body.localPosition = local;
            }

            bool flashing = now < _flashUntil && _diedAt < 0f;

            if (_tint.HasValue)
            {
                if (flashing != _lastFlash)
                    ApplyTint(flashing ? Color.white * 2f : _tint.Value);
            }
            else
            {
                for (int i = 0; i < _renderers.Length; i++)
                    _renderers[i].sharedMaterial = flashing && _flashMaterial != null ? _flashMaterial : _baseMaterials[i];
            }

            _lastFlash = flashing;

            UpdateHpBar(unit, camera);

            // 실제 동작이 있으면 쓰러지는 동작을 끝까지 보여 준 뒤 치운다.
            float vanishAfter = _animator != null ? DeathAnimSeconds : FallSeconds + SinkSeconds;
            if (_diedAt >= 0f && now - _diedAt > vanishAfter)
                Root.gameObject.SetActive(false);
        }

        private void ApplyTint(Color color)
        {
            _block.SetColor("_BaseColor", color);
            _block.SetColor("_Color", color);
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].SetPropertyBlock(_block);
        }

        /// <summary>체력바는 다쳤을 때만 보인다 — 수십 명이 꽉 찬 바를 달고 있으면 화면이 시끄럽다.</summary>
        private void UpdateHpBar(BattleUnit unit, Camera camera)
        {
            if (_hpBar == null || _diedAt >= 0f)
                return;

            bool show = unit.Hp < unit.MaxHp;
            if (_hpBar.gameObject.activeSelf != show)
                _hpBar.gameObject.SetActive(show);

            if (!show)
                return;

            if (camera != null)
                _hpBar.rotation = camera.transform.rotation;

            float frac = unit.MaxHp > 0 ? (float)unit.Hp / unit.MaxHp : 0f;
            _hpFill.localScale = new Vector3(_hpWidth * frac, _hpFill.localScale.y, _hpFill.localScale.z);
            _hpFill.localPosition = new Vector3(-_hpWidth * (1f - frac) * 0.5f, 0f, -0.01f);
        }
    }
}

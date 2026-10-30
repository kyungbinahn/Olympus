using System.Collections.Generic;
using UnityEngine;
using Olympus.Core.Chapter;
using Olympus.Core.State;
using Olympus.Core.Territory;
using Olympus.Game.Battle;
using Olympus.Game.Territory;

namespace Olympus.Game.Chapter
{
    /// <summary>
    /// 기른 용사가 기지에 <b>보이게</b> 한다 — 병영 앞에 줄 선 용사, 훈련장에서 창을 휘두르는 훈련병.
    /// "0/300"이 숫자로만 오르면 내가 무엇을 모으는지 손에 안 잡힌다(2026-10-22 형님).
    ///
    /// 한 명 = 용사 여러 명이다(<see cref="_troopsPerFigure"/>). 300을 다 세우면 무겁고 건물 사이 틈에 안 들어간다.
    /// 상태는 읽기만 한다 — 판단은 Core(<see cref="TrainingService"/>).
    /// </summary>
    public sealed class TroopGarrisonView : MonoBehaviour
    {
        [SerializeField] private TerritoryPresenter _presenter;
        [SerializeField] private BattleArena _arena;
        [SerializeField] private GiantThreatDirector _director;

        [Tooltip("병영 앞에 서는 한 명이 대신하는 용사 수.")]
        [SerializeField] private int _troopsPerFigure = 20;

        [Tooltip("병영 앞에 서는 최대 인원(줄당 5명).")]
        [SerializeField] private int _maxStanding = 15;

        [Tooltip("훈련장에서 훈련하는 최대 인원.")]
        [SerializeField] private int _maxDrilling = 8;

        [Tooltip("그림 속 용사 크기(판정 반지름 기준). 건물보다 작게 보여야 기지가 장난감처럼 안 보인다.")]
        [SerializeField] private float _figureRadius = 0.32f;

        private const string BarracksDefault = "barracks";
        private const string TrainingGroundDefId = "training_ground";

        private readonly List<Figure> _standing = new List<Figure>();
        private readonly List<Figure> _drilling = new List<Figure>();
        private float _nextRefresh;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int VariantParam = Animator.StringToHash("Variant");

        private sealed class Figure
        {
            public GameObject Go;
            public Animator Animator;
            public float NextSwing;
            public int Index;
        }

        private void Update()
        {
            if (_presenter == null || _presenter.Runtime == null || _arena == null)
                return;

            DrillSwings();

            if (Time.time < _nextRefresh)
                return;

            _nextRefresh = Time.time + 0.5f;
            Refresh();
        }

        private void Refresh()
        {
            TerritoryRuntime rt = _presenter.Runtime;
            TrainingDef def = rt.Training.Def;
            if (def == null)
                return;

            TrainingView v = rt.Training.Describe();
            bool away = _director != null && _director.Fighting;   // 협곡에 나가 있는 동안은 병영이 빈다.

            BuildingInstance barracks = FindComplete(def.BarracksDefId ?? BarracksDefault);
            BuildingInstance yard = FindComplete(TrainingGroundDefId);

            int standing = barracks == null || away ? 0
                : Mathf.Min(_maxStanding, Mathf.CeilToInt(v.Troops / (float)Mathf.Max(1, _troopsPerFigure)));
            int drilling = !v.Running || away ? 0
                : Mathf.Clamp(Mathf.CeilToInt(v.OrderCount / 6f), 2, _maxDrilling);

            Resize(_standing, standing, def.UnitId);
            Resize(_drilling, drilling, def.UnitId);

            if (barracks != null)
                LineUpInFront(_standing, barracks, 0);

            // 훈련장이 아직 폐허면 병영 앞(줄 선 용사 뒤)에서 훈련한다.
            if (yard != null)
                Spread(_drilling, yard);
            else if (barracks != null)
                LineUpInFront(_drilling, barracks, Mathf.CeilToInt(standing / 5f));
        }

        private BuildingInstance FindComplete(string defId)
        {
            foreach (BuildingInstance b in _presenter.Runtime.State.Buildings)
            {
                if (b.DefId == defId && !b.IsRuined && b.Level > 0)
                    return b;
            }

            return null;
        }

        private void Resize(List<Figure> list, int count, string unitId)
        {
            while (list.Count > count)
            {
                Destroy(list[list.Count - 1].Go);
                list.RemoveAt(list.Count - 1);
            }

            while (list.Count < count)
            {
                Animator animator;
                GameObject go = _arena.SpawnDisplay(unitId, true, _figureRadius, transform, out animator);
                if (go == null)
                    return;   // 병사 모델이 없다 — 숫자만 보인다.

                if (animator != null)
                {
                    animator.SetFloat(SpeedParam, 0f);
                    animator.speed = 0.9f + 0.2f * ((list.Count * 37) % 10) / 10f;
                }

                list.Add(new Figure { Go = go, Animator = animator, Index = list.Count, NextSwing = Time.time + list.Count * 0.23f });
            }
        }

        /// <summary>건물 앞쪽(카메라 쪽, -Z) 변을 따라 줄을 세운다. 줄당 5명.</summary>
        private void LineUpInFront(List<Figure> list, BuildingInstance b, int firstRow)
        {
            BuildingDef def;
            if (!_presenter.Runtime.Catalog.TryGet(b.DefId, out def))
                return;

            TerritoryGround ground = _presenter.Ground;
            float cell = ground.Grid.CellSize;
            Vector3 center = ground.FootprintCenterWorld(b.Anchor, def.Footprint, 0f);
            float halfDepth = def.Footprint.Height * cell * 0.5f;
            const int perRow = 5;
            const float gap = 0.8f;

            for (int i = 0; i < list.Count; i++)
            {
                int row = firstRow + i / perRow;
                int col = i % perRow;
                float x = (col - (perRow - 1) * 0.5f) * gap;
                float z = -(halfDepth + 0.6f + row * gap);

                Transform t = list[i].Go.transform;
                t.position = center + new Vector3(x, 0f, z);
                t.rotation = Quaternion.LookRotation(Vector3.back);
            }
        }

        /// <summary>훈련장 위에 흩어 세운다 — 서로 마주 보고 창을 휘두른다.</summary>
        private void Spread(List<Figure> list, BuildingInstance b)
        {
            BuildingDef def;
            if (!_presenter.Runtime.Catalog.TryGet(b.DefId, out def))
                return;

            TerritoryGround ground = _presenter.Ground;
            Vector3 center = ground.FootprintCenterWorld(b.Anchor, def.Footprint, 0f);
            const int perRow = 4;
            const float gap = 1.0f;

            for (int i = 0; i < list.Count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                float x = (col - (perRow - 1) * 0.5f) * gap;
                float z = (row - 0.5f) * gap * 1.4f;

                Transform t = list[i].Go.transform;
                t.position = center + new Vector3(x, 0f, z);
                t.rotation = Quaternion.LookRotation(row % 2 == 0 ? Vector3.forward : Vector3.back);
            }
        }

        private void DrillSwings()
        {
            for (int i = 0; i < _drilling.Count; i++)
            {
                Figure f = _drilling[i];
                if (f.Animator == null || Time.time < f.NextSwing)
                    continue;

                f.Animator.SetInteger(VariantParam, (f.Index + (int)Time.time) % 2);
                f.Animator.SetTrigger(AttackParam);
                f.NextSwing = Time.time + 1.3f + (f.Index % 3) * 0.25f;
            }
        }
    }
}

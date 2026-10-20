using UnityEngine;

namespace Olympus.Game.Territory
{
    /// <summary>
    /// 건물 모델을 자리 크기에 맞춰 앉히는 계산.
    ///
    /// 게임(<see cref="TerritoryPresenter"/>)과 편집·캡처 툴이 **같은 함수**를 쓴다.
    /// 각자 계산하게 두면 편집 툴에서 맞춰 놓은 포즈와 게임에 뜨는 포즈가 갈라지고,
    /// 그건 화면으로만 보면 "아트가 잘못 나왔다"로 보인다. 소유자를 한 곳으로 못 박는다.
    ///
    /// Core가 아니라 Game에 있는 이유 — Transform·Bounds가 UnityEngine 타입이라
    /// Core(noEngineReferences)에 들어갈 수 없다.
    /// </summary>
    public static class BuildingModelPlacement
    {
        /// <summary>
        /// 래퍼를 돌리고·키우고·지면에 앉힌다. 모델 자신의 변환은 건드리지 않는다.
        ///
        /// ⚠️ 모델의 회전을 초기화하면 안 된다. FBX 임포터가 축(Blender Z-up → Unity Y-up)을
        /// 맞추려고 루트에 -90° X 회전을 넣어 두는데, 그걸 지우면 건물이 뒤로 눕는다
        /// (2026-09-11에 밟았다 — 본부가 하늘을 보고 누웠다).
        /// 그래서 래퍼만 움직인다.
        ///
        /// 아트가 1x1x1로 정규화돼 나오지만 그걸 믿고 상수를 곱하지 않는다 — 실제 경계를
        /// 재서 맞춘다. 한 채라도 다른 스케일로 나오면 그 한 채만 조용히 어긋난다.
        /// 비율은 유지한다(균등 배율) — 늘려 맞추면 실루엣이 망가진다.
        /// </summary>
        /// <param name="baseRotation">
        /// 돌리기 전에 깔려 있어야 하는 회전. 게임은 래퍼를 돌리므로 <c>identity</c>지만,
        /// 편집 툴은 모델 자신을 돌리므로 FBX 임포터가 넣어 둔 축 보정 회전을 넘긴다.
        /// 이걸 날리면 건물이 뒤로 눕는다.
        /// </param>
        /// <param name="offset">자리 중앙에서 밀어낼 거리. 눈으로 맞춘 보정값이다.</param>
        /// <returns>적용한 배율. 경계를 못 재면 0.</returns>
        public static float Place(
            Transform target,
            Quaternion baseRotation,
            Vector3 groundCenter,
            Vector3 offset,
            float yawDegrees,
            float targetX,
            float targetZ,
            float scaleMultiplier)
        {
            if (target == null)
                return 0f;

            target.localScale = Vector3.one;
            target.position = Vector3.zero;
            target.rotation = Quaternion.Euler(0f, yawDegrees, 0f) * baseRotation;

            Bounds bounds;
            if (!TryMeasureBounds(target, out bounds))
                return 0f;

            float scale = FitScale(bounds, targetX, targetZ) * scaleMultiplier;

            target.localScale = Vector3.one * scale;
            target.position = GroundedPosition(bounds, groundCenter, offset, scale);

            return scale;
        }

        /// <summary>
        /// <see cref="Place"/>의 **역산**. 씬에서 사람이 돌리고 키워 놓은 것을 저장할 값으로 되돌린다.
        ///
        /// 정방향과 역방향을 같은 파일에 붙여 둔 이유 — 떨어뜨려 두면 한쪽만 고쳐져서
        /// "저장한 것과 다시 띄운 것이 다르다"가 된다. 실제로 그렇게 깨졌다(2026-10-14).
        ///
        /// ⚠️ **위치는 읽지 않는다.** 편집용으로 늘어놓은 자리는 서로 크기를 비교하려고
        /// 쓰는 작업판이라, 거기서 옮긴 것은 "이 건물의 보정"이 아니다. 한 번 읽었다가
        /// 사람이 여러 채를 같이 돌린 것이 전부 보정값으로 들어가 기지가 흩어졌다
        /// (2026-10-14). 칸 안에서의 밀기는 숫자로 따로 준다.
        ///
        /// ⚠️ 사람이 <paramref name="target"/> 자신이 아니라 그 **안쪽 부품**을 만졌으면
        /// 이 역산은 그 부분을 보지 못한다. 부르는 쪽이 되돌려 보고 확인해야 한다.
        /// </summary>
        /// <param name="tiltDegrees">세워 둔 축에서 기울어진 정도. 0이 아니면 저장되지 않는다.</param>
        public static bool TryReadBack(
            Transform target,
            Quaternion baseRotation,
            float baseYawDegrees,
            float targetX,
            float targetZ,
            out float yawDegrees,
            out float scaleMultiplier,
            out float tiltDegrees)
        {
            yawDegrees = 0f;
            scaleMultiplier = 1f;
            tiltDegrees = 0f;

            if (target == null)
                return false;

            // 깔려 있던 회전을 벗겨 내면 사람이 돌린 각도만 남는다.
            Quaternion applied = target.rotation * Quaternion.Inverse(baseRotation);

            // eulerAngles.y를 쓰지 않는다 — 자유 회전이 섞이면 터무니없는 값이 나온다
            // (2026-10-14에 20채가 전부 77.46774°로 저장됐다).
            // 정면이 바닥에서 어느 쪽을 보는지로 각도를 정하면 그런 일이 없다.
            Vector3 forward = applied * Vector3.forward;
            yawDegrees = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg - baseYawDegrees;

            tiltDegrees = Vector3.Angle(applied * Vector3.up, Vector3.up);

            float totalScale = target.localScale.x;

            Vector3 position = target.position;
            Vector3 scale = target.localScale;

            // 경계는 배율 1에서 재야 한다 — 지금 배율로 재면 배율이 두 번 곱해진다.
            target.position = Vector3.zero;
            target.localScale = Vector3.one;

            Bounds bounds;
            bool measured = TryMeasureBounds(target, out bounds);

            target.position = position;
            target.localScale = scale;

            if (!measured)
                return false;

            float fit = FitScale(bounds, targetX, targetZ);

            if (fit <= 0.0001f || totalScale <= 0.0001f)
                return false;

            scaleMultiplier = totalScale / fit;
            return true;
        }

        /// <summary>
        /// 바닥을 지면에 대고 자리 중앙에 세웠을 때의 위치.
        /// 피벗이 바닥이 아니라 경계의 중심이라 잰 경계로 보정해야 한다.
        /// </summary>
        private static Vector3 GroundedPosition(
            Bounds bounds, Vector3 groundCenter, Vector3 offset, float scale)
        {
            return groundCenter + offset
                - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        }

        /// <summary>칸을 꽉 채우는 배율(보정 곱하기 전). 편집 툴이 보정값을 되뽑을 때도 쓴다.</summary>
        public static float FitScale(Bounds bounds, float targetX, float targetZ)
        {
            float scaleX = bounds.size.x > 0.0001f ? targetX / bounds.size.x : 1f;
            float scaleZ = bounds.size.z > 0.0001f ? targetZ / bounds.size.z : 1f;
            return Mathf.Min(scaleX, scaleZ);
        }

        /// <summary>배율 1일 때의 경계. 래퍼 밑의 렌더러를 전부 합친다.</summary>
        public static bool TryMeasureBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();

            if (root == null)
                return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // 월드 경계를 루트 기준으로 되돌린다(부르는 쪽이 위치·배율을 1로 초기화해 둔다).
            bounds.center -= root.position;
            return true;
        }
    }
}

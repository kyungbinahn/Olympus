using System.Linq;
using System.Reflection;
using System.Text;
using Olympus.Game.Territory;
using UnityEngine;
using UnityEngine.UI;

namespace Olympus.Editor
{
    /// <summary>
    /// <see cref="EditorBridge"/>의 call 명령으로 부르는 플레이 중 조작. 사람이 손가락으로 하던 확인
    /// ("건물을 눌러 팝업을 띄운다", "버튼을 누른다")을 바깥에서 재현하려고 둔다.
    ///
    /// 게임 코드에 시험용 공개 메서드를 늘리지 않으려고 에디터 쪽에 두고, 필요한 비공개 경로는 리플렉션으로 탄다 —
    /// 이름이 바뀌면 여기가 "못 찾음"으로 말해 준다(조용히 지나가지 않는다).
    /// </summary>
    internal static class BridgeActions
    {
        /// <summary>기지의 자리 목록 — "번호: 이름 Lv 상태" 한 줄씩.</summary>
        public static string ListSlots()
        {
            TerritoryPresenter p = Presenter();
            var sb = new StringBuilder();
            foreach (var b in p.Runtime.State.Buildings.OrderBy(b => b.Id))
                sb.Append(b.Id).Append(": ").Append(b.DefId).Append(" Lv").Append(b.Level).Append(' ').Append(b.Phase).Append('\n');
            return sb.ToString();
        }

        /// <summary>자리를 탭한 것과 같게 고른다(정보 팝업이 뜬다).</summary>
        public static string SelectSlot(int slotId)
        {
            TerritoryPresenter p = Presenter();
            MethodInfo select = typeof(TerritoryPresenter).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            if (select == null)
                return "TerritoryPresenter.Select를 못 찾음 — 이름이 바뀌었으면 BridgeActions도 고칠 것";

            select.Invoke(p, new object[] { slotId });
            return "선택함: " + slotId + " (지금 선택 " + p.SelectedSlotId + ")";
        }

        /// <summary>이름이 같은 버튼을 누른다. 꺼진 버튼은 안 누른다 — 사람도 못 누르니까.</summary>
        public static string Click(string objectName)
        {
            Button button = Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(b => b.name == objectName);
            if (button == null)
                return "켜진 버튼 중 이름이 '" + objectName + "'인 것이 없음";
            if (!button.interactable)
                return "'" + objectName + "' 버튼이 눌리지 않는 상태";

            button.onClick.Invoke();
            return "눌렀음: " + objectName;
        }

        /// <summary>이름이 <paramref name="prefix"/>로 시작하는 첫 오브젝트로 카메라를 옮긴다(거리는 가까이).</summary>
        public static string Focus(string prefix, float distance)
        {
            GameObject go = FindByPrefix(prefix);
            if (go == null)
                return "'" + prefix + "'로 시작하는 오브젝트가 없음";

            TerritoryCamera cam = Object.FindFirstObjectByType<TerritoryCamera>();
            if (cam == null)
                return "TerritoryCamera가 없음";

            cam.ExtendBounds(go.transform.position);   // 성문 밖 병사도 비출 수 있게
            cam.FocusOn(go.transform.position);
            cam.SetDistance(distance);
            return "카메라를 옮김: " + go.name + " @ " + go.transform.position;
        }

        /// <summary>
        /// 병사 모델 진단 — 이름이 <paramref name="prefix"/>로 시작하는 것마다 한 줄:
        /// 몸이 보는 방향(발끝·어깨, 지금 동작 자세 기준)과 오브젝트 정면(=걷는 방향) 사이 각도,
        /// 그리고 소품이 어느 뼈에 붙어 있고 그 뼈에서 몸 기준으로 어느 쪽(앞/옆/위)에 있는지.
        /// 각도가 180 근처면 뒷걸음질이다.
        /// </summary>
        public static string Probe(string prefix)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (GameObject go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                         .Where(t => t.name.StartsWith(prefix)).Select(t => t.gameObject).Take(4))
            {
                n++;
                Animator a = go.GetComponentInChildren<Animator>();
                Transform root = go.transform;
                sb.Append(go.name).Append(" fwd=").Append(Flat(root.forward));
                if (a == null || !a.isHuman)
                {
                    sb.Append(" (휴머노이드 아님)\n");
                    continue;
                }

                Vector3 toes = Dir(a, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes) + Dir(a, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
                Transform l = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), r = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Vector3 across = r.position - l.position; across.y = 0f;
                Vector3 shoulders = Vector3.Cross(across, Vector3.up);

                sb.Append(" 발끝각=").Append(Angle(toes, root.forward)).Append(" 어깨각=").Append(Angle(shoulders, root.forward));
                sb.Append(" 오른손이 몸의 ").Append(Side(a.GetBoneTransform(HumanBodyBones.RightHand).position - a.transform.position, root)).Append('\n');

                foreach (Transform child in a.GetComponentsInChildren<Transform>())
                {
                    if (!child.name.StartsWith("prop_"))
                        continue;
                    Transform bone = child.parent;
                    Vector3 off = child.position - bone.position;
                    sb.Append("   ").Append(child.name).Append(" 뼈=").Append(bone.name)
                      .Append(" 뼈에서 ").Append(Side(off, root)).Append(" 거리 ").Append(off.magnitude.ToString("0.00"))
                      .Append(" 소품앞=").Append(Side(child.forward, root)).Append(" 소품위=").Append(Side(child.up, root)).Append('\n');
                }
            }

            return n == 0 ? "'" + prefix + "'로 시작하는 켜진 오브젝트가 없음" : sb.ToString();
        }

        /// <summary>
        /// 촬영 부스 — 기지에서 멀리 떨어진 빈 곳에 병사 하나를 세우고 옆·앞·위에서 찍는다.
        /// 병사의 정면(걷는 방향)은 화면 오른쪽(옆 사진) / 카메라 쪽(앞 사진) / 화면 위쪽(위 사진)이다.
        /// <paramref name="speed"/> 1이면 행군 동작, 0이면 대기 동작. <paramref name="seconds"/>만큼 동작을 진행한 자세.
        /// 기지 카메라는 기지 범위 밖을 못 비춰서(성문 밖 병사 등) 따로 둔다.
        /// </summary>
        public static string Booth(string unitId, float speed, float seconds)
        {
            Olympus.Game.Battle.BattleArena arena = Object.FindFirstObjectByType<Olympus.Game.Battle.BattleArena>();
            if (arena == null)
                throw new System.InvalidOperationException("BattleArena가 없음 — 기지나 전투 씬에서 플레이 중이어야 함");

            // Destroy는 프레임 끝에 지운다 — 먼저 끄지 않으면 바로 아래 촬영에 이전 병사가 겹쳐 찍힌다.
            GameObject old = GameObject.Find("BridgeBooth");
            if (old != null)
            {
                old.SetActive(false);
                Object.Destroy(old);
            }

            var stage = new GameObject("BridgeBooth").transform;
            stage.position = new Vector3(500f, 0f, 500f);

            Animator animator;
            GameObject soldier = arena.SpawnDisplay(unitId, true, 0.4f, stage, out animator);
            soldier.transform.localPosition = Vector3.zero;
            soldier.transform.localRotation = Quaternion.identity;   // 정면 = +Z

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.SetFloat("Speed", speed);
                for (float t = 0f; t < seconds; t += 1f / 30f)
                    animator.Update(1f / 30f);
            }

            return BoothShoot(unitId + "_spawn");
        }

        /// <summary>
        /// 부스에 서 있는 병사를 지금 자세 그대로 다시 찍는다 — 세운 직후가 아니라 실제 프레임이 흐른 뒤의 모습
        /// (동작이 정말 걸렸는지, 정면이 그대로인지)을 보려고 Booth와 나눴다. 다 보면 BoothClear.
        /// </summary>
        public static string BoothShoot(string tag)
        {
            GameObject found = GameObject.Find("BridgeBooth");
            if (found == null)
                return "부스가 없음 — 먼저 Booth";
            Transform stage = found.transform;

            {
                float h = 1.8f;
                var shots = new[]
                {
                    // 옆: 병사의 오른쪽(+X)에 서서 본다 → 병사 정면(+Z)이 화면 오른쪽.
                    new { name = "side", pos = new Vector3(4.5f, h * 0.55f, 0f), look = new Vector3(0f, h * 0.55f, 0f) },
                    new { name = "front", pos = new Vector3(0f, h * 0.6f, 4.5f), look = new Vector3(0f, h * 0.55f, 0f) },
                    new { name = "top", pos = new Vector3(0f, 6f, -0.01f), look = new Vector3(0f, 0f, 0f) },
                };

                var saved = new StringBuilder();
                foreach (var s in shots)
                {
                    string path = System.IO.Path.GetFullPath("Temp/EditorBridge/shots/booth_" + tag + "_" + s.name + ".png");
                    Render(stage.TransformPoint(s.pos), stage.TransformPoint(s.look), path);
                    saved.Append(path).Append('\n');
                }

                return saved.ToString();
            }
        }

        public static string BoothClear()
        {
            GameObject found = GameObject.Find("BridgeBooth");
            if (found != null)
                Object.Destroy(found);
            return "부스 치움";
        }

        private static void Render(Vector3 from, Vector3 at, string path)
        {
            var go = new GameObject("BridgeBoothCamera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.transform.position = from;
                cam.transform.LookAt(at, Vector3.up);
                cam.fieldOfView = 35f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.82f, 0.84f, 0.86f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50f;

                var rt = new RenderTexture(640, 640, 24);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 640, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

                cam.targetTexture = null;
                Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        /// <summary>동작 장치 상태 — 동작이 안 걸릴 때 어디서 막혔는지 본다.</summary>
        public static string AnimInfo(string prefix)
        {
            var sb = new StringBuilder();
            foreach (Animator a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform t = a.transform;
                while (t.parent != null && !t.name.StartsWith(prefix))
                    t = t.parent;
                if (!t.name.StartsWith(prefix))
                    continue;

                sb.Append(t.name).Append('/').Append(a.name)
                  .Append(" 켜짐=").Append(a.isActiveAndEnabled)
                  .Append(" 준비=").Append(a.isInitialized)
                  .Append(" 사람형=").Append(a.isHuman)
                  .Append(" 아바타=").Append(a.avatar != null ? a.avatar.name + (a.avatar.isValid ? "" : "(깨짐)") : "없음")
                  .Append(" 컨트롤러=").Append(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "없음")
                  .Append(" 컬링=").Append(a.cullingMode)
                  .Append(" 속도=").Append(a.speed);

                if (a.isInitialized && a.runtimeAnimatorController != null)
                {
                    AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
                    AnimatorClipInfo[] clips = a.GetCurrentAnimatorClipInfo(0);
                    sb.Append(" 상태시간=").Append(s.normalizedTime.ToString("0.00"))
                      .Append(" 클립=").Append(clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "없음")
                      .Append(" Speed=").Append(a.GetFloat("Speed"));
                }
                sb.Append('\n');
                if (sb.Length > 3000)
                    break;
            }
            return sb.Length == 0 ? "없음" : sb.ToString();
        }

        /// <summary>"부모이름/접두사"로 쓰면 그 부모 바로 밑에서만 찾는다(같은 이름의 병사가 여러 곳에 있을 때).</summary>
        private static GameObject FindByPrefix(string prefix)
        {
            string parent = null;
            int slash = prefix.IndexOf('/');
            if (slash > 0)
            {
                parent = prefix.Substring(0, slash);
                prefix = prefix.Substring(slash + 1);
            }

            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(t => t.name.StartsWith(prefix) && (parent == null || (t.parent != null && t.parent.name == parent)))
                .Select(t => t.gameObject).FirstOrDefault();
        }

        private static Vector3 Dir(Animator a, HumanBodyBones from, HumanBodyBones to)
        {
            Transform f = a.GetBoneTransform(from), t = a.GetBoneTransform(to);
            if (f == null || t == null) return Vector3.zero;
            Vector3 d = t.position - f.position; d.y = 0f;
            return d.normalized;
        }

        private static string Angle(Vector3 v, Vector3 forward)
        {
            v.y = 0f; forward.y = 0f;
            return v.sqrMagnitude < 1e-6f ? "?" : Mathf.RoundToInt(Vector3.SignedAngle(forward, v, Vector3.up)) + "°";
        }

        /// <summary>몸 기준 방향을 말로 — 앞/뒤 · 오른쪽/왼쪽 · 위/아래 중 큰 성분 순.</summary>
        private static string Side(Vector3 v, Transform root)
        {
            float f = Vector3.Dot(v, root.forward), r = Vector3.Dot(v, root.right), u = Vector3.Dot(v, root.up);
            return "(앞" + f.ToString("+0.00;-0.00") + " 오른" + r.ToString("+0.00;-0.00") + " 위" + u.ToString("+0.00;-0.00") + ")";
        }

        private static string Flat(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
        }

        /// <summary>
        /// 모델 파일의 면 방향 검사 — 메시마다 "삼각형 감김(앞면) 방향이 중심에서 바깥을 보는 비율"과
        /// "감김 방향과 정점 노말이 어긋난 비율", 그리고 오브젝트 크기에 음수(거울)가 있는지.
        /// 바깥 비율이 낮으면 면이 안팎으로 뒤집혀 안쪽 면이 보인다(방패 테가 두 줄로 보이는 증상).
        /// </summary>
        public static string MeshInfo(string assetPath)
        {
            var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (root == null)
                return "모델 없음: " + assetPath;

            var sb = new StringBuilder();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;

                Vector3[] v = m.vertices;
                Vector3[] n = m.normals;
                int[] t = m.triangles;
                Vector3 c = m.bounds.center;
                int outward = 0, mismatch = 0, tris = t.Length / 3;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                    Vector3 fn = Vector3.Cross(b - a, d - a);   // Unity 앞면 = 시계 방향 감김
                    Vector3 mid = (a + b + d) / 3f;
                    if (Vector3.Dot(fn, mid - c) > 0f) outward++;
                    if (n.Length == v.Length && Vector3.Dot(fn, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < 0f) mismatch++;
                }

                // 감김 일관성 — 이웃 삼각형이 공유한 모서리를 같은 방향으로 지나면 둘 중 하나가 뒤집힌 것이다.
                // 위치로 정점을 합쳐서 본다(UV 이음매로 정점이 쪼개져 있어도 같은 모서리로 센다).
                var key = new System.Collections.Generic.Dictionary<Vector3, int>();
                int[] id = new int[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 q = new Vector3(Mathf.Round(v[i].x * 1e4f), Mathf.Round(v[i].y * 1e4f), Mathf.Round(v[i].z * 1e4f));
                    int k;
                    if (!key.TryGetValue(q, out k)) { k = key.Count; key[q] = k; }
                    id[i] = k;
                }
                var directed = new System.Collections.Generic.Dictionary<long, int>();
                for (int i = 0; i < t.Length; i += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        long ek = ((long)id[t[i + e]] << 32) | (uint)id[t[i + (e + 1) % 3]];
                        directed[ek] = directed.TryGetValue(ek, out int cnt) ? cnt + 1 : 1;
                    }
                int same = 0;
                foreach (var kv in directed)
                    if (kv.Value > 1) same += kv.Value - 1;

                Vector3 s = mf.transform.lossyScale;
                sb.Append(mf.name).Append(": 감김이 어긋난 모서리 ").Append(same).Append(", 삼각형 ").Append(tris)
                  .Append(", 바깥을 보는 면 ").Append((100f * outward / Mathf.Max(1, tris)).ToString("0")).Append('%')
                  .Append(", 노말 어긋남 ").Append((100f * mismatch / Mathf.Max(1, tris)).ToString("0")).Append('%')
                  .Append(", 크기 ").Append(s.ToString("0.###"))
                  .Append(s.x * s.y * s.z < 0f ? " (거울 — 면이 뒤집혀 그려진다)" : "").Append('\n');
            }

            return sb.Length > 0 ? sb.ToString() : "메시 없음";
        }

        /// <summary>에셋 하나를 다시 가져온다(가져오기 규칙을 고친 뒤). 플레이 중에는 안 된다.</summary>
        public static string Reimport(string path)
        {
            if (Application.isPlaying)
                return "플레이 중에는 다시 가져오지 않음 — 먼저 stop";
            UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
            return "다시 가져옴: " + path;
        }

        /// <summary>
        /// 본부를 <paramref name="hqLevel"/>까지 밀어 올린다(자원 지급 → 복구·업그레이드 → 공사 즉시 완료 → 임무 받기를 반복).
        /// 본부 레벨이 올라야 켜지는 HUD(탭·식량·금·위협 띠)를 화면으로 확인하려고 둔다. 시험 봇(RealDataTests)과 같은 순서다.
        /// ⚠️ 로컬 세이브에 그대로 남는다 — 형님 진행을 지키려면 부르기 전에 olympus-save.json을 백업해 두고 끝나면 되돌린다.
        /// </summary>
        public static string RushHq(int hqLevel)
        {
            Olympus.Core.Territory.TerritoryRuntime rt = Presenter().Runtime;
            for (int round = 0; round < 200 && rt.HqLevel < hqLevel; round++)
            {
                rt.DebugGrantResources(50000);
                foreach (var b in rt.State.Buildings.OrderBy(b => b.Id))
                {
                    if (b.IsRuined)
                        rt.Construction.TryStartRepair(b.Id);
                    else
                        rt.Upgrades.TryStart(b.Id);
                }
                rt.DebugCompleteAllConstruction();
                while (rt.Quests.TryClaim() == Olympus.Core.Progression.QuestClaimRejection.None) { }

                // 본부 Lv.2부터는 성문 밖 기지를 무너뜨려야 열린다 — 시간이 걸리는 일이라 공격 가능한 기지 하나를 바로 넘긴다.
                Olympus.Core.Chapter.FrontView front = rt.Frontline.Describe();
                if (front.HasOutpost && !front.Waiting)
                    rt.Store.Apply(new Olympus.Core.State.StateDelta()
                        .WithCounter(Olympus.Core.State.CountChange.Absolute(Olympus.Core.Chapter.FrontlineService.IndexKey, front.Index + 1))
                        .WithCounter(Olympus.Core.State.CountChange.Absolute(Olympus.Core.Chapter.FrontlineService.DamageKey, 0)));

                // 본부 Lv.4부터는 거인을 무찔러야 열린다 — 용사를 채우고 바로 도착시켜 Core에서 결판낸다(협곡 연출은 건너뜀).
                var threat = rt.Threats.Current;
                if (threat != null && rt.Threats.Describe(threat).Phase != Olympus.Core.Chapter.ThreatPhase.Hidden)
                {
                    rt.DebugGrantTroops(300);
                    rt.DebugThreatArriveNow();
                    rt.Tick();
                    if (rt.Threats.CanDispatch(threat.ThreatId) == Olympus.Core.Chapter.ThreatRejection.None)
                        rt.Threats.Resolve(threat.ThreatId);
                }
            }
            // 훈련 칸(왼쪽 열)도 보이게 몇 명 훈련을 걸어 둔다. 병영이 없으면 거절되고 그만이다.
            rt.Training.TryStart(10);
            return "본부 Lv." + rt.HqLevel;
        }

        private static TerritoryPresenter Presenter()
        {
            if (!Application.isPlaying)
                throw new System.InvalidOperationException("플레이 중에만 됩니다");

            TerritoryPresenter p = Object.FindFirstObjectByType<TerritoryPresenter>();
            if (p == null || p.Runtime == null)
                throw new System.InvalidOperationException("기지(TerritoryPresenter)가 없음 — 기지 씬에서 플레이 중인지 확인");
            return p;
        }
    }
}

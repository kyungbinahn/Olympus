using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Olympus.Game.Battle;

namespace Olympus.Editor
{
    /// <summary>
    /// 전투 병사에 실제 모델(Quaternius, CC0)과 동작을 붙인다. <see cref="BattleSceneBuilder"/>가 부른다.
    ///
    /// 만드는 것(전부 코드가 짓고 다시 지을 수 있다):
    /// - <c>Soldier.mat</c> — 몸 텍스처를 입힌 URP Lit. 진영 색은 화면에서 덧칠한다.
    /// - 애니메이터 3벌 — 대기·행군·공격·쓰러짐. 공격만 다르다(검 / 주먹·몽둥이 / 던지기).
    ///   <see cref="BattlePresenter"/>가 보내는 신호 이름(Speed·Attack·Die)과 맞춘다.
    ///
    /// 모델 파일이 없으면 아무것도 하지 않는다 — 그때는 도형 병사로 선다.
    /// </summary>
    internal static class BattleSoldierSetup
    {
        private const string Folder = "Assets/Art/Characters/Quaternius";
        private const string BodyPath = Folder + "/Superhero_Male_FullBody.fbx";
        private const string AnimPath = Folder + "/UAL1_Standard.fbx";
        private const string TexturePath = Folder + "/T_Superhero_Male_Dark.png";


        /// <summary>
        /// 아군 팀색 — 영화 300의 붉은 망토. 거인은 피부·용암색이라 붉어도 섞이지 않는다.
        /// 마스크(볏·망토) 영역에만 들어간다.
        /// </summary>
        private static readonly Color PlayerTeamColor = new Color(0.72f, 0.10f, 0.08f);
        private static readonly Color EnemyTeamColor = new Color(0.16f, 0.22f, 0.48f);

        [MenuItem("Olympus/Setup/전투 병사 모델 다시 연결", false, 121)]
        private static void FromMenu()
        {
            BattleArena[] arenas = Object.FindObjectsByType<BattleArena>(FindObjectsSortMode.None);
            if (arenas.Length == 0)
            {
                EditorUtility.DisplayDialog("전투 병사 모델", "전투 씬이나 기지 씬을 먼저 여세요.", "확인");
                return;
            }

            foreach (BattleArena arena in arenas)
            {
                Wire(arena);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(arena.gameObject.scene);
            }
        }

        public static void Wire(BattleArena arena)
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
            if (body == null)
            {
                Debug.Log("병사 모델이 없습니다(" + BodyPath + ") — 도형 병사로 섭니다.");
                return;
            }

            List<AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(AnimPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToList();

            if (clips.Count == 0)
            {
                Debug.LogWarning("동작 파일에서 클립을 못 찾았습니다: " + AnimPath + " — 모델은 서지만 움직이지 않습니다.");
            }

            Material material = CreateOrLoadMaterial();

            // 공격 동작은 둘씩 — 병사가 번갈아 쓴다(BattleUnitView가 Variant를 0/1로 보낸다). 창병의 찌르기는 잽이 가장 가깝다.
            RuntimeAnimatorController melee = BuildController("Soldier_Melee", clips, "Sword_Attack", "Punch_Jab");
            RuntimeAnimatorController brute = BuildController("Soldier_Brute", clips, "Punch_Cross", "Punch_Jab");
            RuntimeAnimatorController ranged = BuildController("Soldier_Ranged", clips, "Spell_Simple_Shoot", "Spell_Simple_Shoot");

            var so = new SerializedObject(arena);
            SerializedProperty list = so.FindProperty("_unitModels");
            list.arraySize = 0;

            // 아트 납품(2026-10-22 v2, 새 컨셉 화풍). 파일이 없으면 맨몸 임시 모델로 선다.
            // 무기 소품은 전부 "오른손에 쥐고 T포즈에서 앞을 향함" — 창과 같은 소켓이다(납품 규칙: 피벗 = 쥐는 점, 날 = +Z).
            if (!AddDelivered(list, "hoplite", "Hoplite", "hoplite", melee,
                    ("prop_spear", PropSocket.SpearRightHand), ("prop_aspis", PropSocket.ShieldLeftForearm)))
                Add(list, "hoplite", body, melee, material);

            Add(list, "archer", body, ranged, material);

            // 거인 몹과 1장 "300 vs 1"의 거인은 같은 몸이다(키는 판정 반지름에서 — 몹 약 5m, 1장 거인 약 7m). 뽑은 나무 몽둥이.
            if (!AddDelivered(list, "giant_brute", "Giant", "giant_brute", brute, ("prop_club", PropSocket.SpearRightHand)))
                Add(list, "giant_brute", body, brute, material);
            if (!AddDelivered(list, "giant_chieftain", "Giant", "giant_brute", brute, ("prop_club", PropSocket.SpearRightHand)))
                Add(list, "giant_chieftain", body, brute, material);

            // 돌팔매 거인 = 바위 던지는 키클롭스. 바위는 두 손으로 드는 것이지만 지금은 오른손 소켓에 단다.
            if (!AddDelivered(list, "giant_slinger", "CyclopsThrower", "cyclops_thrower", ranged, ("prop_boulder", PropSocket.SpearRightHand)))
                Add(list, "giant_slinger", body, ranged, material);

            // 보스 키클롭스 — 맨손으로 납품됐다. 거인 몽둥이를 같이 쥐여 준다(몸에 맞춰 같이 커진다).
            if (!AddDelivered(list, "cyclops", "CyclopsBoss", "cyclops_boss", brute, ("../Giant/prop_club", PropSocket.SpearRightHand)))
                Add(list, "cyclops", body, brute, material);

            // 도적은 두 몸을 섞는다(형님 결정 — 덩치 큰 두목형 + 마른 뾰족 두건). 같은 unitId 칸이 둘이면
            // 화면이 병사 번호로 번갈아 고른다(BattleArena.ModelFor). 납품이 없으면 칸을 비워 도형 병사로 선다.
            AddDelivered(list, "bandit", "Bandit", "bandit", melee, ("prop_bandit_cleaver", PropSocket.SpearRightHand));
            AddDelivered(list, "bandit", "BanditB", "bandit_b", melee, ("prop_bandit_dagger", PropSocket.SpearRightHand));

            // 아군 = 300의 붉은색(호플리테스 팀색과 맞춘다), 적 = 돌빛 푸른색. 임시 몸(궁수·거인)에 덧칠되는 색이다.
            SerializedProperty playerTint = so.FindProperty("_playerTint");
            SerializedProperty enemyTint = so.FindProperty("_enemyTint");
            if (playerTint != null) playerTint.colorValue = new Color(1f, 0.55f, 0.50f);
            if (enemyTint != null) enemyTint.colorValue = new Color(0.60f, 0.70f, 0.90f);

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("전투 병사 모델을 연결했습니다 — 클립 " + clips.Count + "개 중 대기·행군·공격·쓰러짐을 씁니다.");
        }

        private const string CharactersRoot = "Assets/Art/Characters";

        /// <summary>
        /// 아트 세션 납품 한 벌을 붙인다 — 몸 + 소품, 진영별 팀색 판. 몸 파일이 없으면 false.
        /// 정면은 화면이 발끝으로 재서 맞추므로 돌리지 않는다(yaw 0). 키는 판정 반지름에서 나온다(scale 1).
        /// 소품 이름에 "../폴더/"를 붙이면 다른 납품 폴더의 소품을 빌린다.
        /// </summary>
        private static bool AddDelivered(SerializedProperty list, string unitId, string folderName, string baseName,
            RuntimeAnimatorController controller, params (string name, PropSocket socket)[] props)
        {
            string folder = CharactersRoot + "/" + folderName;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + baseName + ".fbx");
            if (model == null)
                return false;

            Material[] bodyMats = TeamMaterials(folder, baseName);

            int i = list.arraySize;
            list.arraySize = i + 1;
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("unitId").stringValue = unitId;
            e.FindPropertyRelative("model").objectReferenceValue = model;
            e.FindPropertyRelative("controller").objectReferenceValue = controller;
            e.FindPropertyRelative("material").objectReferenceValue = bodyMats[0];
            e.FindPropertyRelative("enemyMaterial").objectReferenceValue = bodyMats[1];
            e.FindPropertyRelative("scale").floatValue = 1f;
            e.FindPropertyRelative("yaw").floatValue = 0f;

            SerializedProperty propList = e.FindPropertyRelative("props");
            propList.arraySize = 0;
            var attached = new List<string>();
            foreach ((string name, PropSocket socket) in props)
            {
                string propFolder = folder;
                string propName = name;
                if (name.StartsWith("../"))
                {
                    int slash = name.LastIndexOf('/');
                    propFolder = CharactersRoot + "/" + name.Substring(3, slash - 3);
                    propName = name.Substring(slash + 1);
                }

                GameObject prop = AssetDatabase.LoadAssetAtPath<GameObject>(propFolder + "/" + propName + ".fbx");
                if (prop == null)
                    continue;

                AddProp(propList, prop, propFolder, propName, socket);
                attached.Add(propName);
            }

            Debug.Log("아트 납품 모델을 연결했습니다 — " + unitId + " (" + baseName + ")" +
                      (attached.Count > 0 ? ", 소품 " + string.Join(", ", attached) : ""));
            return true;
        }

        private static void AddProp(SerializedProperty props, GameObject model, string folder, string baseName, PropSocket socket)
        {
            Material[] mats = TeamMaterials(folder, baseName);
            int i = props.arraySize;
            props.arraySize = i + 1;
            SerializedProperty p = props.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("model").objectReferenceValue = model;
            p.FindPropertyRelative("material").objectReferenceValue = mats[0];
            p.FindPropertyRelative("enemyMaterial").objectReferenceValue = mats[1];
            p.FindPropertyRelative("socket").enumValueIndex = (int)socket;
        }

        /// <summary>
        /// 텍스처 + 팀색 마스크로 진영별 판 두 장을 굽고, 그걸 입힌 머티리얼 두 개를 돌려준다([0]=아군, [1]=적).
        /// 실행 중에 색을 섞지 않고 미리 굽는 이유 — URP 기본 셰이더에 마스크 칸이 없고, 셰이더를 새로 만드는 것보다
        /// 텍스처 두 장이 훨씬 단순하다. 납품 파일이 바뀌면 이 메뉴를 다시 누르면 된다.
        /// </summary>
        private static Material[] TeamMaterials(string folder, string baseName)
        {
            string basePath = folder + "/" + baseName + "_basecolor.png";
            string maskPath = folder + "/" + baseName + "_teammask.png";
            string outFolder = folder + "/Generated";

            if (!AssetDatabase.IsValidFolder(outFolder))
                AssetDatabase.CreateFolder(folder, "Generated");

            var result = new Material[2];
            Color[] colors = { PlayerTeamColor, EnemyTeamColor };
            string[] suffix = { "_player", "_enemy" };

            for (int t = 0; t < 2; t++)
            {
                string texOut = outFolder + "/" + baseName + suffix[t] + ".png";
                BakeTeamTexture(basePath, maskPath, colors[t], texOut);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texOut);

                string matPath = outFolder + "/" + baseName + suffix[t] + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    mat = new Material(shader) { name = baseName + suffix[t] };
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                mat.mainTexture = tex;
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
                EditorUtility.SetDirty(mat);

                result[t] = mat;
            }

            return result;
        }

        /// <summary>결과 = 원색 × (마스크가 흰 곳만 팀색). 마스크가 없으면 원색 그대로.</summary>
        private static void BakeTeamTexture(string basePath, string maskPath, Color team, string outPath)
        {
            var baseTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            baseTex.LoadImage(File.ReadAllBytes(basePath));

            Texture2D maskTex = null;
            if (File.Exists(maskPath))
            {
                maskTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                maskTex.LoadImage(File.ReadAllBytes(maskPath));
            }

            int w = baseTex.width, h = baseTex.height;
            Color[] px = baseTex.GetPixels();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float m = maskTex == null ? 0f
                        : maskTex.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r;

                    int i = y * w + x;
                    Color c = px[i];
                    Color tinted = new Color(c.r * team.r, c.g * team.g, c.b * team.b, c.a);
                    px[i] = Color.Lerp(c, tinted, m);
                }
            }

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels(px);
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath);

            Object.DestroyImmediate(baseTex);
            Object.DestroyImmediate(outTex);
            if (maskTex != null) Object.DestroyImmediate(maskTex);
        }

        private static void Add(SerializedProperty list, string unitId, GameObject model,
            RuntimeAnimatorController controller, Material material)
        {
            int i = list.arraySize;
            list.arraySize = i + 1;
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("unitId").stringValue = unitId;
            e.FindPropertyRelative("model").objectReferenceValue = model;
            e.FindPropertyRelative("controller").objectReferenceValue = controller;
            e.FindPropertyRelative("material").objectReferenceValue = material;
            e.FindPropertyRelative("scale").floatValue = 1f;

            // 배열을 늘리면 Unity가 바로 앞 칸을 통째로 복사한다 — 안 비우면 궁수가 호플리테스의 창·방패와
            // 적 판(거인 텍스처)을 물려받는다(2026-10-22 실제로 그렇게 보였다).
            e.FindPropertyRelative("enemyMaterial").objectReferenceValue = null;
            e.FindPropertyRelative("props").arraySize = 0;

            // 정면은 화면이 어깨로 재서 맞춘다(BattlePresenter.FaceForward) — 손 보정은 없다.
            e.FindPropertyRelative("yaw").floatValue = 0f;
        }

        private static Material CreateOrLoadMaterial()
        {
            string path = Folder + "/Soldier.mat";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = "Soldier" };
                AssetDatabase.CreateAsset(material, path);
            }

            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                material.mainTexture = texture;
            }

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.15f);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 대기 ↔ 행군(Speed), 아무 때나 공격(Attack) 뒤 대기로, 아무 때나 쓰러짐(Die, 끝에서 멈춤).
        /// 매번 내용을 새로 짓는다 — 손으로 고칠 값이 없고, 클립 이름이 바뀌면 다시 지으면 된다.
        ///
        /// ⚠️ 파일을 지우고 새로 만들지 않는다. 기지 씬은 이 연결을 무대 둘(Chapter·Raids)에 연달아 하는데,
        /// 지웠다 만들면 두 번째 연결이 첫 번째 무대가 물고 있던 컨트롤러를 지워 그 무대 병사가 전부 동작 없이
        /// T포즈로 미끄러졌다(2026-10-26 — 씬 파일에 controller: {fileID: 0}로 저장돼 있었다).
        /// 같은 자산을 비우고 다시 채우면 물고 있는 참조가 그대로 산다.
        /// </summary>
        private static RuntimeAnimatorController BuildController(string name, List<AnimationClip> clips,
            string attackClip, string attackClipB)
        {
            string path = Folder + "/" + name + ".controller";

            AnimatorController c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null)
            {
                c = AnimatorController.CreateAnimatorControllerAtPath(path);
            }
            else
            {
                c.parameters = new AnimatorControllerParameter[0];
                AnimatorStateMachine old = c.layers[0].stateMachine;
                foreach (AnimatorStateTransition t in old.anyStateTransitions)
                    old.RemoveAnyStateTransition(t);
                foreach (ChildAnimatorState s in old.states)
                    old.RemoveState(s.state);
            }

            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Variant", AnimatorControllerParameterType.Int);

            AnimatorStateMachine sm = c.layers[0].stateMachine;

            AnimatorState idle = sm.AddState("Idle");
            idle.motion = Find(clips, "Idle_Loop");

            AnimatorState march = sm.AddState("March");
            march.motion = Find(clips, "Jog_Fwd_Loop") ?? Find(clips, "Walk_Loop");

            AnimatorState attack = AddAttack(sm, "Attack", Find(clips, attackClip), 0);
            AnimatorState attackB = AddAttack(sm, "AttackB", Find(clips, attackClipB) ?? Find(clips, attackClip), 1);

            AnimatorState death = sm.AddState("Death");
            death.motion = Find(clips, "Death01");

            sm.defaultState = idle;

            AnimatorStateTransition toMarch = idle.AddTransition(march);
            toMarch.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed");
            toMarch.hasExitTime = false;
            toMarch.duration = 0.1f;

            AnimatorStateTransition toIdle = march.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.5f, "Speed");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.1f;

            foreach (AnimatorState a in new[] { attack, attackB })
            {
                AnimatorStateTransition back = a.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.1f;
            }

            AnimatorStateTransition die = sm.AddAnyStateTransition(death);
            die.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            die.hasExitTime = false;
            die.duration = 0.05f;
            die.canTransitionToSelf = false;

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        private static AnimatorState AddAttack(AnimatorStateMachine sm, string stateName, Motion motion, int variant)
        {
            AnimatorState state = sm.AddState(stateName);
            state.motion = motion;
            state.speed = 1.4f;   // 공격 간격(1~1.6초)보다 동작이 길면 다음 공격이 중간에 끊긴다.

            AnimatorStateTransition hit = sm.AddAnyStateTransition(state);
            hit.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            hit.AddCondition(AnimatorConditionMode.Equals, variant, "Variant");
            hit.hasExitTime = false;
            hit.duration = 0.05f;
            hit.canTransitionToSelf = true;
            return state;
        }

        private static AnimationClip Find(List<AnimationClip> clips, string name)
        {
            // FBX에 따라 "Armature|Idle_Loop" 처럼 앞에 붙는 것이 있다 — 끝 이름으로 찾는다.
            AnimationClip exact = clips.FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name));
            if (exact != null)
                return exact;

            AnimationClip loose = clips.FirstOrDefault(c => c.name.Contains(name));
            if (loose == null)
                Debug.LogWarning("동작 '" + name + "'을 못 찾았습니다 — 그 상태는 멈춘 자세로 보입니다.");

            return loose;
        }
    }
}

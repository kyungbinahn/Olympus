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

        /// <summary>아트 세션이 만든 호플리테스(2026-10-21 납품). 있으면 hoplite 칸은 이걸로 선다.</summary>
        private const string HopliteFolder = "Assets/Art/Characters/Hoplite";

        /// <summary>아트 세션이 만든 거인 몹(2026-10-21 납품, 키 7.5m). 호플리테스와 뼈 이름이 같다.</summary>
        private const string GiantFolder = "Assets/Art/Characters/Giant";

        /// <summary>
        /// 아군 팀색 — 영화 300의 붉은 망토. 거인은 피부·용암색이라 붉어도 섞이지 않는다.
        /// 마스크(볏·망토) 영역에만 들어간다.
        /// </summary>
        private static readonly Color PlayerTeamColor = new Color(0.72f, 0.10f, 0.08f);
        private static readonly Color EnemyTeamColor = new Color(0.16f, 0.22f, 0.48f);

        [MenuItem("Olympus/Setup/전투 병사 모델 다시 연결", false, 121)]
        private static void FromMenu()
        {
            BattlePresenter presenter = Object.FindFirstObjectByType<BattlePresenter>();
            if (presenter == null)
            {
                EditorUtility.DisplayDialog("전투 병사 모델", "전투 씬을 먼저 여세요(Olympus/Setup/전투 씬 만들기).", "확인");
                return;
            }

            Wire(presenter);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(presenter.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(presenter.gameObject.scene);
        }

        public static void Wire(BattlePresenter presenter)
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

            RuntimeAnimatorController melee = BuildController("Soldier_Melee", clips, "Sword_Attack");
            RuntimeAnimatorController brute = BuildController("Soldier_Brute", clips, "Punch_Cross");
            RuntimeAnimatorController ranged = BuildController("Soldier_Ranged", clips, "Spell_Simple_Shoot");

            var so = new SerializedObject(presenter);
            SerializedProperty list = so.FindProperty("_unitModels");
            list.arraySize = 0;

            if (!AddHoplite(list, melee))
                Add(list, "hoplite", body, melee, material);
            Add(list, "archer", body, ranged, material);
            if (!AddGiant(list, brute))
                Add(list, "giant_brute", body, brute, material);
            Add(list, "giant_slinger", body, ranged, material);
            Add(list, "cyclops", body, brute, material);

            // 아군 = 300의 붉은색(호플리테스 팀색과 맞춘다), 적 = 돌빛 푸른색. 임시 몸(궁수·거인)에 덧칠되는 색이다.
            SerializedProperty playerTint = so.FindProperty("_playerTint");
            SerializedProperty enemyTint = so.FindProperty("_enemyTint");
            if (playerTint != null) playerTint.colorValue = new Color(1f, 0.55f, 0.50f);
            if (enemyTint != null) enemyTint.colorValue = new Color(0.60f, 0.70f, 0.90f);

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("전투 병사 모델을 연결했습니다 — 클립 " + clips.Count + "개 중 대기·행군·공격·쓰러짐을 씁니다.");
        }

        /// <summary>
        /// 아트 세션의 호플리테스를 붙인다 — 몸·창·방패, 진영별 팀색 판. 파일이 없으면 false(임시 몸으로 선다).
        /// 정면이 처음부터 Unity +Z라 돌리지 않는다(yaw 0).
        /// </summary>
        private static bool AddHoplite(SerializedProperty list, RuntimeAnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(HopliteFolder + "/hoplite.fbx");
            if (model == null)
                return false;

            Material[] bodyMats = TeamMaterials(HopliteFolder, "hoplite");
            GameObject spear = AssetDatabase.LoadAssetAtPath<GameObject>(HopliteFolder + "/prop_spear.fbx");
            GameObject shield = AssetDatabase.LoadAssetAtPath<GameObject>(HopliteFolder + "/prop_aspis.fbx");

            int i = list.arraySize;
            list.arraySize = i + 1;
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("unitId").stringValue = "hoplite";
            e.FindPropertyRelative("model").objectReferenceValue = model;
            e.FindPropertyRelative("controller").objectReferenceValue = controller;
            e.FindPropertyRelative("material").objectReferenceValue = bodyMats[0];
            e.FindPropertyRelative("enemyMaterial").objectReferenceValue = bodyMats[1];
            e.FindPropertyRelative("scale").floatValue = 1f;
            e.FindPropertyRelative("yaw").floatValue = 0f;

            SerializedProperty props = e.FindPropertyRelative("props");
            props.arraySize = 0;
            AddProp(props, spear, "prop_spear", PropSocket.SpearRightHand);
            AddProp(props, shield, "prop_aspis", PropSocket.ShieldLeftForearm);

            Debug.Log("호플리테스(아트 납품) 모델을 연결했습니다 — 창 " + (spear != null) + ", 방패 " + (shield != null));
            return true;
        }

        /// <summary>
        /// 아트 세션의 거인 몹을 붙인다. 정면 +Z(yaw 0). 몽둥이는 아직 소품으로 안 나와서 맨손 — 동작도 주먹 치기.
        /// 팀색 마스크가 비어 있다(거인은 적으로만 나온다) — 진영별 판은 같은 원색이 된다.
        /// </summary>
        private static bool AddGiant(SerializedProperty list, RuntimeAnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(GiantFolder + "/giant_brute.fbx");
            if (model == null)
                return false;

            Material[] mats = TeamMaterials(GiantFolder, "giant_brute");

            int i = list.arraySize;
            list.arraySize = i + 1;
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("unitId").stringValue = "giant_brute";
            e.FindPropertyRelative("model").objectReferenceValue = model;
            e.FindPropertyRelative("controller").objectReferenceValue = controller;
            e.FindPropertyRelative("material").objectReferenceValue = mats[0];
            e.FindPropertyRelative("enemyMaterial").objectReferenceValue = mats[1];
            // 키는 판정 반지름(battle.json, 거인 1.1)에서 나온다 — 약 5m, 호플리테스의 2.7배. 센 거인은 배수만큼 더 커진다.
            // 그림만 따로 키우면 판정보다 몸이 커서 떼로 겹쳐 보였다(2026-10-22).
            e.FindPropertyRelative("scale").floatValue = 1f;
            e.FindPropertyRelative("yaw").floatValue = 0f;
            e.FindPropertyRelative("props").arraySize = 0;

            Debug.Log("거인(아트 납품) 모델을 연결했습니다.");
            return true;
        }

        private static void AddProp(SerializedProperty props, GameObject model, string baseName, PropSocket socket)
        {
            if (model == null)
                return;

            Material[] mats = TeamMaterials(HopliteFolder, baseName);
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
        /// 매번 새로 짓는다 — 손으로 고칠 값이 없고, 클립 이름이 바뀌면 다시 지으면 된다.
        /// </summary>
        private static RuntimeAnimatorController BuildController(string name, List<AnimationClip> clips, string attackClip)
        {
            string path = Folder + "/" + name + ".controller";
            AssetDatabase.DeleteAsset(path);

            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = c.layers[0].stateMachine;

            AnimatorState idle = sm.AddState("Idle");
            idle.motion = Find(clips, "Idle_Loop");

            AnimatorState march = sm.AddState("March");
            march.motion = Find(clips, "Jog_Fwd_Loop") ?? Find(clips, "Walk_Loop");

            AnimatorState attack = sm.AddState("Attack");
            attack.motion = Find(clips, attackClip);
            attack.speed = 1.4f;   // 공격 간격(1~1.6초)보다 동작이 길면 다음 공격이 중간에 끊긴다.

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

            AnimatorStateTransition hit = sm.AddAnyStateTransition(attack);
            hit.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            hit.hasExitTime = false;
            hit.duration = 0.05f;
            hit.canTransitionToSelf = true;

            AnimatorStateTransition back = attack.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.1f;

            AnimatorStateTransition die = sm.AddAnyStateTransition(death);
            die.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            die.hasExitTime = false;
            die.duration = 0.05f;
            die.canTransitionToSelf = false;

            AssetDatabase.SaveAssets();
            return c;
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

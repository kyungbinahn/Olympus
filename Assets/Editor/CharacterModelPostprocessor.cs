using UnityEditor;
using UnityEngine;

namespace Olympus.Editor
{
    /// <summary>
    /// <c>Assets/Art/Characters/</c> 아래 FBX의 가져오기 규칙. 병사 몸과 동작 모음(Quaternius)이 같은 뼈대를 쓴다.
    ///
    /// - <b>Humanoid</b>로 가져온다 — 몸과 동작이 다른 파일이어도 Unity가 사람 뼈대로 이어 준다(리타깃).
    ///   Generic으로 두면 뼈 이름이 정확히 같아야 하고, 나중에 다른 몸(그리스 병사)을 넣을 때 동작을 못 물려받는다.
    /// - 머티리얼은 FBX에서 만들지 않는다 — 건물에서 두 번 텍스처가 안 붙은 일이 있어서
    ///   (<c>BuildingModelEntry</c> 주석) 우리가 만든 .mat을 직접 입힌다(<see cref="BattleSoldierSetup"/>).
    /// - 이름에 <c>Loop</c>가 든 동작은 반복으로 표시한다. 기본값이 "한 번"이라 걷다가 멈춘 채로 미끄러진다.
    /// </summary>
    internal sealed class CharacterModelPostprocessor : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Art/Characters/";

        /// <summary>아트 납품 규칙: 소품 파일 이름은 <c>prop_</c>로 시작한다.</summary>
        internal static bool IsProp(string path) => System.IO.Path.GetFileName(path).StartsWith("prop_");

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CharacterFolder))
                return;

            var importer = (ModelImporter)assetImporter;

            // 손에 드는 소품(창·방패)은 뼈대가 없다 — Humanoid로 가져오면 아바타를 못 만들어 오류가 난다.
            if (IsProp(assetPath))
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;

            FixUalMapping(importer);
        }

        /// <summary>
        /// UAL 뼈대(아트 납품 병사 전부 — Quaternius UAL 65본, 이름 같음) 연결표. 자동 연결이 맞게 잡은 거인·키클롭스·도적B와 같다.
        /// 골반은 pelvis다 — 호플리테스·도적은 자동 연결이 발밑 기준 뼈(root)를 골반으로 잡아 두었다.
        /// </summary>
        private static readonly (string bone, string human)[] UalMap =
        {
            ("pelvis", "Hips"), ("spine_01", "Spine"), ("spine_02", "Chest"), ("spine_03", "UpperChest"),
            ("neck_01", "Neck"), ("Head", "Head"),
            ("thigh_l", "LeftUpperLeg"), ("calf_l", "LeftLowerLeg"), ("foot_l", "LeftFoot"), ("ball_l", "LeftToes"),
            ("thigh_r", "RightUpperLeg"), ("calf_r", "RightLowerLeg"), ("foot_r", "RightFoot"), ("ball_r", "RightToes"),
            ("clavicle_l", "LeftShoulder"), ("upperarm_l", "LeftUpperArm"), ("lowerarm_l", "LeftLowerArm"), ("hand_l", "LeftHand"),
            ("clavicle_r", "RightShoulder"), ("upperarm_r", "RightUpperArm"), ("lowerarm_r", "RightLowerArm"), ("hand_r", "RightHand"),
            ("thumb_01_l", "Left Thumb Proximal"), ("thumb_02_l", "Left Thumb Intermediate"), ("thumb_03_l", "Left Thumb Distal"),
            ("index_01_l", "Left Index Proximal"), ("index_02_l", "Left Index Intermediate"), ("index_03_l", "Left Index Distal"),
            ("pinky_01_l", "Left Little Proximal"), ("pinky_02_l", "Left Little Intermediate"), ("pinky_03_l", "Left Little Distal"),
            ("thumb_01_r", "Right Thumb Proximal"), ("thumb_02_r", "Right Thumb Intermediate"), ("thumb_03_r", "Right Thumb Distal"),
            ("index_01_r", "Right Index Proximal"), ("index_02_r", "Right Index Intermediate"), ("index_03_r", "Right Index Distal"),
            ("pinky_01_r", "Right Little Proximal"), ("pinky_02_r", "Right Little Intermediate"), ("pinky_03_r", "Right Little Distal"),
        };

        // 이 부위가 엉뚱한 뼈에 붙었으면 자동 연결이 틀린 것이다.
        private static readonly string[] CoreParts = { "Hips", "Head", "LeftHand", "RightHand", "LeftFoot", "RightFoot", "LeftUpperArm", "RightUpperArm" };

        /// <summary>
        /// Unity 자동 연결은 몸 비율로 뼈를 짐작한다. 도적(키 작고 배낭)에서는 손을 엄지 끝마디로, 엄지를 새끼손가락으로 잡아
        /// 아바타가 깨졌고 — 사람형이 아니게 되어 동작도 무기도 안 붙은 채 T포즈로 서 있었다(2026-10-26, 촬영 부스로 확인).
        /// 뼈 이름이 정해진 UAL이니 짐작이 틀렸을 때만 표대로 바로잡는다(맞게 잡힌 모델은 건드리지 않는다).
        /// 뼈대 목록(skeleton)은 첫 가져오기에서 Unity가 채운 것을 그대로 쓴다 — 처음 들어온 파일은 가져오기가 한 번 더 필요하다.
        /// </summary>
        private static void FixUalMapping(ModelImporter importer)
        {
            HumanDescription d = importer.humanDescription;
            if (d.skeleton == null || d.skeleton.Length == 0)
                return;

            var names = new System.Collections.Generic.HashSet<string>();
            foreach (SkeletonBone b in d.skeleton)
                names.Add(b.name);
            if (!names.Contains("pelvis") || !names.Contains("hand_l") || !names.Contains("hand_r"))
                return;   // UAL 뼈대가 아니다

            var want = new System.Collections.Generic.Dictionary<string, string>();
            foreach ((string ualBone, string ualHuman) in UalMap)
                want[ualHuman] = ualBone;

            bool wrong = false;
            foreach (string part in CoreParts)
            {
                string have = null;
                if (d.human != null)
                {
                    foreach (HumanBone h in d.human)
                    {
                        if (h.humanName == part)
                            have = h.boneName;
                    }
                }
                if (have != want[part])
                    wrong = true;
            }

            if (!wrong)
                return;

            var human = new HumanBone[UalMap.Length];
            for (int i = 0; i < UalMap.Length; i++)
            {
                var limit = new HumanLimit();
                limit.useDefaultValues = true;
                human[i] = new HumanBone { boneName = UalMap[i].bone, humanName = UalMap[i].human, limit = limit };
            }

            d.human = human;
            importer.humanDescription = d;
            UnityEngine.Debug.Log("사람형 뼈대 연결을 UAL 표대로 바로잡았습니다: " + importer.assetPath);
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CharacterFolder) || IsProp(assetPath))
                return;

            var importer = (ModelImporter)assetImporter;

            // 이미 손으로 나눈 클립이 있으면 건드리지 않는다.
            if (importer.clipAnimations != null && importer.clipAnimations.Length > 0)
                return;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool loop = clips[i].name.Contains("Loop") || clips[i].takeName.Contains("Loop");
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;

                // 제자리 동작으로 — 위치는 시뮬레이션이 정한다. 동작이 몸을 옮기면 칸이 어긋난다.
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalPositionXZ = true;
                clips[i].lockRootHeightY = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = true;
            }

            importer.clipAnimations = clips;
        }
    }
}

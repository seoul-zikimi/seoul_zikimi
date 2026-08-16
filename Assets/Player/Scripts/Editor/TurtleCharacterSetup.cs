#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// 거북이 캐릭터 1회 셋업 (Tools ▸ Character ▸ 거북이 캐릭터 생성/갱신):
    /// 1) Assets/Player/Turtle 클립들의 루프 설정(Idle/Walk/Run/Climb)
    /// 2) TurtleAnim.controller 생성 — PlayerAnim과 같은 상태 이름(미러링 계약)
    /// 3) Resources/Characters/char_turtle.prefab 생성 — 달팽이 키에 맞춰 스케일
    /// 4) PlayerUnit 프리팹에 CharacterWearer 부착(인게임 동기화)
    /// walk/run.fbx가 아직 없으면 Idle 클립으로 대체(파일 추가 후 재실행하면 교체됨).
    /// </summary>
    public static class TurtleCharacterSetup
    {
        const string kDir = "Assets/Player/Turtle";
        const string kController = kDir + "/TurtleAnim.controller";
        const string kPrefabDir = "Assets/Resources/Characters";
        const string kPrefab = kPrefabDir + "/char_turtle.prefab";
        const string kPlayerPrefab = "Assets/Player/Prefabs/PlayerUnit.prefab";
        const string kSnailModel = "Assets/Player/Animations/model.fbx";

        // 상태 이름(PlayerAnim.controller와 동일해야 CharacterMirror가 따라감) → 클립 fbx 파일명
        static readonly (string state, string file, bool loop)[] kStates =
        {
            ("Idle",   "Idle",     true),
            ("Walk",   "walk",     true),
            ("Run",    "run",      true),
            ("Jump",   "Jumping",  false),
            ("Throw",  "Throw",    false),
            ("Hammer", "hammer",   true),    // 공정 중 반복 스윙 — 루프
            ("Climb",  "Climbing", true),
        };

        // 클립별 루트 회전 보정(도) — 루트모션을 버리는 구조라 임포터 오프셋은 무효.
        // 사다리 방향 전환은 CharacterMirror가 런타임에 모델을 180° 돌려 처리한다.
        static float RotOffset(string file) => 0f;

        [MenuItem("Tools/Character/거북이 캐릭터 생성·갱신")]
        static void Setup()
        {
            // 1) 클립 임포트 설정(루프) + 수집
            var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
            foreach (var (state, file, loop) in kStates)
            {
                string path = $"{kDir}/{file}.fbx";
                var clip = LoadClip(path, loop, RotOffset(file));
                if (clip == null && state != "Idle")
                {
                    Debug.LogWarning($"[Turtle] {path} 없음 — Idle로 대체(파일 추가 후 메뉴 재실행)");
                    clip = LoadClip($"{kDir}/Idle.fbx", true, 0f);
                }
                if (clip == null) { Debug.LogError($"[Turtle] Idle.fbx도 없음 — {kDir} 확인"); return; }
                clips[state] = clip;
            }

            // 2) 컨트롤러(항상 새로 구성 — 상태 추가/클립 교체 반영)
            AssetDatabase.DeleteAsset(kController);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(kController);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var (state, _, _) in kStates)
            {
                var st = sm.AddState(state);
                st.motion = clips[state];

                // 망치질: 달팽이 망치 클립 한 사이클 동안 거북이가 ~3번 스윙하게 배속(효과음 싱크).
                // 페인트칠도 같은 Hammer 상태를 쓰므로 함께 맞는다.
                if (state == "Hammer")
                {
                    // 달팽이 Hammer 상태는 자체 3배속 → 실효 사이클 = 클립길이/3.
                    // 그 사이클 동안 3스윙: speed = 3 × 거북클립 / (달팽클립/3) = 9 × 비율
                    float snailLen = SnailClipLength("hammer");
                    if (snailLen > 0.05f) st.speed = 9f * clips[state].length / snailLen;
                }
            }

            // 3) 캐릭터 프리팹 — Idle.fbx 모델 + 컨트롤러, 달팽이 키 맞춤 스케일
            var turtleModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/Idle.fbx");
            if (turtleModel == null) { Debug.LogError($"[Turtle] {kDir}/Idle.fbx 없음"); return; }
            if (!AssetDatabase.IsValidFolder(kPrefabDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Characters");

            // 래퍼 루트 밑에 모델을 두고, 달팽이와 같은 키·같은 발 높이로 정렬한다.
            // (원점 규약이 달라 생기는 "걷는데 땅에 묻힘"은 여기서 발 높이를 맞춰 해결)
            var wrapper = new GameObject("char_turtle");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(turtleModel);
            try
            {
                inst.transform.SetParent(wrapper.transform, false);

                var anim = inst.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // 발 높이는 "정지 포즈"가 아니라 실제 Idle 첫 프레임 포즈로 잰다.
                // Animator.Update는 에디트 모드에서 샘플이 안 먹을 수 있어 SampleAnimation으로 확정 샘플.
                clips["Idle"].SampleAnimation(inst, 0f);

                var snailB = ModelBounds(kSnailModel);           // 루트 원점 기준 달팽이 렌더 바운즈
                var turtleB = Bounds(inst);
                if (snailB.size.y > 0.01f && turtleB.size.y > 0.01f)
                {
                    float s = snailB.size.y / turtleB.size.y;
                    inst.transform.localScale = Vector3.one * s;
                    clips["Idle"].SampleAnimation(inst, 0f);
                    turtleB = Bounds(inst);                       // 스케일 반영 재계산
                    // 프리팹 규약: 피벗 = 발바닥(Idle 포즈 min.y = 0).
                    // 지면 앉히기는 CharacterSwap이 런타임에 캡슐 바닥으로 처리한다(맵·지형 무관, 재베이크 불필요).
                    inst.transform.localPosition = new Vector3(0f, -turtleB.min.y, 0f);
                    Debug.Log($"[Turtle] 발바닥 피벗 정렬: scale={s:F3} turtleMinY={turtleB.min.y:F3}");
                }

                PrefabUtility.SaveAsPrefabAsset(wrapper, kPrefab);
            }
            finally { Object.DestroyImmediate(wrapper); }

            // 4) PlayerUnit에 CharacterWearer(널 가드)
            using (var scope = new PrefabUtility.EditPrefabContentsScope(kPlayerPrefab))
            {
                var root = scope.prefabContentsRoot;
                if (root.GetComponent<CharacterWearer>() == null) root.AddComponent<CharacterWearer>();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Turtle] 완료 — {kPrefab}, {kController}, PlayerUnit에 CharacterWearer. " +
                      "마이페이지 캐릭터 탭에서 선택 가능.");
        }

        // fbx의 메인 클립 로드 + 루프 설정(임포터 세팅 변경 시 재임포트)
        // 달팽이 쪽 클립 길이(배속 동기 기준). 없으면 0.
        static float SnailClipLength(string file)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"Assets/Player/Animations/{file}.fbx"))
                if (a is AnimationClip c && !c.name.StartsWith("__preview")) return c.length;
            return 0f;
        }

        static AnimationClip LoadClip(string fbxPath, bool loop, float rotDeg)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return null;

            // 전 fbx 임포트 기준 통일 — Legacy는 컨트롤러에 못 쓰고,
            // useFileScale이 파일마다 다르면 포지션 커브 스케일이 어긋나 힙이 바닥으로 붕괴한다(땅꺼짐)
            if (importer.animationType != ModelImporterAnimationType.Generic || !importer.useFileScale)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.useFileScale = true;
                importer.SaveAndReimport();
            }

            var clipsInfo = importer.defaultClipAnimations;
            if (clipsInfo.Length > 0)
            {
                bool dirty = false;
                var overrides = importer.clipAnimations is { Length: > 0 } ? importer.clipAnimations : clipsInfo;
                foreach (var c in overrides)
                {
                    if (c.loopTime != loop) { c.loopTime = loop; dirty = true; }
                    if (!Mathf.Approximately(c.rotationOffset, rotDeg)) { c.rotationOffset = rotDeg; dirty = true; }
                    // 루트 이동·회전을 포즈에 굽기 — 이동은 게임 코드 담당이라 클립은 완전 제자리여야
                    // (믹사모 클립 루트 모션이 남아 있으면 걷는 동안 몸이 땅으로 꺼진다)
                    if (!c.lockRootPositionXZ || !c.lockRootHeightY || !c.lockRootRotation ||
                        !c.keepOriginalPositionXZ || !c.keepOriginalPositionY || !c.keepOriginalOrientation)
                    {
                        c.lockRootPositionXZ = true;  c.keepOriginalPositionXZ = true;
                        c.lockRootHeightY = true;     c.keepOriginalPositionY = true;
                        c.lockRootRotation = true;    c.keepOriginalOrientation = true;
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    importer.clipAnimations = overrides;
                    importer.SaveAndReimport();
                }
            }

            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (a is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    return clip;
            return null;
        }

        static Bounds ModelBounds(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return new Bounds();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                inst.transform.position = Vector3.zero;   // 원점 기준 바운즈(발 높이 비교용)
                return Bounds(inst);
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static Bounds Bounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
#endif

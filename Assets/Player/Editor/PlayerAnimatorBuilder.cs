using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// Mixamo FBX(모델 + 7클립)를 Humanoid로 임포트하고 Animator Controller를 자동 생성한다.
    /// Tools ▸ Player ▸ Setup Animations 메뉴 한 번 실행.
    /// 스킴(단일 레이어): Idle/Walk/Run/Jump/Climb + Hammer(공정) + Throw(던지기).
    /// 손에 든 건 머리 위 비주얼(RebuildHeldVisual) 그대로 — carry/putdown 애니 없음.
    /// 파라미터는 PlayerAnimator.cs가 구동: Speed/Grounded/Climbing/Processing/Throw.
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        const string kDir   = "Assets/Player/Animations";
        const string kModel = kDir + "/model.fbx";
        const string kCtrl  = kDir + "/PlayerAnim.controller";

        // 상태 → (fbx, 루프)
        static readonly (string state, string fbx, bool loop)[] kClips =
        {
            ("Idle",   "Idle.fbx",     true),
            ("Walk",   "walk.fbx",     true),
            ("Run",    "run.fbx",      true),
            ("Jump",   "Jumping.fbx",  false),
            ("Climb",  "Climbing.fbx", true),
            ("Hammer", "hammer.fbx",   true),
            ("Throw",  "Throw.fbx",    false),
        };

        const float kRunSpeed  = 5.5f;   // 이 속도 이상이면 Run(스프린트)
        const float kWalkSpeed = 0.2f;

        [MenuItem("Tools/Player/Setup Animations (Mixamo)")]
        public static void Setup()
        {
            // ── 1) 임포트: 모델=Humanoid(자체 아바타), 클립=Humanoid(모델 아바타로 리타게팅) ──
            ConfigureModel(kModel);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(kModel).OfType<Avatar>().FirstOrDefault();
            if (avatar == null) { Debug.LogError($"[AnimSetup] {kModel} 에서 Humanoid Avatar를 못 찾음. model.fbx가 Mixamo Humanoid인지 확인."); return; }

            foreach (var (_, fbx, loop) in kClips)
                ConfigureClipFbx($"{kDir}/{fbx}", avatar, loop);

            // ── 2) 컨트롤러 생성 ──
            var controller = AnimatorController.CreateAnimatorControllerAtPath(kCtrl);
            controller.AddParameter("Speed",      AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded",   AnimatorControllerParameterType.Bool);
            controller.AddParameter("Climbing",   AnimatorControllerParameterType.Bool);
            controller.AddParameter("Processing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Throw",      AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var st = kClips.ToDictionary(c => c.state, c => MakeState(sm, c.state, LoadClip($"{kDir}/{c.fbx}")));
            sm.defaultState = st["Idle"];
            st["Hammer"].speed = 3f;   // 공정(망치) 애니 3배속

            // 이동: Idle ↔ Walk ↔ Run (Speed)
            Cond(st["Idle"].AddTransition(st["Walk"]), ("Speed", AnimatorConditionMode.Greater, kWalkSpeed));
            Cond(st["Walk"].AddTransition(st["Idle"]), ("Speed", AnimatorConditionMode.Less,    kWalkSpeed));
            Cond(st["Walk"].AddTransition(st["Run"]),  ("Speed", AnimatorConditionMode.Greater, kRunSpeed));
            Cond(st["Run"].AddTransition(st["Walk"]),  ("Speed", AnimatorConditionMode.Less,    kRunSpeed));
            Cond(st["Idle"].AddTransition(st["Run"]),  ("Speed", AnimatorConditionMode.Greater, kRunSpeed));

            // 점프: AnyState → Jump (공중 + 기어오르기 아님), Jump → Idle (착지)
            Cond(AnyTo(sm, st["Jump"]), ("Grounded", AnimatorConditionMode.IfNot, 0f), ("Climbing", AnimatorConditionMode.IfNot, 0f));
            Cond(st["Jump"].AddTransition(st["Idle"]), ("Grounded", AnimatorConditionMode.If, 0f));

            // 기어오르기
            Cond(AnyTo(sm, st["Climb"]), ("Climbing", AnimatorConditionMode.If, 0f));
            Cond(st["Climb"].AddTransition(st["Idle"]), ("Climbing", AnimatorConditionMode.IfNot, 0f));

            // 공정(망치)
            Cond(AnyTo(sm, st["Hammer"]), ("Processing", AnimatorConditionMode.If, 0f));
            Cond(st["Hammer"].AddTransition(st["Idle"]), ("Processing", AnimatorConditionMode.IfNot, 0f));

            // 던지기(트리거) → 클립 끝나면 Idle
            Cond(AnyTo(sm, st["Throw"]), ("Throw", AnimatorConditionMode.If, 0f));
            var tOut = st["Throw"].AddTransition(st["Idle"]); tOut.hasExitTime = true; tOut.exitTime = 0.9f; tOut.duration = 0.1f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = controller;
            Debug.Log($"[AnimSetup] 완료 → {kCtrl}\n다음: 플레이어 프리팹 아래 model.fbx를 비주얼로 넣고, 그 오브젝트(또는 자식)에 Animator 추가 후 이 컨트롤러 할당. (PlayerAnimator가 자식 Animator 자동으로 잡음)");
        }

        // ── helpers ──
        static AnimatorState MakeState(AnimatorStateMachine sm, string name, Motion clip)
        {
            var s = sm.AddState(name);
            s.motion = clip;
            if (clip == null) Debug.LogWarning($"[AnimSetup] {name} 클립 없음 — {kDir} 임포트 확인");
            return s;
        }

        static AnimatorStateTransition AnyTo(AnimatorStateMachine sm, AnimatorState to)
        {
            var t = sm.AddAnyStateTransition(to);
            t.canTransitionToSelf = false;
            return t;
        }

        static void Cond(AnimatorStateTransition t, params (string p, AnimatorConditionMode mode, float th)[] conds)
        {
            t.hasExitTime = false;
            t.duration = 0.08f;
            foreach (var c in conds) t.AddCondition(c.mode, c.th, c.p);
        }

        static AnimationClip LoadClip(string fbxPath)
        {
            return AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        static void ConfigureModel(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogError($"[AnimSetup] {path} 없음"); return; }
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }

        static void ConfigureClipFbx(string path, Avatar source, bool loop)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"[AnimSetup] {path} 없음"); return; }
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CopyFromOther;
            imp.sourceAvatar  = source;
            var clips = imp.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++) clips[i].loopTime = loop;
            if (clips.Length > 0) imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }
    }
}

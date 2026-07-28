using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MapTools
{
    /// <summary>
    /// 구경꾼 NPC 원클릭 셋업 — Tools ▸ Map ▸ Setup Onlookers.
    /// Props 폴더의 *_Idle.fbx 마다: Generic 임포트 + 클립 루프 → 컨트롤러 생성 → Animator 연결된 프리팹 생성.
    /// 결과 프리팹(Onlooker_이름.prefab)을 배경 프리팹에 드래그만 하면 idle로 살아 움직임.
    /// 새 구경꾼 추가 = fbx를 '이름_Idle.fbx'로 넣고 재실행(멱등).
    /// </summary>
    public static class OnlookerSetup
    {
        const string kDir = "Assets/Map/01_GwangTongGyo/Props";

        [MenuItem("Tools/Map/Setup Onlookers")]
        public static void Setup()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { kDir });
            int made = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith("_Idle.fbx")) continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(path).Replace("Prop_", "").Replace("_Idle", "");

                // ① 임포트: Generic + 루프
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.Generic;
                var clips = imp.defaultClipAnimations;
                for (int i = 0; i < clips.Length; i++) clips[i].loopTime = true;
                if (clips.Length > 0) imp.clipAnimations = clips;
                imp.SaveAndReimport();

                var clip = AssetDatabase.LoadAllAssetRepresentationsAtPath(path)
                    .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) { Debug.LogWarning($"[Onlooker] {path} 클립 없음 — 건너뜀"); continue; }

                // ② 컨트롤러(있으면 재생성)
                string ctrlPath = $"{kDir}/Onlooker_{name}.controller";
                AssetDatabase.DeleteAsset(ctrlPath);
                var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                var st = ctrl.layers[0].stateMachine.AddState("Idle");
                st.motion = clip;
                ctrl.layers[0].stateMachine.defaultState = st;

                // ③ Animator 연결된 프리팹
                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // 화면 밖이면 애니 스킵(성능)

                string prefabPath = $"{kDir}/Onlooker_{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                made++;
                Debug.Log($"[Onlooker] {name} ✔ → {prefabPath}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Onlooker] 완료 — 프리팹 {made}개. 배경 프리팹에 드래그해서 배치하세요.");
        }
    }
}

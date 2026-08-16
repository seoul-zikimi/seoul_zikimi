#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>TurtleAnim 각 상태 클립이 char_turtle 프리팹 본 경로에 실제로 붙는지 검사(콘솔 출력).</summary>
    public static class TurtleAnimDiagnose
    {
        [MenuItem("Tools/Character/거북이 애니 진단")]
        static void Run()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Player/Turtle/TurtleAnim.controller");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Characters/char_turtle.prefab");
            if (ctrl == null || prefab == null) { Debug.LogError("[진단] 컨트롤러/프리팹 없음"); return; }

            var paths = new System.Collections.Generic.HashSet<string>();
            var anim = prefab.GetComponentInChildren<Animator>(true);
            Transform root = anim != null ? anim.transform : prefab.transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                paths.Add(AnimationUtility.CalculateTransformPath(t, root));

            foreach (var state in ctrl.layers[0].stateMachine.states)
            {
                var clip = state.state.motion as AnimationClip;
                if (clip == null) { Debug.LogWarning($"[진단] {state.state.name}: 클립 없음"); continue; }
                var binds = AnimationUtility.GetCurveBindings(clip);
                int match = 0, miss = 0; string missSample = "";
                foreach (var b in binds)
                {
                    if (paths.Contains(b.path)) match++;
                    else { miss++; if (missSample == "") missSample = b.path; }
                }
                Debug.Log($"[진단] {state.state.name}: clip='{clip.name}' len={clip.length:F2}s loop={clip.isLooping} " +
                          $"curves={binds.Length} match={match} miss={miss}" +
                          (miss > 0 ? $" missSample='{missSample}'" : ""));
            }
            Debug.Log($"[진단] 프리팹 경로 샘플: {string.Join(", ", System.Linq.Enumerable.Take(paths, 5))}");
        }
    }
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// 거북이 음영 순화: char_turtle.prefab이 fbx 내장 머티리얼(AI 노멀맵 포함)을 쓰고 있어
    /// 얼굴에 험한 음영이 생긴다 — 노멀맵 없는 순한 URP Lit(디퓨즈만, 저광택)으로 교체.
    /// 되돌리기: 거북이 캐릭터 생성·갱신 후 이 메뉴를 안 돌리면 원상태.
    /// </summary>
    public static class TurtleShadowSoften
    {
        const string kDiffuse = "Assets/Player/Turtle/Idle.fbm/diffuse.png";
        const string kMat = "Assets/Player/Turtle/char_turtle_soft.mat";
        const string kPrefab = "Assets/Resources/Characters/char_turtle.prefab";

        [MenuItem("Tools/Character/거북이 음영 순화(노멀맵 제거)")]
        static void Soften()
        {
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(kDiffuse);
            if (diffuse == null) { Debug.LogError($"[Character] {kDiffuse} 없음"); return; }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(kMat);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, kMat);
            }
            mat.SetTexture("_BaseMap", diffuse);
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", 0.1f);   // 번들거림도 낮춰 카툰 톤에 맞춤
            EditorUtility.SetDirty(mat);

            using (var scope = new PrefabUtility.EditPrefabContentsScope(kPrefab))
            {
                int n = 0;
                foreach (var r in scope.prefabContentsRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    n++;
                }
                Debug.Log($"[Character] 거북이 음영 순화 — 렌더러 {n}개에 {kMat} 적용");
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif

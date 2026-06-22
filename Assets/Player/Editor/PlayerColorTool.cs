using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Player.EditorTools
{
    /// <summary>
    /// model.fbx(회색)에 색을 자동으로 입힌다. Tools ▸ Player ▸ Apply Color to model.fbx.
    /// 우선순위: ① GLB에 임베드된 텍스처 → URP Base Map  ② fbx 메쉬에 정점색 → 정점색 셰이더  ③ 둘 다 없음 → 단색 노랑.
    /// 머티리얼을 fbx 슬롯에 리맵(에셋 단위)하므로 프리팹/씬에 자동 반영.
    /// </summary>
    public static class PlayerColorTool
    {
        const string kGlb      = "Assets/Player/Animations/model.glb";
        const string kModelFbx = "Assets/Player/Animations/model.fbx";
        const string kMatPath  = "Assets/Player/Animations/SnailColor.mat";

        [MenuItem("Tools/Player/Apply Color to model.fbx")]
        public static void Apply()
        {
            var urp  = Shader.Find("Universal Render Pipeline/Lit");
            var vcol = Shader.Find("Custom/URP Vertex Color Lit");

            // fbx 머티리얼 슬롯 확보(읽기 위해)
            var imp = AssetImporter.GetAtPath(kModelFbx) as ModelImporter;
            if (imp == null) { Debug.LogError($"[Color] {kModelFbx} 없음 — 메뉴 전에 임포트 확인"); return; }
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.SaveAndReimport();

            // ① GLB 임베드 텍스처(노멀 제외)
            Texture2D tex = null;
            if (System.IO.File.Exists(kGlb))
                tex = AssetDatabase.LoadAllAssetsAtPath(kGlb).OfType<Texture2D>()
                        .FirstOrDefault(t => !t.name.ToLower().Contains("normal"));

            // ② fbx 메쉬 정점색 유무
            var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(kModelFbx);
            bool fbxVColor = false;
            if (fbxGo != null)
                foreach (var smr in fbxGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.sharedMesh != null && smr.sharedMesh.colors != null && smr.sharedMesh.colors.Length > 0) fbxVColor = true;

            // 머티리얼 결정
            Material mat = new Material(urp != null ? urp : Shader.Find("Standard"));
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                Debug.Log($"[Color] ① GLB 텍스처 사용: {tex.name}");
            }
            else if (fbxVColor && vcol != null)
            {
                mat = new Material(vcol);
                Debug.Log("[Color] ② fbx 정점색 → 정점색 셰이더");
            }
            else
            {
                mat.SetColor("_BaseColor", new Color(1f, 0.93f, 0.6f));   // 크림 노랑
                Debug.Log("[Color] ③ 텍스처·정점색 없음 → 단색 노랑(눈 디테일은 텍스처 필요). GLB 메쉬 써야 눈 살림.");
            }

            // 에셋 저장(덮어쓰기)
            var existing = AssetDatabase.LoadAssetAtPath<Material>(kMatPath);
            if (existing != null) { existing.shader = mat.shader; EditorUtility.CopySerialized(mat, existing); mat = existing; }
            else AssetDatabase.CreateAsset(mat, kMatPath);

            // fbx 머티리얼 슬롯 전부 이 머티리얼로 리맵
            var names = new HashSet<string>();
            if (fbxGo != null)
                foreach (var r in fbxGo.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) names.Add(m.name);
            foreach (var n in names)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            imp.SaveAndReimport();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = mat;
            Debug.Log($"[Color] 완료 → {kMatPath} 를 model.fbx 슬롯 {names.Count}개에 적용. 콘솔의 ①②③ 로그로 어느 경로인지 확인.");
        }
    }
}

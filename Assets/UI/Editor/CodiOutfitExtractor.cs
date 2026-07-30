using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아웃핏 추출 — 드레스업한 캐릭터(예: PreviewSnail 프리팹)를 선택하고
/// Tools ▸ MyPage ▸ Extract Outfit From Selection 실행.
/// 이름이 "Item_"으로 시작하는 조각(본에 붙여둔 glb 인스턴스)의 본·로컬값을 그대로 담아
/// Resources/CodiOutfits/skin_safetyset.prefab 을 만든다(이름·가격은 프리팹에서 수정).
/// </summary>
public static class CodiOutfitExtractor
{
    private const string kDstDir = "Assets/Resources/CodiOutfits";
    private const string kDefaultId = "skin_safetyset";

    [MenuItem("Tools/MyPage/Extract Outfit From Selection")]
    public static void Extract()
    {
        var sel = Selection.activeGameObject;
        if (sel == null)
        {
            EditorUtility.DisplayDialog("아웃핏 추출", "드레스업한 캐릭터(PreviewSnail 프리팹 또는 씬 인스턴스)를 선택하고 실행해 주세요.", "확인");
            return;
        }

        // 프리팹 에셋을 선택했으면 임시 인스턴스로
        GameObject root = sel;
        bool temp = false;
        if (!sel.scene.IsValid())
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(sel);
            temp = true;
        }

        var outfitRoot = new GameObject(kDefaultId);
        var meta = outfitRoot.AddComponent<CodiOutfit>();
        meta.DisplayName = "안전복 세트";
        meta.Price = 100;

        // 포즈 기준 = Animator 노드(model.fbx 루트) — 적용 쪽(CodiOutfit.Apply)과 동일 기준
        var rootAnim = root.GetComponentInChildren<Animator>(true);
        Transform poseRoot = rootAnim != null ? rootAnim.transform : root.transform;

        int n = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Item_")) continue;
            if (t.parent == null) continue;

            var piece = Object.Instantiate(t.gameObject, outfitRoot.transform);
            piece.name = t.name;
            var cp = piece.AddComponent<CodiOutfitPiece>();
            var bone = t.parent;
            float rootScale = poseRoot.lossyScale.x;
            cp.BoneName = bone.name;
            // 본 기준 상대 포즈 기록(루트 스케일 정규화) — 리그 스케일·애니메이션 자세와 무관하게 재현됨
            cp.BonePos = Quaternion.Inverse(bone.rotation) * (t.position - bone.position) / rootScale;
            cp.BoneRot = Quaternion.Inverse(bone.rotation) * t.rotation;
            cp.WorldScale = t.lossyScale / rootScale;
            cp.Version = 3;
            n++;
            Debug.Log($"[Outfit] 조각: {t.name} → 본 '{bone.name}', 본기준 pos={cp.BonePos} 크기={cp.WorldScale}");
        }

        if (temp) Object.DestroyImmediate(root);

        if (n == 0)
        {
            Object.DestroyImmediate(outfitRoot);
            EditorUtility.DisplayDialog("아웃핏 추출", "이름이 'Item_'으로 시작하는 조각을 못 찾았어요.\n(glb 인스턴스 이름을 바꾸지 말고 본 아래에 두세요)", "확인");
            return;
        }

        Directory.CreateDirectory(kDstDir);
        string path = $"{kDstDir}/{kDefaultId}.prefab";
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(outfitRoot, path);
        Object.DestroyImmediate(outfitRoot);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Outfit] 저장 완료 → {path} (조각 {n}개). 이름·가격은 프리팹 CodiOutfit에서 수정하세요.");
    }
}

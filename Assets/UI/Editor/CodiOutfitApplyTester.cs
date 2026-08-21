#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 아웃핏 적용 검증 — Resources/CodiOutfits 프리팹을 선택하고 실행하면
/// 그 아웃핏의 대상 캐릭터를 씬 원점 옆에 소환해 실제 Apply 로직으로 입혀 보여준다.
/// (옷장/인게임과 동일한 코드 경로 — 씬 저작본과 비교해 어긋남을 에디터에서 바로 확인)
/// </summary>
public static class CodiOutfitApplyTester
{
    [MenuItem("Tools/MyPage/아웃핏 적용 테스트(선택 아웃핏)")]
    public static void Test()
    {
        var sel = Selection.activeGameObject;
        var meta = sel != null ? sel.GetComponent<CodiOutfit>() : null;
        if (meta == null)
        {
            EditorUtility.DisplayDialog("적용 테스트", "Resources/CodiOutfits의 아웃핏 프리팹을 선택하고 실행하세요.", "확인");
            return;
        }

        string charId = meta.TargetCharacter;
        GameObject charPrefab = string.IsNullOrEmpty(charId)
            ? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MyPage/PreviewSnail.prefab")
            : AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Characters/{charId}.prefab");
        if (charPrefab == null) { Debug.LogError($"[적용테스트] 캐릭터 프리팹 없음 (target='{charId}')"); return; }

        var ch = (GameObject)PrefabUtility.InstantiatePrefab(charPrefab);
        ch.name = $"~ApplyTest_{meta.name}";
        ch.transform.position = new Vector3(2f, 0f, 0f);   // 저작본 옆에 소환

        // 옷장 프리뷰와 같은 조건: Idle 첫 프레임 포즈
        var anim = ch.GetComponentInChildren<Animator>(true);
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
                if (clip.name.ToLower().Contains("idle") || true) { clip.SampleAnimation(anim.gameObject, 0f); break; }
        }

        CodiOutfit.Apply(ch, meta.name, charId);

        // 결과 실측 로그 — 조각별 월드 위치/크기
        foreach (var t in ch.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("~Outfit_"))
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                    Debug.Log($"[적용테스트] {t.name}/{r.name} worldPos={r.bounds.center} size={r.bounds.size}");

        Selection.activeGameObject = ch;
        Debug.Log($"[적용테스트] '{meta.name}' 적용 완료 — 씬 (2,0,0) 위치에서 저작본과 비교하세요. 확인 후 오브젝트 삭제.");
    }
}
#endif

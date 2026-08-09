using UnityEngine;

/// <summary>
/// 캐릭터 모델 교체 실행기 — 달팽이 모델은 그대로 두고(애니 동기화 캐리어) 렌더러만 끈 뒤,
/// 선택 캐릭터 프리팹을 모델 루트 자식으로 붙여 상태를 미러링(CharacterMirror)한다.
/// NetworkAnimator는 스폰 후 재바인딩이 안 되므로 이 구조가 유일하게 안전하다.
/// 프리뷰(마이페이지)와 인게임(CharacterWearer) 공용.
/// </summary>
public static class CharacterSwap
{
    private const string kClone = "~Character_";

    /// <summary>맵 물리 바닥과 렌더 바닥의 어긋남 보정(실측 0.135). 맵 콜라이더를 고치면 0으로.</summary>
    private const float kVisualGroundOffset = -0.135f;

    /// <summary>현재 기본 외 캐릭터가 활성화돼 있으면 true(아웃핏 적용 스킵 판단용).</summary>
    public static bool IsNonDefault(GameObject player)
    {
        if (player == null) return false;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(kClone)) return true;
        return false;
    }

    /// <summary>id 캐릭터로 교체(빈 id = 달팽이 복원). 같은 id면 무동작.</summary>
    public static void Apply(GameObject player, string id)
    {
        if (player == null) return;

        // 캐리어(달팽이) Animator = 클론 밖의 첫 Animator
        Animator carrier = null;
        foreach (var a in player.GetComponentsInChildren<Animator>(true))
        {
            if (IsInClone(a.transform, player.transform)) continue;
            carrier = a; break;
        }
        if (carrier == null) return;

        // 기존 클론 정리(같은 id면 유지하고 끝)
        Transform existing = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(kClone)) { existing = t; break; }
        if (existing != null)
        {
            if (existing.name == kClone + id) return;
            Object.Destroy(existing.gameObject);
        }

        if (string.IsNullOrEmpty(id)) { SetCarrierVisible(carrier, true); return; }

        var prefab = CharacterCatalog.LoadPrefab(id);
        if (prefab == null) { Debug.LogWarning($"[Character] 프리팹 없음: Resources/Characters/{id}"); SetCarrierVisible(carrier, true); return; }

        var clone = Object.Instantiate(prefab, carrier.transform);
        clone.name = kClone + id;
        clone.transform.localRotation = Quaternion.identity;
        // 프리팹 피벗 = 발바닥(Idle 포즈 min.y=0). 부착 기준은 "비주얼 지면":
        // 맵 배경의 물리 콜라이더가 렌더 바닥보다 ~0.135 높다(실측: 물리 -0.115 vs 비주얼 ≈-0.25).
        // 달팽이는 배가 그만큼 묻히는 디자인이라 안 보였던 것 — 발 달린 캐릭터는 이만큼 내려야 접지로 보인다.
        clone.transform.localPosition = new Vector3(0f, kVisualGroundOffset, 0f);

        SetCarrierVisible(carrier, false);
        carrier.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 렌더러 꺼도 상태머신은 계속 돌아야 미러 가능

        var selfAnim = clone.GetComponentInChildren<Animator>(true);
        if (selfAnim != null)
        {
            var mirror = clone.GetComponent<CharacterMirror>();
            if (mirror == null) mirror = clone.AddComponent<CharacterMirror>();
            mirror.Init(carrier, selfAnim);
        }
    }

    private static bool IsInClone(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent)
            if (p.name.StartsWith(kClone)) return true;
        return false;
    }

    // 캐리어(달팽이) 쪽 렌더러만 켜고 끔 — 클론 자식은 건드리지 않는다
    private static void SetCarrierVisible(Animator carrier, bool visible)
    {
        foreach (var r in carrier.GetComponentsInChildren<Renderer>(true))
        {
            if (IsInClone(r.transform, carrier.transform.parent)) continue;
            r.enabled = visible;
        }
    }
}

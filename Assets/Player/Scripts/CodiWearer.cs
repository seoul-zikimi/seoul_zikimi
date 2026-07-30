using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 인게임 코디 착용 동기화 — PlayerUnit 프리팹에 붙는다.
/// 스폰 시 오너가 저장된 아웃핏 id를 NetworkVariable에 실어 모두에게 복제(원격도 같은 모습).
/// 마이페이지에서 갈아입으면 다음 게임 스폰부터 반영.
/// </summary>
public class CodiWearer : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString64Bytes> m_Outfit = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        m_Outfit.OnValueChanged += OnOutfitChanged;
        if (IsOwner)
            m_Outfit.Value = new FixedString64Bytes(SaveService.EquippedOutfit ?? "");
        else
            ApplyCurrent();   // 늦게 합류: 이미 실려 있는 값 적용
    }

    public override void OnNetworkDespawn() => m_Outfit.OnValueChanged -= OnOutfitChanged;

    private void OnOutfitChanged(FixedString64Bytes _, FixedString64Bytes __) => ApplyCurrent();

    private Coroutine m_Pending;

    private void ApplyCurrent()
    {
        if (m_Pending != null) StopCoroutine(m_Pending);
        m_Pending = StartCoroutine(CoApply());
    }

    // 스폰 직후엔 모델 본이 아직 준비 전일 수 있어 잠시 재시도 후 착용
    private System.Collections.IEnumerator CoApply()
    {
        float deadline = Time.time + 5f;
        while (Time.time < deadline && !HasBone()) yield return null;
        CodiOutfit.Apply(gameObject, m_Outfit.Value.ToString());
        m_Pending = null;
    }

    private bool HasBone()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "spine.001") return true;
        return false;
    }
}

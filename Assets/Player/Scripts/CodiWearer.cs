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
    private readonly NetworkVariable<FixedString64Bytes> m_Trail = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        m_Outfit.OnValueChanged += OnOutfitChanged;
        m_Trail.OnValueChanged += OnTrailChanged;
        if (IsOwner)
        {
            m_Outfit.Value = new FixedString64Bytes(SaveService.EquippedOutfit ?? "");
            m_Trail.Value = new FixedString64Bytes(SaveService.EquippedTrail ?? "");
        }
        else
            ApplyCurrent();   // 늦게 합류: 이미 실려 있는 값 적용
        TrailCatalog.Attach(gameObject, m_Trail.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        m_Outfit.OnValueChanged -= OnOutfitChanged;
        m_Trail.OnValueChanged -= OnTrailChanged;
    }

    private void OnOutfitChanged(FixedString64Bytes _, FixedString64Bytes __) => ApplyCurrent();
    private void OnTrailChanged(FixedString64Bytes _, FixedString64Bytes v) => TrailCatalog.Attach(gameObject, v.ToString());

    /// <summary>캐릭터 교체 후 아웃핏 다시 입히기 — CharacterWearer가 호출(교체 전에 입힌 아웃핏은 무효).</summary>
    public void Reapply() => ApplyCurrent();

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

    // 아웃핏 부착 기준 본 — 달팽이(spine.001) 또는 대체 캐릭터(mixamorig:Hips) 어느 쪽이든 준비되면 OK
    private bool HasBone()
    {
        string need = CharacterSwap.CurrentId(gameObject) == "" ? "spine.001" : "mixamorig:Hips";
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == need) return true;
        return false;
    }
}

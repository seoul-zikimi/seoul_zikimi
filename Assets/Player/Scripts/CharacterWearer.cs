using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 인게임 캐릭터 선택 동기화 — PlayerUnit 프리팹에 붙는다(CodiWearer와 같은 패턴).
/// 스폰 시 오너가 저장된 캐릭터 id를 NetworkVariable에 실어 모두에게 복제.
/// 마이페이지에서 바꾸면 다음 게임 스폰부터 반영.
/// </summary>
public class CharacterWearer : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString64Bytes> m_Character = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>복제된 캐릭터 id(빈 문자열 = 달팽이).</summary>
    public string CharacterId { get; private set; } = "";

    /// <summary>이 캐릭터의 특수 능력 — 이동/운반 코드가 매 틱 읽는다.
    /// FixedString.ToString()은 호출마다 문자열을 할당하므로 값이 바뀔 때만 여기서 캐시한다.</summary>
    public CharacterAbility Ability { get; private set; } = CharacterAbility.Snail;

    public override void OnNetworkSpawn()
    {
        m_Character.OnValueChanged += OnChanged;
        if (IsOwner)
            m_Character.Value = new FixedString64Bytes(SaveService.EquippedCharacter ?? "");
        else
            ApplyCurrent();
        CacheAbility();   // 오너는 위 대입이 콜백을 안 태울 수도 있어 여기서 한 번 맞춰둔다
    }

    public override void OnNetworkDespawn() => m_Character.OnValueChanged -= OnChanged;

    private void OnChanged(FixedString64Bytes _, FixedString64Bytes __) { CacheAbility(); ApplyCurrent(); }

    private void CacheAbility()
    {
        CharacterId = m_Character.Value.ToString();
        Ability = CharacterAbility.For(CharacterId);
    }

    private Coroutine m_Pending;

    private void ApplyCurrent()
    {
        if (m_Pending != null) StopCoroutine(m_Pending);
        m_Pending = StartCoroutine(CoApply());
    }

    // 스폰 직후엔 모델(Animator)이 아직 준비 전일 수 있어 잠시 재시도
    private System.Collections.IEnumerator CoApply()
    {
        float deadline = Time.time + 5f;
        while (Time.time < deadline && GetComponentInChildren<Animator>(true) == null) yield return null;
        CharacterSwap.Apply(gameObject, m_Character.Value.ToString());
        // 캐릭터가 바뀌면 그 캐릭터용 아웃핏을 다시 입힌다(교체 전 적용분은 대상이 달라 무효)
        GetComponent<CodiWearer>()?.Reapply();
        m_Pending = null;
    }
}

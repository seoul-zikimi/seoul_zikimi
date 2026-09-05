using UnityEngine;

/// <summary>
/// 캐릭터별 특수 능력 표 — 키는 CharacterCatalog와 같은 id("" = 기본, 달팽이).
/// 능력 수치와 화면에 뜨는 소개 문구를 한자리에 둔다(따로 두면 수치와 설명이 갈라진다).
/// 새 캐릭터·새 능력은 아래 표에 한 줄만 추가하면 사용처는 손댈 게 없다.
/// </summary>
public readonly struct CharacterAbility
{
    /// <summary>벽 기어오르기 가능 — 달팽이 전용(나머지는 벽에 아예 안 붙는다).</summary>
    public readonly bool CanClimb;

    /// <summary>배치·집기·공정 사거리 보너스(칸) — 소라게 +1.</summary>
    public readonly float ReachBonusCells;

    /// <summary>무거운(MaterialDef.IsHeavy) 재료를 혼자 들어도 이동속도가 안 깎임 — 거북이.</summary>
    public readonly bool HeavyImmune;

    /// <summary>인트로 선택 카드용 소개 — 두 줄(분위기 한 줄 + 효과 한 줄). 카드가 넓어 문장이 들어간다.</summary>
    public readonly string Description;

    /// <summary>옷장 카드용 소개 — 카드가 좁아 짧은 한 줄만 들어간다.</summary>
    public readonly string ShortDescription;

    private CharacterAbility(bool canClimb, float reachBonusCells, bool heavyImmune,
                             string description, string shortDescription)
    {
        CanClimb = canClimb;
        ReachBonusCells = reachBonusCells;
        HeavyImmune = heavyImmune;
        Description = description;
        ShortDescription = shortDescription;
    }

    // ── 캐릭터별 능력 ────────────────────────────────────────────────
    // 소개 문구는 인트로 만화 톤(등껍질 삼총사 → 건축레인저)에 맞춘다 — 첫 줄은 캐릭터 성격, 둘째 줄은 실제 효과.
    public static readonly CharacterAbility Snail = new(true, 0f, false,
        "맨몸으로 벽을 기어오릅니다.\n비계 없이 위층으로 직행!", "벽을 탑니다");

    public static readonly CharacterAbility Turtle = new(false, 0f, true,
        "등껍질로 다져진 뚝심.\n무거운 짐에도 느려지지 않습니다.", "무거워도 안 느려짐");

    public static readonly CharacterAbility Crab = new(false, 1f, false,
        "쭉 뻗는 집게발.\n한 칸 더 멀리 손이 닿습니다.", "손이 한 칸 더");

    private static readonly CharacterAbility None = new(false, 0f, false, "", "");

    /// <summary>id의 능력. 빈 id = 기본 캐릭터(달팽이), 표에 없는 id = 능력 없음.</summary>
    public static CharacterAbility For(string id)
    {
        if (string.IsNullOrEmpty(id)) return Snail;
        return id switch
        {
            "char_turtle" => Turtle,
            "char_crab"   => Crab,
            _             => None,
        };
    }

    /// <summary>플레이어 오브젝트의 현재 능력(CharacterWearer가 복제·캐시한 id 기준).
    /// wearer는 호출부가 들고 있는 캐시 슬롯 — 매 틱 GetComponent 하지 않으려고 ref로 받는다.
    /// 캐릭터 컴포넌트가 없거나 아직 복제 전이면 기본(달팽이).</summary>
    public static CharacterAbility Of(GameObject player, ref CharacterWearer wearer)
    {
        if (wearer == null && player != null) wearer = player.GetComponent<CharacterWearer>();
        return wearer != null ? wearer.Ability : Snail;
    }
}

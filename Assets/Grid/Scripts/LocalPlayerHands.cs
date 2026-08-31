namespace GridSystem
{
    /// <summary>
    /// 로컬 플레이어가 지금 뭔가를 들고 있는지 GridSystem 쪽에서 읽기 위한 창구.
    ///
    /// 왜 이런 게 필요한가: Player 어셈블리(Assembly-CSharp)는 GridSystem.asmdef를 참조하지만
    /// 반대 방향은 참조가 없다 — 그래서 기믹 스크립트가 PlayerCarry를 직접 볼 수 없다.
    /// PlayerCarry가 매 프레임 여기에 써 두고(OwnerUpdate), 기믹은 여기서 읽는다.
    ///
    /// 로컬 전용(복제 안 함). 다른 플레이어의 손 상태는 알 수 없다 —
    /// 각 클라가 자기 상태만 보고 서버에 의사를 보내는 구조라 그걸로 충분하다.
    /// </summary>
    public static class LocalPlayerHands
    {
        /// <summary>재료나 도구를 들고 있는가. PlayerCarry가 매 프레임 갱신한다.</summary>
        public static bool IsHoldingAnything;

        /// <summary>지금 손에 든 재료의 Id. 빈손이거나 도구를 들었으면 <see cref="int.MinValue"/>.
        /// 정답 고스트 강조가 '손에 든 재료'를 최우선으로 따르기 위한 값 — PlayerCarry가 매 프레임 갱신한다.</summary>
        public static int HeldMaterialId = int.MinValue;

        /// <summary>E 키가 이미 다른 용도(공정·아이템)로 물려 있는가 — 기믹이 E를 뺏으면 안 되는 상황.</summary>
        public static bool IsEKeyTaken =>
            IsHoldingAnything || (ItemNetwork.Instance != null && ItemNetwork.Instance.LocalHasItem);

        /// <summary>플레이어 디스폰/씬 전환 시 호출 — 남은 값이 다음 라운드로 새지 않게.</summary>
        public static void Clear()
        {
            IsHoldingAnything = false;
            HeldMaterialId = int.MinValue;
        }
    }
}

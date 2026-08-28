using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 사방신 받침대 마커 — PlayerCarry가 레이캐스트로 인식해 '석상 든 채 클릭 배치'에 쓴다.
    /// Index = 방위(0 동/청룡, 1 서/백호, 2 남/주작, 3 북/현무). GyeongbokgungMapTool이 붙인다.
    /// </summary>
    public class GuardianPedestal : MonoBehaviour
    {
        public int Index;
    }
}

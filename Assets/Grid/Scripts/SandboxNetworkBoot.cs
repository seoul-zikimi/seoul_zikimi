using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 샌드박스 테스트 씬 전용: Play 누르면 바로 호스트로 시작한다(로비/매치메이킹 없이).
    /// 호스트가 되면 씬의 NetworkObject(보급소 등)가 스폰되고, 주문 HUD가 자동으로 뜬다.
    /// </summary>
    public class SandboxNetworkBoot : MonoBehaviour
    {
        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogWarning("[Sandbox] NetworkManager가 없음 — 주문/배송 테스트 불가"); return; }
            if (nm.IsListening) return;
            nm.StartHost();
            Debug.Log("[Sandbox] 호스트 시작 — 우상단 주문 HUD에서 재료를 주문하면 DeliveryPoint 자리에 떨어집니다.");
        }
    }
}

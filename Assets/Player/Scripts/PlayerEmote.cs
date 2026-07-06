using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Player
{
    /// <summary>
    /// 이모트 전용(F1 = 머리 위 하트 깨짐). 입력·이펙트·원격 동기화만 담당 — 들기/공정(PlayerCarry)과 분리.
    /// 이모트 추가 시 여기에 키·프리팹만 늘리면 됨.
    /// </summary>
    public class PlayerEmote : NetworkBehaviour
    {
        [SerializeField] private GameObject m_HeartFx;   // CFXR2 Broken Heart

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f1Key.wasPressedThisFrame) EmoteHeart();
        }

        // owner 로컬 즉시 재생 + 서버 경유로 다른 클라에도(내 이모트가 남들한테 보이게).
        private void EmoteHeart()
        {
            Vector3 pos = transform.position + Vector3.up * 2.2f;
            SpawnFx(pos);
            if (IsSpawned) RequestFxRpc(pos);
        }

        private void SpawnFx(Vector3 pos)
        {
            if (m_HeartFx == null) return;
            var go = Instantiate(m_HeartFx, pos, Quaternion.identity);
            Destroy(go, 5f);   // CFXR 자체 정리 실패 대비 안전망
        }

        [Rpc(SendTo.Server)]
        private void RequestFxRpc(Vector3 pos) => FxRpc(pos);

        [Rpc(SendTo.NotOwner)]
        private void FxRpc(Vector3 pos) => SpawnFx(pos);
    }
}

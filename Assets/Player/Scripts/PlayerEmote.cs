using Unity.Netcode;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 이모트 전용(F1~F10 = 머리 위 이펙트). 입력·이펙트·원격 동기화만 담당 — 들기/공정(PlayerCarry)과 분리.
    /// 매핑 교체 = 인스펙터 m_EmoteFx 배열(0=F1 … 9=F10)에 프리팹 드래그.
    /// </summary>
    public class PlayerEmote : NetworkBehaviour
    {
        public static PlayerEmote Local { get; private set; }
        [SerializeField] private GameObject[] m_EmoteFx = new GameObject[10];   // 0=F1 … 9=F10 (비면 이모지 폴백)
        [SerializeField] private Texture2D m_EmojiAtlas;                        // TMP EmojiOne(4x4) — 이모지 팝용
        [SerializeField] private Texture2D m_ThumbsDownTex;                     // 붐따 👎 (Noto Emoji 개별 PNG)
        [SerializeField] private Texture2D m_ThumbsUpTex;                       // 붐업 👍 (Noto Emoji 개별 PNG)

        // 슬롯이 빌 때 쓸 이모지(F2~F10): 😍 😎 👍 😜 😫 🤣 ☺️ ☹️ 👎  (-2=붐따, -3=붐업 통짜 텍스처)
        private static readonly int[] kEmojiForKey = { -1, 2, 3, -3, 11, 10, 13, 0, 15, -2 };

        private EmoteWheelUI m_Wheel;   // T 홀드 동안 표시되는 선택 패널(프리팹 HUD)

        private void Update()
        {
            if (!IsOwner) return;
            var input = PlayerInputHandler.Local;
            if (input == null) return;
            int emote = input.ConsumeEmoteIndex();
            if (emote >= 0) TriggerEmote(emote);
            UpdateWheel(input);
        }

        // [07/26 기획] T 꾹 = 이모티콘 선택 UI 표시(누른 동안), 버튼 클릭 = 발동, 떼면 닫힘.
        private void UpdateWheel(PlayerInputHandler input)
        {
            if (input.EmoteWheelPressedThisFrame)
            {
                if (UIManager.Instance != null)
                {
                    m_Wheel = UIManager.Instance.ShowHUDUI<EmoteWheelUI>();
                    if (m_Wheel != null)
                    {
                        m_Wheel.OnPick = i => { TriggerEmote(i); HideWheel(); };
                        m_Wheel.gameObject.SetActive(true);
                    }
                }
            }
            else if (input.EmoteWheelReleasedThisFrame)
            {
                // 오버워치식: 마우스가 가리키던 섹터를 T 떼는 순간 발동(클릭 불필요)
                if (m_Wheel != null && m_Wheel.gameObject.activeSelf && m_Wheel.HoverIndex >= 0)
                    TriggerEmote(m_Wheel.HoverIndex);
                HideWheel();
            }
        }

        private void HideWheel()
        {
            if (m_Wheel != null) m_Wheel.gameObject.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            HideWheel();
        }

        // owner 로컬 즉시 재생 + 서버 경유로 다른 클라에도(내 이모트가 남들한테 보이게).
        public void TriggerEmote(int index)
        {
            if (!IsOwner || index < 0 || index >= m_EmoteFx.Length) return;
            // 파티클(F1 하트)은 머리에 붙게 낮게, 이모지 팝은 위에서 떠오르게
            float h = m_EmoteFx[index] != null ? 1.6f : 2.2f;
            Vector3 pos = transform.position + Vector3.up * h;
            SpawnFx(index, pos);
            if (IsSpawned) RequestFxRpc(index, pos);
        }

        private void SpawnFx(int index, Vector3 pos)
        {
            if (index < 0 || index >= m_EmoteFx.Length) return;

            if (m_EmoteFx[index] != null)   // 파티클 프리팹 지정된 슬롯(F1 하트 등)
            {
                var go = Instantiate(m_EmoteFx[index], pos, Quaternion.identity);
                Destroy(go, 4f);   // 루프 계열(Cartoon Fight 등)도 이모트는 4초에 끊음
                return;
            }

            // 빈 슬롯 → 이모지 팝(빌보드 스프라이트)
            if (index >= kEmojiForKey.Length) return;
            int code = kEmojiForKey[index];
            if (code == -2)      EmoteBubble.ShowFull(m_ThumbsDownTex, pos);   // 붐따 👎
            else if (code == -3) EmoteBubble.ShowFull(m_ThumbsUpTex, pos);     // 붐업 👍
            else if (code >= 0)  EmoteBubble.Show(m_EmojiAtlas, code, pos);
        }

        [Rpc(SendTo.Server)]
        private void RequestFxRpc(int index, Vector3 pos) => FxRpc(index, pos);

        [Rpc(SendTo.NotOwner)]
        private void FxRpc(int index, Vector3 pos) { if (!IsOwner) SpawnFx(index, pos); }   // 오너는 이미 로컬 재생(이중 방지)
    }
}

using Unity.Netcode;
using UnityEngine;

namespace Player
{
    /// <summary>
    /// 감정표현(기획서 '인게임 소통 수단 시스템' 07/24) — 대사 11종(EmoteDefs) 전용.
    /// T 꾹 = 휠 표시, 떼면 가리킨 대사 발동. F1~F10 = 앞 10개 대사 단축키.
    /// 발동 = 머리 위 대사 말풍선 + 보이스 재생(클립이 Resources/Voices/Emotes/에 있으면).
    /// 입력·연출·원격 동기화만 담당 — 들기/공정(PlayerCarry)과 분리.
    /// </summary>
    public class PlayerEmote : NetworkBehaviour
    {
        public static PlayerEmote Local { get; private set; }
        [Tooltip("대사별 추가 파티클(선택) — 인덱스 = EmoteDefs.All 순서. 비워도 됨(말풍선+아이콘+보이스만).\n"
               + "대사와 상관없는 이펙트를 물리면 오해를 부른다(예: 망치 대사에 Broken Heart) — 확실할 때만 채울 것.")]
        [SerializeField] private GameObject[] m_EmoteFx = new GameObject[11];

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
            if (!IsOwner || index < 0 || index >= EmoteDefs.Count) return;
            Vector3 pos = transform.position + Vector3.up * 2.2f;   // 머리 위 말풍선 높이
            Play(index, pos);
            if (IsSpawned) RequestFxRpc(index, pos);
        }

        // 말풍선 + 보이스 + (있으면) 파티클. 로컬·원격 공통 경로.
        private void Play(int index, Vector3 pos)
        {
            if (index < 0 || index >= EmoteDefs.Count) return;

            // 말풍선(+ 대사별 아이콘 — 있는 대사만. '망치 갖다줘!' 등은 망치 이모티콘이 붙는다)
            EmoteBubble.ShowText(EmoteDefs.All[index].Line, EmoteDefs.Icon(index), pos);

            // 보이스: 클립이 준비된 대사만 재생(3D — 멀면 작게, SFX 볼륨 슬라이더 적용)
            var voice = EmoteDefs.Voice(index);
            if (voice != null)
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFXAt(voice, pos);
                else AudioSource.PlayClipAtPoint(voice, pos);
            }

            // 대사별 추가 파티클(선택 슬롯 — 인스펙터에서 지정한 경우만)
            if (index < m_EmoteFx.Length && m_EmoteFx[index] != null)
            {
                var go = Instantiate(m_EmoteFx[index], pos + Vector3.down * 0.6f, Quaternion.identity);
                Destroy(go, 4f);   // 루프 계열도 감정표현은 4초에 끊음
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestFxRpc(int index, Vector3 pos) => FxRpc(index, pos);

        [Rpc(SendTo.NotOwner)]
        private void FxRpc(int index, Vector3 pos) { if (!IsOwner) Play(index, pos); }   // 오너는 이미 로컬 재생(이중 방지)
    }
}

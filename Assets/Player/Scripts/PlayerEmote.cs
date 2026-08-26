using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [Tooltip("대사별 추가 파티클(선택) — 인덱스 = EmoteDefs.All 순서. 비워도 됨(말풍선+보이스만).")]
        [SerializeField] private GameObject[] m_EmoteFx = new GameObject[11];

        private EmoteWheelUI m_Wheel;   // T 홀드 동안 표시되는 선택 패널(프리팹 HUD)

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            // F1~F10 = 앞 10개 대사 바로 발동(휠 없이)
            for (int i = 0; i < 10 && i < EmoteDefs.Count; i++)
            {
                var key = kb[(Key)((int)Key.F1 + i)];   // Key.F1~F12는 연속 enum
                if (key != null && key.wasPressedThisFrame) { Emote(i); break; }
            }

            UpdateWheel(kb);
        }

        // [07/26 기획] T 꾹 = 감정표현 선택 UI 표시(누른 동안), 버튼 클릭 = 발동, 떼면 닫힘.
        private void UpdateWheel(Keyboard kb)
        {
            if (kb.tKey.wasPressedThisFrame)
            {
                if (UIManager.Instance != null)
                {
                    m_Wheel = UIManager.Instance.ShowHUDUI<EmoteWheelUI>();
                    if (m_Wheel != null)
                    {
                        m_Wheel.OnPick = i => { Emote(i); HideWheel(); };
                        m_Wheel.gameObject.SetActive(true);
                    }
                }
            }
            else if (kb.tKey.wasReleasedThisFrame)
            {
                // 오버워치식: 마우스가 가리키던 섹터를 T 떼는 순간 발동(클릭 불필요)
                if (m_Wheel != null && m_Wheel.gameObject.activeSelf && m_Wheel.HoverIndex >= 0)
                    Emote(m_Wheel.HoverIndex);
                HideWheel();
            }
        }

        private void HideWheel()
        {
            if (m_Wheel != null) m_Wheel.gameObject.SetActive(false);
        }

        public override void OnNetworkDespawn() => HideWheel();

        // owner 로컬 즉시 재생 + 서버 경유로 다른 클라에도(내 감정표현이 남들한테 보이게).
        private void Emote(int index)
        {
            if (index < 0 || index >= EmoteDefs.Count) return;
            Vector3 pos = transform.position + Vector3.up * 2.2f;   // 머리 위 말풍선 높이
            Play(index, pos);
            if (IsSpawned) RequestFxRpc(index, pos);
        }

        // 말풍선 + 보이스 + (있으면) 파티클. 로컬·원격 공통 경로.
        private void Play(int index, Vector3 pos)
        {
            if (index < 0 || index >= EmoteDefs.Count) return;

            EmoteBubble.ShowText(EmoteDefs.All[index].Line, pos);

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

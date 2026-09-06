#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이 영상 촬영용 자유 시점 카메라 — 에디터·개발 빌드 전용. GameScene에 자동 생성된다.
///
///  · 혼자 찍는 구조: 특수 방·관전자 없이, 평범하게 플레이하다가 F9를 누르면 카메라가 내 캐릭터에서 떨어져 나온다.
///    내 캐릭터는 그 자리에 그대로 서 있고(GameplayInputBlocker.TrailerBlocked로 입력 차단), 카메라만 WASD로 날아다닌다.
///    F9를 다시 누르면 카메라가 내 캐릭터로 돌아오고 조작도 돌아온다.
///  · 우선순위 100 CinemachineCamera라 켜면 Brain이 플레이어 카메라에서 부드럽게 넘어오고, 끄면 되돌아간다.
///  · 서버·다른 클라에는 아무것도 알리지 않는다 — 다른 사람 눈엔 내가 가만히 서 있는 것뿐.
///
/// 조작
///  이동  W/A/S/D · 위/아래 E/Q(또는 Space/Ctrl) · Shift 3배 · Alt 0.3배 · 휠 = 기본 속도
///  시선  우클릭 드래그
///  1/2/3 프리셋 — 탑다운(오버쿡드) / 쿼터뷰 / 로우앵글. 팔로우 중이면 대상, 아니면 그리드 중앙을 바라본다
///  Tab   팔로우 대상 순환(모든 플레이어, 나 포함)   F  팔로우 켬/끔(현재 오프셋 유지, 둥실 따라감)
///  H     HUD 숨김/표시   N  머리 위 이름표 숨김/표시   F9  카메라 켬/끔
///  켜진 동안 마우스 커서는 숨김(끄면 복원).
/// </summary>
public sealed class TrailerCamera : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode _)
    {
        if (scene.name != SceneNames.GameScene) return;
        if (FindFirstObjectByType<TrailerCamera>() != null) return;
        new GameObject("~TrailerCamera").AddComponent<TrailerCamera>();   // 씬 오브젝트 — 씬이 바뀌면 함께 정리
    }

    private const int   kPriority     = 100;
    private const float kLookSpeed    = 0.15f;   // 도/픽셀
    private const float kFollowDamp   = 4f;      // 팔로우 감쇠(클수록 빨리 붙음)
    private const float kDefaultSpeed = 10f;     // m/s

    private CinemachineCamera m_Vcam;
    private bool  m_Active;
    private float m_Yaw, m_Pitch;
    private float m_Speed = kDefaultSpeed;
    private bool  m_HudHidden;

    private Transform m_Follow;
    private Vector3   m_FollowOffset;
    private readonly List<Player.PlayerUnit> m_Players = new();

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f9Key.wasPressedThisFrame) { if (m_Active) Disable(); else Enable(); }
        if (!m_Active) return;

        var ms = Mouse.current;
        float dt = Time.unscaledDeltaTime;

        // ── 시선: 우클릭 드래그 ──
        if (ms != null)
        {
            if (ms.rightButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (ms.rightButton.wasReleasedThisFrame) Cursor.lockState = CursorLockMode.None;
            // 촬영 중엔 커서가 화면에 찍히면 안 된다 — 다른 코드가 되살려도 매 프레임 다시 숨김
            // (에디터는 게임 뷰에 포커스가 있을 때만 적용되므로 창을 한 번 클릭해야 한다)
            if (Cursor.visible) Cursor.visible = false;
            if (ms.rightButton.isPressed)
            {
                Vector2 d = ms.delta.ReadValue();
                m_Yaw   += d.x * kLookSpeed;
                m_Pitch  = Mathf.Clamp(m_Pitch - d.y * kLookSpeed, -89f, 89f);
            }
            float scroll = ms.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                m_Speed = Mathf.Clamp(m_Speed * Mathf.Pow(1.15f, Mathf.Sign(scroll)), 1f, 60f);
        }
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);

        // ── 이동: 카메라 yaw 기준 수평 + 절대 상하 ──
        Vector3 flatFwd = Quaternion.Euler(0f, m_Yaw, 0f) * Vector3.forward;
        Vector3 flatRight = Quaternion.Euler(0f, m_Yaw, 0f) * Vector3.right;
        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += flatFwd;
        if (kb.sKey.isPressed) move -= flatFwd;
        if (kb.dKey.isPressed) move += flatRight;
        if (kb.aKey.isPressed) move -= flatRight;
        if (kb.eKey.isPressed || kb.spaceKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed || kb.leftCtrlKey.isPressed) move -= Vector3.up;
        float speed = m_Speed * (kb.leftShiftKey.isPressed ? 3f : kb.leftAltKey.isPressed ? 0.3f : 1f);
        Vector3 delta = move.sqrMagnitude > 0f ? move.normalized * speed * dt : Vector3.zero;

        // ── 프리셋 ──
        if (kb.digit1Key.wasPressedThisFrame) ApplyPreset(65f, 22f);   // 탑다운(오버쿡드)
        if (kb.digit2Key.wasPressedThisFrame) ApplyPreset(45f, 16f);   // 쿼터뷰
        if (kb.digit3Key.wasPressedThisFrame) ApplyPreset(15f, 9f);    // 로우앵글

        // ── 팔로우 ──
        if (kb.tabKey.wasPressedThisFrame) CycleFollowTarget();
        if (kb.fKey.wasPressedThisFrame)
        {
            if (m_Follow != null) { m_Follow = null; Debug.Log("[TrailerCam] 팔로우 해제"); }
            else CycleFollowTarget();
        }
        if (m_Follow != null)
        {
            m_FollowOffset += delta;   // 팔로우 중 WASD는 오프셋 조절
            Vector3 desired = m_Follow.position + m_FollowOffset;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-kFollowDamp * dt));
        }
        else transform.position += delta;

        // ── 표시 토글 ──
        if (kb.hKey.wasPressedThisFrame) SetHudHidden(!m_HudHidden);
        if (kb.nKey.wasPressedThisFrame)
        {
            Player.PlayerUnit.HideNametags = !Player.PlayerUnit.HideNametags;
            Debug.Log($"[TrailerCam] 이름표 {(Player.PlayerUnit.HideNametags ? "숨김" : "표시")}");
        }
    }

    // 카메라를 내 캐릭터에서 떼어낸다 — 현재 플레이어 카메라 위치·시선에서 그대로 이어받아 출발.
    private void Enable()
    {
        if (m_Active) return;
        m_Active = true;

        var main = Camera.main;
        if (main != null)
        {
            transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            Vector3 e = main.transform.rotation.eulerAngles;
            m_Yaw = e.y; m_Pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        if (m_Vcam == null)
        {
            m_Vcam = gameObject.AddComponent<CinemachineCamera>();   // Follow/LookAt 없음 — 이 transform 그대로 촬영
            m_Vcam.Lens.FieldOfView = 50f;
        }
        m_Vcam.Priority = kPriority;
        m_Vcam.enabled = true;

        GameplayInputBlocker.TrailerBlocked = true;   // 내 캐릭터는 제자리에 선다(WASD는 카메라가 쓴다)
        SetHudHidden(true);
        Debug.Log("[TrailerCam] ON — WASD/EQ 이동 · 우클릭 시선 · 1/2/3 프리셋 · Tab/F 팔로우 · H HUD · N 이름표 · F9 종료");
    }

    // 카메라를 내 캐릭터로 되돌리고 조작을 돌려준다.
    private void Disable()
    {
        if (!m_Active) return;
        m_Active = false;
        if (m_Vcam != null) m_Vcam.enabled = false;
        m_Follow = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SetHudHidden(false);
        Player.PlayerUnit.HideNametags = false;
        GameplayInputBlocker.TrailerBlocked = false;
        Debug.Log("[TrailerCam] OFF");
    }

    private void OnApplicationFocus(bool focus)
    {
        if (m_Active && focus) Cursor.visible = false;   // 알트탭 후 돌아오면 다시 숨김
    }

    private void OnDestroy()
    {
        if (m_Active) Disable();   // 씬을 떠나면 잠금·표시 상태를 반드시 원복
    }

    // 초점(팔로우 대상 또는 그리드 중앙)을 pitch 각도·거리로 내려다보는 자리로 점프. yaw는 유지.
    private void ApplyPreset(float pitch, float distance)
    {
        Vector3 focus = m_Follow != null ? m_Follow.position + Vector3.up * 0.8f : GridCenter();
        m_Pitch = pitch;
        Vector3 dir = Quaternion.Euler(m_Pitch, m_Yaw, 0f) * Vector3.forward;
        Vector3 pos = focus - dir * distance;
        if (m_Follow != null) m_FollowOffset = pos - m_Follow.position;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
    }

    private static Vector3 GridCenter()
    {
        var gm = FindFirstObjectByType<GridSystem.GridManager>();
        if (gm == null) return Camera.main != null ? Camera.main.transform.position + Camera.main.transform.forward * 10f : Vector3.zero;
        Vector3Int s = gm.EffectiveSize;
        float u = GridSystem.GridContract.Unit;
        return gm.transform.position + new Vector3(s.x * 0.5f, s.y * 0.25f, s.z * 0.5f) * u;
    }

    private void CycleFollowTarget()
    {
        m_Players.Clear();
        foreach (var p in FindObjectsByType<Player.PlayerUnit>(FindObjectsSortMode.InstanceID))
            if (p != null) m_Players.Add(p);
        if (m_Players.Count == 0) { Debug.Log("[TrailerCam] 따라갈 플레이어가 없음"); return; }

        int idx = -1;
        for (int i = 0; i < m_Players.Count; i++) if (m_Players[i].transform == m_Follow) { idx = i; break; }
        var next = m_Players[(idx + 1) % m_Players.Count];
        m_Follow = next.transform;
        m_FollowOffset = transform.position - m_Follow.position;   // 현재 구도 그대로 따라붙기
        Debug.Log($"[TrailerCam] 팔로우: {next.name} (owner {next.OwnerClientId})");
    }

    private void SetHudHidden(bool hidden)
    {
        m_HudHidden = hidden;
        var ui = UIManager.Instance;
        if (ui != null && ui.HUDRoot != null) ui.HUDRoot.SetActive(!hidden);
    }
}
#endif

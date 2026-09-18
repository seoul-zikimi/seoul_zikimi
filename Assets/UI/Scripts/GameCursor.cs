using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 게임 전역 마우스 커서: OS 기본 화살표 대신 크고 테두리 굵은 손가락(96px · Kenney Cursor Pack, CC0 — 원본 64px를 확대).
/// 기본 화살표가 작고 흰색이라 공사장 톤 배경에 묻혀 못 찾는다는 테스트 피드백 — 누르는 동안엔 쥔 손으로 바꿔 반응도 준다.
/// 하드웨어 커서(CursorMode.Auto)라 프레임이 떨어져도 끊기지 않는다.
/// 이미지(Resources/UI_pngs/Cursor)는 JobsnailUiTexturePostprocessor가 Sprite·무압축·밉맵 없음으로 고정한다 —
/// 커서로 쓰려면 거기에 Read/Write(isReadable)가 켜져 있어야 한다(.meta에 설정해 둠. 끄면 SetCursor가 거부한다).
/// </summary>
public class GameCursor : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (MobileControlsHUD.ShouldUseMobileUI) return;
        var go = new GameObject("~GameCursor");
        DontDestroyOnLoad(go);
        go.AddComponent<GameCursor>();
    }

    // 검지 끝(96px 이미지의 좌상단 기준 px). 쥔 손도 같은 값 — 누를 때 클릭 지점이 튀지 않고 손가락만 접힌 것처럼 보인다.
    private static readonly Vector2 kHotspot = new Vector2(28f, 7f);

    private Texture2D m_Point, m_Closed;
    private bool m_Pressed;

    private void Awake()
    {
        m_Point  = Resources.Load<Texture2D>("UI_pngs/Cursor/cursor_hand_point");
        m_Closed = Resources.Load<Texture2D>("UI_pngs/Cursor/cursor_hand_closed");
        Cursor.SetCursor(m_Point, kHotspot, CursorMode.Auto);
    }

    private void Update()
    {
        var mouse = Mouse.current;
        bool pressed = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
        if (pressed == m_Pressed) return;
        m_Pressed = pressed;
        Cursor.SetCursor(pressed ? m_Closed : m_Point, kHotspot, CursorMode.Auto);
    }
}

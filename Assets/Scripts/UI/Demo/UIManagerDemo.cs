using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// UIManager 동작 확인용 데모.
/// 이 스크립트를 씬의 빈 GameObject에 붙이면 됨.
/// UIManager 싱글톤이 같은 씬(또는 DontDestroyOnLoad)에 있어야 함.
/// </summary>
public class UIManagerDemo : MonoBehaviour
{
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.hKey.wasPressedThisFrame)      UIManager.Instance.ShowHUDUI<DemoHUD>();
        if (kb.gKey.wasPressedThisFrame)      UIManager.Instance.HideHUDUI<DemoHUD>();
        if (kb.pKey.wasPressedThisFrame)      UIManager.Instance.ShowPopupUI<DemoPopup>();
        if (kb.cKey.wasPressedThisFrame)      UIManager.Instance.ClosePopupUI();
        if (kb.sKey.wasPressedThisFrame)      UIManager.Instance.ShowSystemUI<DemoPopup>();
        if (kb.xKey.wasPressedThisFrame)      UIManager.Instance.CloseSystemUI();
        if (kb.escapeKey.wasPressedThisFrame) UIManager.Instance.CloseAllPopupUI();
    }

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 280, 168),
            "UIManager Demo\n\n" +
            "[H] Show HUD  [G] Hide HUD\n" +
            "[P] Show Popup  [C] Close Popup\n" +
            "[S] Show System  [X] Close System\n" +
            "[ESC] Close All Popups");
    }
}

using System;
using System.Collections.Generic;
using Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 실제 사용 가능한 PC 키 설정 팝업. 프리팹의 고정 행을 재사용하며 런타임 UI 오브젝트는 생성하지 않는다.
/// 메인 메뉴와 인게임 설정이 같은 팝업/PlayerPrefs override를 공유한다.
/// </summary>
public sealed class KeyBindingPopup : UIPopup
{
    private enum Buttons { CloseButton, ResetAllButton }
    private enum Texts { StatusText }

    private KeyBindingRow[] m_Rows;
    private KeyBindingRow m_WaitingRow;
    private int m_RebindVersion;

    public static bool IsOpen { get; private set; }

    public static KeyBindingPopup Open()
    {
        var existing = FindFirstObjectByType<KeyBindingPopup>();
        if (existing != null)
        {
            existing.NormalizePresentation();
            return existing;
        }
        if (Resources.Load<GameObject>("UI/Popup/KeyBindingPopup") == null)
        {
            Debug.LogError("[KeyBindingPopup] Resources/UI/Popup/KeyBindingPopup 프리팹이 없습니다.");
            return null;
        }
        if (UIManager.Instance == null)
            new GameObject("UIManager").AddComponent<UIManager>();
        return UIManager.Instance.ShowPopupUI<KeyBindingPopup>();
    }

    public override void Init()
    {
        NormalizePresentation();
        Bind<Button>(typeof(Buttons));
        Bind<TextMeshProUGUI>(typeof(Texts));

        Get<Button>((int)Buttons.CloseButton)?.onClick.AddListener(Close);
        Get<Button>((int)Buttons.ResetAllButton)?.onClick.AddListener(ResetAll);
        m_Rows = GetComponentsInChildren<KeyBindingRow>(true);
        Array.Sort(m_Rows, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        GameplayInputBindings.OverridesChanged += Refresh;
        GameplayInputBlocker.Blocked = true;
        IsOpen = true;
        Refresh();
    }

    private void NormalizePresentation()
    {
        // ScreenSpaceOverlay Canvas를 프리팹으로 저장하면 Unity가 루트 RectTransform의
        // scale/anchor를 0으로 직렬화할 수 있다. PopupRoot에 붙은 뒤 항상 화면 전체로 복구한다.
        if (transform is RectTransform rect)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 650;
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
        if (GameplayInputBindings.IsRebinding) return; // 리바인딩 중 ESC는 취소 키로 입력 시스템이 소비한다.
        Close();
    }

    public void BeginRebind(KeyBindingRow row, GameplayInputBindings.BindingInfo info)
    {
        int version = ++m_RebindVersion;
        m_WaitingRow?.SetWaiting(false);
        m_WaitingRow = row;
        row.SetWaiting(true);
        SetStatus(L.T("변경할 키를 누르세요. ESC를 누르면 취소됩니다.", "Press the new key. ESC to cancel."));

        if (!GameplayInputBindings.StartInteractiveRebind(info.ActionPath, info.BindingIndex, (success, _) =>
        {
            if (this == null || version != m_RebindVersion) return;
            m_WaitingRow?.SetWaiting(false);
            m_WaitingRow = null;
            SetStatus(success ? L.T("키가 저장되었습니다.", "Key saved.") : L.T("키 변경을 취소했습니다.", "Key change cancelled."));
            Refresh();
        }))
        {
            m_WaitingRow = null;
            row.SetWaiting(false);
            SetStatus(L.T("이 항목은 변경할 수 없습니다.", "This item can't be changed."));
        }
    }

    public void ResetBinding(GameplayInputBindings.BindingInfo info)
    {
        GameplayInputBindings.ResetBinding(info.ActionPath, info.BindingIndex);
        SetStatus(L.T("선택한 키를 기본값으로 되돌렸습니다.", "Selected key reset to default."));
    }

    private void ResetAll()
    {
        GameplayInputBindings.ResetAll();
        SetStatus(L.T("모든 키를 기본값으로 되돌렸습니다.", "All keys reset to default."));
    }

    private void Refresh()
    {
        if (m_Rows == null) return;
        IReadOnlyList<GameplayInputBindings.BindingInfo> all = GameplayInputBindings.GetBindings();
        int rowIndex = 0;
        for (int i = 0; i < all.Count && rowIndex < m_Rows.Length; i++)
        {
            if (!ShowOnPc(all[i])) continue;
            m_Rows[rowIndex++].Setup(this, all[i]);
        }
        for (int i = rowIndex; i < m_Rows.Length; i++)
            m_Rows[i].gameObject.SetActive(false);

        // 프리팹에는 편집 가능한 여유 행을 미리 두되, 실제 스크롤 길이는 표시 중인 행만큼만 맞춘다.
        if (m_Rows.Length > 0 && m_Rows[0].transform.parent is RectTransform content)
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowIndex * 58f + 8f);
    }

    private void SetStatus(string message)
    {
        var status = Get<TextMeshProUGUI>((int)Texts.StatusText);
        if (status != null) status.text = message;
    }

    private void Close()
    {
        ++m_RebindVersion;
        GameplayInputBindings.CancelInteractiveRebind();
        if (UIManager.Instance != null) UIManager.Instance.ClosePopupUI(this);
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        GameplayInputBindings.OverridesChanged -= Refresh;
        GameplayInputBindings.CancelInteractiveRebind();
        GameplayInputBlocker.Blocked = false;
        IsOpen = false;
    }

    public static bool ShowOnPc(GameplayInputBindings.BindingInfo info)
    {
        if (info.IsComposite || string.IsNullOrEmpty(info.EffectivePath)) return false;
        if (info.EffectivePath.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)) return false;
        // 카메라 회전 composite의 실제 delta는 고정하고, 누르고 있을 modifier 버튼만 변경한다.
        if (info.ActionPath == GameplayInputBindings.CameraRotate && info.BindingName == "binding") return false;
        return true;
    }

    public static string ActionLabel(GameplayInputBindings.BindingInfo info)
    {
        if (info.ActionPath == GameplayInputBindings.Move)
            return L.T("이동 - ", "Move - ") + (info.BindingName switch
            {
                "up" => L.T("위", "Up"), "down" => L.T("아래", "Down"), "left" => L.T("왼쪽", "Left"), "right" => L.T("오른쪽", "Right"), _ => info.BindingName
            });
        // 감정표현은 11줄이라 번호만 있으면 뭐가 뭔지 모른다 — 실제 대사를 붙여 준다.
        if (info.ActionPath.StartsWith("Player/Emote", StringComparison.Ordinal) && info.ActionName != "EmoteWheel")
        {
            string number = info.ActionName.Replace("Emote", "");
            return int.TryParse(number, out int n) && n >= 1 && n <= EmoteDefs.Count
                ? L.T($"감정표현 - {EmoteDefs.All[n - 1].Line}", $"Emote - {EmoteDefs.All[n - 1].Line}")
                : L.T("감정표현 ", "Emote ") + number;
        }
        return info.ActionPath switch
        {
            GameplayInputBindings.Sprint => L.T("달리기", "Run"),
            GameplayInputBindings.Jump => L.T("점프 / 비계", "Jump / Scaffold"),
            GameplayInputBindings.Interact => L.T("집기 / 배치", "Pick up / Place"),
            GameplayInputBindings.Process => L.T("공정 / 아이템 사용", "Process / Use item"),
            GameplayInputBindings.Revert => L.T("공정 취소", "Undo process"),
            GameplayInputBindings.RotateHeld => L.T("든 물건 회전", "Rotate held item"),
            GameplayInputBindings.Throw => L.T("던지기", "Throw"),
            GameplayInputBindings.ToggleOrder => L.T("휴대폰 / 주문 UI", "Phone / Order UI"),
            GameplayInputBindings.EmoteWheel => L.T("감정표현 메뉴", "Emote menu"),
            GameplayInputBindings.CameraRotate => L.T("카메라 회전", "Rotate camera"),
            GameplayInputBindings.CameraZoom => L.T("카메라 확대 / 축소", "Camera zoom"),
            _ => info.ActionName,
        };
    }

    public static string BindingLabel(GameplayInputBindings.BindingInfo info)
    {
        string path = info.EffectivePath;
        if (path == "<Mouse>/leftButton") return L.T("마우스 왼쪽", "Left mouse");
        if (path == "<Mouse>/rightButton") return L.T("마우스 오른쪽", "Right mouse");
        if (path == "<Mouse>/middleButton") return L.T("마우스 휠 클릭", "Mouse wheel click");
        if (path == "<Mouse>/scroll/y") return L.T("마우스 휠", "Mouse wheel");
        if (path == "<Keyboard>/leftShift") return L.T("왼쪽 Shift", "Left Shift");
        if (path == "<Keyboard>/rightShift") return L.T("오른쪽 Shift", "Right Shift");
        if (path == "<Keyboard>/space") return "Space";
        if (path == "<Keyboard>/tab") return "Tab";
        if (path == "<Keyboard>/escape") return "Esc";
        if (path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
            return path.Substring("<Keyboard>/".Length).ToUpperInvariant();
        return string.IsNullOrEmpty(info.DisplayString) ? path : info.DisplayString;
    }
}

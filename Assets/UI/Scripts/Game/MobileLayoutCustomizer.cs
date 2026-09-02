using System.Collections.Generic;
using Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 모바일 컨트롤 배치 커스터마이즈(배틀그라운드식) — '버튼 배치' 진입 → 버튼을 드래그로 이동 →
/// [완료]로 저장(PlayerPrefs, 기기별) / [초기화]로 프리팹 기본값 복원.
///
/// · 대상: 조작 버튼 7종(kTargets). 감정표현·고스트 토글은 드롭다운/상태 연동이 얽혀 제외.
/// · 저장값은 MobileControlsHUD.Init에서 ApplySaved로 입혀진다(프리팹 기본값은 초기화용으로 기억).
/// · 편집 중엔 월드 입력을 잠그고(WorldInputLocked) 버튼 자체 기능(Button·홀드·조이스틱)을 꺼서
///   드래그가 오발동을 일으키지 않게 한다. 상황 버튼 흐림도 Editing 플래그로 건너뛴다.
/// </summary>
public sealed class MobileLayoutCustomizer : MonoBehaviour
{
    private static readonly string[] kTargets =
        { "MoveJoystick", "JumpButton", "ThrowButton", "ProcessButton", "RevertButton", "RotateButton", "PhoneButton", "ItemButton" };
    private const string kPrefPrefix = "MobileUiPos_";

    /// <summary>편집 모드 중인가 — MobileControlsHUD가 상황 버튼 흐림/비활성 처리를 건너뛰는 데 쓴다.</summary>
    public static bool Editing { get; private set; }

    private readonly Dictionary<string, RectTransform> m_Targets = new();
    private readonly Dictionary<string, Vector2> m_Defaults = new();   // 프리팹 기본값(초기화용)
    private readonly List<Behaviour> m_Suspended = new();              // 편집 동안 꺼둔 입력 컴포넌트
    private readonly List<DragHandle> m_Handles = new();
    private GameObject m_Overlay, m_Toolbar;
    private Transform m_SafeArea;

    /// <summary>HUD 초기화 때 1회 — 대상 수집 + 기본값 기억 + 저장된 배치 적용. 지워진 버튼(비계 등)은 그냥 건너뛴다.</summary>
    public void ApplySaved(Transform safeArea)
    {
        m_SafeArea = safeArea;
        m_Targets.Clear(); m_Defaults.Clear();
        foreach (var name in kTargets)
        {
            var t = FindDeep(safeArea, name);
            if (t == null) continue;
            m_Targets[name] = t;
            m_Defaults[name] = t.anchoredPosition;
            if (PlayerPrefs.HasKey(kPrefPrefix + name + "_x"))
                t.anchoredPosition = new Vector2(
                    PlayerPrefs.GetFloat(kPrefPrefix + name + "_x"),
                    PlayerPrefs.GetFloat(kPrefPrefix + name + "_y"));
        }
    }

    public void BeginEdit()
    {
        if (Editing || m_SafeArea == null) return;
        Editing = true;
        MobileGameplayInput.WorldInputLocked = true;   // 드래그가 카메라/탭으로 새지 않게

        foreach (var kv in m_Targets)
        {
            // 버튼 고유 기능은 잠시 정지 — 드래그 중 점프·공정이 발동하면 안 된다
            foreach (var b in kv.Value.GetComponents<Behaviour>())
                if (b is Button || b is MobileHoldButton || b is MobileJoystickControl)
                { if (b.enabled) { b.enabled = false; m_Suspended.Add(b); } }

            // 흐림 상태여도 드래그는 되게 — CanvasGroup 잠금 해제(종료 후 UpdateActionStates가 재계산)
            var cg = kv.Value.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }

            var h = kv.Value.gameObject.AddComponent<DragHandle>();
            h.Target = kv.Value;
            m_Handles.Add(h);
        }
        BuildEditChrome();
    }

    private void EndEdit(bool save)
    {
        if (!Editing) return;
        if (save)
        {
            foreach (var kv in m_Targets)
            {
                PlayerPrefs.SetFloat(kPrefPrefix + kv.Key + "_x", kv.Value.anchoredPosition.x);
                PlayerPrefs.SetFloat(kPrefPrefix + kv.Key + "_y", kv.Value.anchoredPosition.y);
            }
            PlayerPrefs.Save();
        }
        foreach (var h in m_Handles) if (h != null) Destroy(h);
        m_Handles.Clear();
        foreach (var b in m_Suspended) if (b != null) b.enabled = true;
        m_Suspended.Clear();
        if (m_Overlay != null) Destroy(m_Overlay);
        if (m_Toolbar != null) Destroy(m_Toolbar);
        MobileGameplayInput.WorldInputLocked = false;
        Editing = false;
    }

    private void ResetToDefaults()
    {
        foreach (var kv in m_Defaults)
        {
            if (m_Targets.TryGetValue(kv.Key, out var t)) t.anchoredPosition = kv.Value;
            PlayerPrefs.DeleteKey(kPrefPrefix + kv.Key + "_x");
            PlayerPrefs.DeleteKey(kPrefPrefix + kv.Key + "_y");
        }
        PlayerPrefs.Save();
    }

    // ── 편집 모드 UI(어둡게 + 안내 + 완료/초기화) ─────────────────────────
    private void BuildEditChrome()
    {
        // 어둡게: SafeArea 뒤(형제 index 0) — 버튼들이 위에 남아 드래그 가능, 월드만 죽어 보인다
        m_Overlay = new GameObject("~LayoutEditDim", typeof(RectTransform)) { layer = 5 };
        var ort = (RectTransform)m_Overlay.transform;
        ort.SetParent(m_SafeArea.parent, false);
        ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one; ort.sizeDelta = Vector2.zero;
        ort.SetSiblingIndex(m_SafeArea.GetSiblingIndex());
        var dim = m_Overlay.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);
        dim.raycastTarget = false;   // 터치는 통과 — 잡을 건 버튼뿐

        m_Toolbar = new GameObject("~LayoutEditBar", typeof(RectTransform)) { layer = 5 };
        var brt = (RectTransform)m_Toolbar.transform;
        brt.SetParent(m_SafeArea, false);
        brt.anchorMin = new Vector2(0.5f, 1f); brt.anchorMax = new Vector2(0.5f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -24f);
        brt.sizeDelta = new Vector2(900f, 150f);

        MakeLabel(brt, "버튼을 드래그해 원하는 위치로 옮기세요", new Vector2(0f, -8f), new Vector2(900f, 44f), 30);
        MakePill(brt, "초기화", new Vector2(-130f, -92f), () => ResetToDefaults());
        MakePill(brt, "완료",  new Vector2(130f, -92f), () => EndEdit(save: true));
    }

    private static void MakeLabel(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize)
    {
        var go = new GameObject("Label", typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var l = go.AddComponent<TextMeshProUGUI>();
        l.text = text; l.font = JobsnailUiKit.TmpFont; l.fontSize = fontSize;
        l.fontStyle = FontStyles.Bold; l.color = Color.white;
        l.alignment = TextAlignmentOptions.Center; l.raycastTarget = false;
    }

    private static void MakePill(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(text, typeof(RectTransform)) { layer = 5 };
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(220f, 70f);
        var img = go.AddComponent<Image>();
        img.sprite = JobsnailUiKit.Sprite("UI_pngs/MyPage/RoundRect");
        img.type = Image.Type.Sliced;
        img.color = new Color(0.94f, 0.94f, 0.93f, 0.97f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        var label = new GameObject("Label", typeof(RectTransform)) { layer = 5 };
        var lrt = (RectTransform)label.transform;
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;
        var l = label.AddComponent<TextMeshProUGUI>();
        l.text = text; l.font = JobsnailUiKit.TmpFont; l.fontSize = 28;
        l.fontStyle = FontStyles.Bold; l.color = new Color(0.2f, 0.2f, 0.19f, 1f);
        l.alignment = TextAlignmentOptions.Center; l.raycastTarget = false;
    }

    private static RectTransform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t as RectTransform;
        return null;
    }

    /// <summary>드래그 핸들 — Overlay 캔버스라 스크린 델타를 그대로 position에 더하면 스케일 무관하게 따라온다.</summary>
    private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Target;
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e)
        {
            if (Target != null) Target.position += (Vector3)e.delta;
        }
    }
}

using System;
using GridSystem;
using Player;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// T키 꾹 → 오버워치식 원형 감정표현 휠(기획서 '인게임 소통 수단 시스템').
/// 섹터는 EmoteDefs 대사 목록에서 Init 때 런타임 생성 — 대사를 바꾸면 UI도 자동 반영(프리팹 재생성 불필요).
/// 마우스 '방향'으로 섹터 하이라이트, T를 떼면 하이라이트된 대사 발동(클릭 없이) — 클릭 즉시 발동도 지원.
/// UIManager가 Resources/UI/HUD/EmoteWheelUI 프리팹에서 인스턴스화(원판/링 배경만 프리팹에 존재).
/// 프리팹 생성: Jobsnail ▸ UI ▸ Generate EmoteWheel Prefab.
/// </summary>
public class EmoteWheelUI : UIHUD
{
    /// <summary>선택 콜백(EmoteDefs 인덱스). PlayerEmote가 구독.</summary>
    public Action<int> OnPick;

    /// <summary>현재 마우스 방향이 가리키는 섹터(-1 = 중앙 데드존).</summary>
    public int HoverIndex { get; private set; } = -1;

    private RectTransform m_Wheel;
    private RectTransform[] m_Items;
    private const float kDeadZone = 80f;   // 중앙 데드존 반지름(캔버스 px) — 이 안이면 선택 없음
    private const float kRadius = 235f;    // 대사 버튼 배치 반지름

    public override void Init()
    {
        m_Wheel = transform.Find("Wheel") as RectTransform;
        if (m_Wheel == null) { Debug.LogWarning("[EmoteWheelUI] Wheel 없음"); return; }

        // 구세대 프리팹 잔재(이모지 버튼·살 경계선) 제거 — 섹터는 아래서 EmoteDefs 기준으로 다시 만든다
        for (int i = m_Wheel.childCount - 1; i >= 0; i--)
        {
            var child = m_Wheel.GetChild(i);
            if (child.name.StartsWith("Emote") || child.name == "Spoke")
                Destroy(child.gameObject);
        }

        int n = EmoteDefs.Count;
        float step = 360f / n;
        m_Items = new RectTransform[n];

        // 살 경계선(섹터 사이) — 얇은 흰 선
        for (int i = 0; i < n; i++)
        {
            float deg = 90f - (i * step) - step * 0.5f;   // 버튼 사이 경계각
            var line = new GameObject("SpokeLine", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(m_Wheel, false);
            var lrt = (RectTransform)line.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(190f, 2f);
            float mid = (78f + 268f) * 0.5f;
            lrt.anchoredPosition = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * mid;
            lrt.localRotation = Quaternion.Euler(0f, 0f, deg);
            var li = line.GetComponent<Image>();
            li.color = new Color(1f, 1f, 1f, 0.16f);
            li.raycastTarget = false;
        }

        // 대사 버튼 N방향 — 흰 대사 텍스트 + 투명 히트영역
        for (int i = 0; i < n; i++)
        {
            int idx = i;   // 클로저 캡처
            float ang = (90f - i * step) * Mathf.Deg2Rad;
            var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * kRadius;

            var go = new GameObject($"EmoteBtn{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(m_Wheel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(150f, 70f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);   // 투명 히트영역(호버·클릭용)
            hit.raycastTarget = true;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                OnPick?.Invoke(idx);
            });
            m_Items[i] = rt;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var trt = (RectTransform)label.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(150f, 60f);
            var t = label.GetComponent<Text>();
            t.font = JobsnailUiKit.LegacyFont;
            t.text = EmoteDefs.All[i].Line;
            t.fontSize = 20;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
        }

        gameObject.SetActive(false);   // 기본 숨김 — T 홀드 때만 표시
    }

    private void OnEnable()
    {
        HoverIndex = -1;

        // 안전망: GameLoopManager는 GameScene(실제 인게임)에만 존재. PlayerEmote 게이트를 못 뚫고 들어와도
        // 로비 씬에서 휠이 표시되면 여기서 즉시 닫는다(예: PlayerEmote 수정 누락·버그 대비).
        if (FindFirstObjectByType<GameLoopManager>() == null)
            gameObject.SetActive(false);
    }

    private void Update()
    {
        if (m_Wheel == null || m_Items == null) return;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        int n = m_Items.Length;
        float step = 360f / n;

        // 오버레이 캔버스 → 카메라 null. 휠 중심 기준 로컬 좌표로 방향 산출.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Wheel, mouse.position.ReadValue(), null, out Vector2 p);

        int hover = -1;
        if (p.magnitude >= kDeadZone)
        {
            float deg = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;          // 0=오른쪽, 90=위
            int idx = Mathf.RoundToInt((90f - deg) / step);             // 12시=0, 시계방향 step° 간격
            hover = ((idx % n) + n) % n;
        }
        HoverIndex = hover;

        // 하이라이트: 가리킨 섹터 확대(스르륵)
        for (int i = 0; i < n; i++)
        {
            if (m_Items[i] == null) continue;
            float target = i == hover ? 1.25f : 1f;
            m_Items[i].localScale = Vector3.one * Mathf.MoveTowards(m_Items[i].localScale.x, target, Time.unscaledDeltaTime * 6f);
        }
    }
}

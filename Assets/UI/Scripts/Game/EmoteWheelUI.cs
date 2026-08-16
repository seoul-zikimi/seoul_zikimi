using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// T키 꾹 → 오버워치식 원형 이모트 휠. 마우스 '방향'으로 섹터 하이라이트,
/// T를 떼면 하이라이트된 이모트 발동(클릭 없이) — 클릭 즉시 발동도 지원.
/// UIManager가 Resources/UI/HUD/EmoteWheelUI 프리팹에서 인스턴스화.
/// 프리팹 생성: Jobsnail ▸ UI ▸ Generate EmoteWheel Prefab.
/// </summary>
public class EmoteWheelUI : UIHUD
{
    private enum Btns { Emote0, Emote1, Emote2, Emote3, Emote4, Emote5, Emote6, Emote7, Emote8, Emote9 }

    /// <summary>선택 콜백(0~9). PlayerEmote가 구독.</summary>
    public Action<int> OnPick;

    /// <summary>현재 마우스 방향이 가리키는 섹터(-1 = 중앙 데드존).</summary>
    public int HoverIndex { get; private set; } = -1;

    private RectTransform m_Wheel;
    private readonly RectTransform[] m_Items = new RectTransform[10];
    private const float kDeadZone = 80f;   // 중앙 데드존 반지름(캔버스 px) — 이 안이면 선택 없음

    public override void Init()
    {
        Bind<Button>(typeof(Btns));
        for (int i = 0; i < 10; i++)
        {
            int idx = i;   // 클로저 캡처
            var btn = Get<Button>(i);
            if (btn == null) continue;
            m_Items[i] = btn.transform as RectTransform;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SFXType.UIClick);
                OnPick?.Invoke(idx);
            });
        }
        m_Wheel = transform.Find("Wheel") as RectTransform;
        gameObject.SetActive(false);   // 기본 숨김 — T 홀드 때만 표시
    }

    private void OnEnable() => HoverIndex = -1;

    private void Update()
    {
        if (m_Wheel == null) return;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;

        // 오버레이 캔버스 → 카메라 null. 휠 중심 기준 로컬 좌표로 방향 산출.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Wheel, mouse.position.ReadValue(), null, out Vector2 p);

        int hover = -1;
        if (p.magnitude >= kDeadZone)
        {
            float deg = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;          // 0=오른쪽, 90=위
            int idx = Mathf.RoundToInt((90f - deg) / 36f);              // 12시=0, 시계방향 36° 간격
            hover = ((idx % 10) + 10) % 10;
        }
        HoverIndex = hover;

        // 하이라이트: 가리킨 섹터 확대(스르륵)
        for (int i = 0; i < 10; i++)
        {
            if (m_Items[i] == null) continue;
            float target = i == hover ? 1.25f : 1f;
            m_Items[i].localScale = Vector3.one * Mathf.MoveTowards(m_Items[i].localScale.x, target, Time.unscaledDeltaTime * 6f);
        }
    }
}

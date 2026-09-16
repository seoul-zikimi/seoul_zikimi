using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 영어 모드에서 프리팹에 박힌 한글 텍스트·이미지를 런타임에 바꿔치기한다 — 프리팹은 손대지 않는다.
///  · TMP_Text / Text: 텍스트가 <see cref="LocTables.Text"/>(한글 → 영어) 표에 있으면 교체.
///    코드가 나중에 같은 한글을 다시 써도 다음 프레임에 또 교체된다(마지막으로 본 문자열 참조를 기억).
///  · Image: 스프라이트 이름이 <see cref="LocTables.SpritePath"/>에 있으면 Resources의 영어판(_en)으로 교체.
///    코드가 준비/준비완료 같은 스프라이트를 갈아 끼워도 다음 프레임에 잡힌다.
/// 캔버스가 그려지기 직전(Canvas.willRenderCanvases)마다 돈다 — UI 개체 수백 개 기준 비용은 무시할 만하다.
/// 한국어 모드에서는 아무것도 하지 않는다(영→한 전환은 씬 재로드로 원본 프리팹이 다시 뜬다).
/// </summary>
public static class LocalizedUI
{
    private static readonly Dictionary<int, string> s_LastText = new();
    private static readonly Dictionary<int, Sprite> s_SpriteCache = new();   // 원본 스프라이트 id → 영어판(없으면 null)
    private static bool s_Hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_Hooked) return;
        s_Hooked = true;
        Canvas.willRenderCanvases += Tick;
        SceneManager.sceneLoaded += (_, __) => { s_LastText.Clear(); Tick(); };
        L.Changed += () => s_LastText.Clear();
        Tick();
    }

    /// <summary>즉시 한 번 돌린다(팝업을 막 띄운 직후 첫 프레임 깜빡임을 피하고 싶을 때).</summary>
    public static void Refresh() => Tick();

    private static void Tick()
    {
        if (!L.En) return;

        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string cur = t.text;
            if (string.IsNullOrEmpty(cur)) continue;
            int id = t.GetInstanceID();
            if (s_LastText.TryGetValue(id, out var last) && ReferenceEquals(last, cur)) continue;
            if (TryTranslate(cur, out var en)) { t.text = en; cur = t.text; }
            s_LastText[id] = cur;
        }

        foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string cur = t.text;
            if (string.IsNullOrEmpty(cur)) continue;
            int id = t.GetInstanceID();
            if (s_LastText.TryGetValue(id, out var last) && ReferenceEquals(last, cur)) continue;
            if (TryTranslate(cur, out var en)) { t.text = en; cur = t.text; }
            s_LastText[id] = cur;
        }

        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var sp = img.sprite;
            if (sp == null) continue;
            int sid = sp.GetInstanceID();
            if (!s_SpriteCache.TryGetValue(sid, out var en))
            {
                en = LocTables.SpritePath.TryGetValue(sp.name, out var path) ? Resources.Load<Sprite>(path) : null;
                s_SpriteCache[sid] = en;
            }
            if (en != null) img.sprite = en;
        }
    }

    private static bool TryTranslate(string ko, out string en)
    {
        if (LocTables.Text.TryGetValue(ko, out en)) return true;
        var trimmed = ko.Trim();
        if (!ReferenceEquals(trimmed, ko) && trimmed.Length != ko.Length && LocTables.Text.TryGetValue(trimmed, out en)) return true;
        en = null;
        return false;
    }
}

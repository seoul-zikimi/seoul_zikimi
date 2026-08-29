using UnityEngine;

/// <summary>
/// 선택 가능한 플레이어 캐릭터 목록 — id 접두사 char_, 빈 id = 기본(달팽이).
/// 모델 프리팹: Resources/Characters/&lt;id&gt;.prefab (제작: Tools ▸ Character ▸ 거북이 캐릭터 생성).
/// 썸네일: Resources/UI_pngs/MyPage/Thumb_&lt;id&gt; (이름 컨벤션, 없으면 라벨만).
/// </summary>
public static class CharacterCatalog
{
    public readonly struct Entry
    {
        public readonly string Id;          // "" = 기본(달팽이, 프리팹 교체 없음)
        public readonly string DisplayName;
        public readonly int Price;          // 옷장 해금 가격(코인) — 인트로에서 고른 캐릭터는 무료 지급
        public Entry(string id, string display, int price = 300) { Id = id; DisplayName = display; Price = price; }
    }

    // 새 캐릭터 추가 시 여기에 한 줄 + Resources/Characters 프리팹만 만들면 옷장에 뜬다.
    public static readonly Entry[] All =
    {
        new("", "달팽이"),
        new("char_turtle", "거북이"),
        new("char_crab", "게"),
    };

    /// <summary>id의 화면 표시 이름(모르는 id면 id 그대로).
    /// 표시 이름은 이 카탈로그가 유일한 원본 — 화면마다 따로 하드코딩하지 말 것
    /// (인트로는 "소라게", 옷장은 "게"로 갈렸던 적이 있다).</summary>
    public static string DisplayName(string id)
    {
        foreach (var entry in All)
            if (entry.Id == id) return entry.DisplayName;
        return id;
    }

    public static GameObject LoadPrefab(string id)
        => string.IsNullOrEmpty(id) ? null : Resources.Load<GameObject>("Characters/" + id);

    public static Sprite LoadThumbnail(string id)
        => Resources.Load<Sprite>("UI_pngs/MyPage/Thumb_" + (string.IsNullOrEmpty(id) ? "default" : id));
}

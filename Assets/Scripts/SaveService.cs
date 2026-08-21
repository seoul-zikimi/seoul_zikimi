using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 저장 시스템 (Easy Save 3) — 기획: 로컬 저장, 항목 변경 시마다 즉시 자동저장.
/// 저장 항목: 닉네임 / 코인 / 코디 아이템 목록 / 캐릭터 스킨 목록 / 맵별×인원수별 최고기록.
/// 스팀 클라우드 백업은 추후(로컬 파일 save.es3 하나라 그대로 올리면 됨).
/// </summary>
public static class SaveService
{
    private const string kFile = "save.es3";
    private const string kNick = "nickname";
    private const string kCoins = "coins";
    private const string kCodi = "codiItems";
    private const string kSkins = "skins";

    // ── 닉네임 (변경 시 저장) ──
    public static string Nickname
    {
        get => ES3.Load(kNick, kFile, PlayerPrefs.GetString("PlayerNickname", ""));   // 구버전(PlayerPrefs) 마이그레이션 폴백
        set
        {
            ES3.Save(kNick, value ?? "", kFile);
            PlayerPrefs.SetString("PlayerNickname", value ?? "");   // 기존 판독처(NGO 이름 등록 등) 호환
            PlayerPrefs.Save();
        }
    }

    // ── 코인 (증가/감소 시 저장 — 게임 종료 보상, 아이템 구매) ──
    public static int Coins => ES3.Load(kCoins, kFile, 0);

    public static void AddCoins(int delta) => ES3.Save(kCoins, Mathf.Max(0, Coins + delta), kFile);

    /// <summary>잔액 충분하면 차감 후 true(저장 포함).</summary>
    public static bool TrySpendCoins(int price)
    {
        if (price < 0 || Coins < price) return false;
        ES3.Save(kCoins, Coins - price, kFile);
        return true;
    }

    // ── 보유 코디 아이템 (구매 시 저장) ──
    public static List<string> CodiItems => ES3.Load(kCodi, kFile, new List<string>());
    public static bool HasCodiItem(string id) => CodiItems.Contains(id);

    public static bool BuyCodiItem(string id, int price)
    {
        if (string.IsNullOrEmpty(id) || HasCodiItem(id) || !TrySpendCoins(price)) return false;
        var list = CodiItems;
        list.Add(id);
        ES3.Save(kCodi, list, kFile);
        return true;
    }

    // ── 착용 중 아웃핏(의상 세트) — 한 번에 하나, 빈 문자열 = 기본 모습 ──
    public static string EquippedOutfit
    {
        get => ES3.Load("equippedOutfit", kFile, "");
        set => ES3.Save("equippedOutfit", value ?? "", kFile);
    }

    // ── 착용 중 트레일(이동 이펙트) — 아웃핏과 독립, 빈 문자열 = 없음. 예: trail_fire ──
    public static string EquippedTrail
    {
        get => ES3.Load("equippedTrail", kFile, "");
        set => ES3.Save("equippedTrail", value ?? "", kFile);
    }

    // ── 선택 중 캐릭터 — 빈 문자열 = 기본(달팽이). 예: char_turtle ──
    public static string EquippedCharacter
    {
        get => ES3.Load("equippedCharacter", kFile, "");
        set => ES3.Save("equippedCharacter", value ?? "", kFile);
    }

    // ── 인트로 컷씬(최초 실행 연출 + 초기 캐릭터 선택) 완료 여부 ──
    public static bool IntroSeen
    {
        get => ES3.Load("introSeen", kFile, false);
        set => ES3.Save("introSeen", value, kFile);
    }

    // ── 보유 캐릭터 — 인트로에서 고른 1명 무료, 나머지는 옷장에서 코인 해금.
    //    빈 id(달팽이)는 리스트에 "default" 토큰으로 저장.
    private const string kCharacters = "ownedCharacters";
    private static string CharToken(string id) => string.IsNullOrEmpty(id) ? "default" : id;

    public static List<string> OwnedCharacters => ES3.Load(kCharacters, kFile, new List<string>());

    public static bool HasCharacter(string id)
        => !IntroSeen || OwnedCharacters.Contains(CharToken(id));   // 인트로 전(기존 세이브 포함)엔 전부 개방 유지

    public static void GrantCharacter(string id)
    {
        var list = OwnedCharacters;
        if (list.Contains(CharToken(id))) return;
        list.Add(CharToken(id));
        ES3.Save(kCharacters, list, kFile);
    }

    public static bool BuyCharacter(string id, int price)
    {
        if (OwnedCharacters.Contains(CharToken(id)) || !TrySpendCoins(price)) return false;
        GrantCharacter(id);
        return true;
    }

    // ── 보유 캐릭터 스킨 (구매 시 저장) — 선행조건(캐릭터 보유 등) 검사는 상점 쪽 책임 ──
    public static List<string> Skins => ES3.Load(kSkins, kFile, new List<string>());
    public static bool HasSkin(string id) => Skins.Contains(id);

    public static bool BuySkin(string id, int price)
    {
        if (string.IsNullOrEmpty(id) || HasSkin(id) || !TrySpendCoins(price)) return false;
        var list = Skins;
        list.Add(id);
        ES3.Save(kSkins, list, kFile);
        return true;
    }

    // ── 맵별×인원수별 최고 기록 (갱신 시 저장) ──
    // 인원수는 게임 시작 시 팀원 수 기준(중도 이탈해도 그대로). 미플레이는 "-" 표기.
    private static string BestKey(string map, int players) => $"best_{map}_{players}p";

    public static bool TryGetBest(string map, int players, out int pct, out float seconds)
    {
        pct = 0;
        seconds = 0f;
        string key = BestKey(map, players);
        try
        {
            if (!ES3.KeyExists(key + "_pct", kFile)) return false;
            pct = ES3.Load(key + "_pct", kFile, 0);
            seconds = ES3.Load(key + "_sec", kFile, 0f);
            return true;
        }
        catch (System.IO.IOException) { pct = 0; seconds = 0f; return false; }   // MPPM 등 동시 접근 시 방어
    }

    /// <summary>기록 표시용: 미플레이 "-", 그 외 "58% 3:00".</summary>
    public static string FormatBest(string map, int players)
    {
        if (!TryGetBest(map, players, out int pct, out float sec)) return "-";
        int s = Mathf.RoundToInt(sec);
        return $"{pct}% {s / 60}:{s % 60:00}";
    }

    /// <summary>게임 결과 보고 — 최고기록이면 저장 후 true. 완성도 우선, 동률이면 짧은 시간 승.</summary>
    public static bool ReportRecord(string map, int players, int pct, float seconds)
    {
        if (string.IsNullOrEmpty(map)) return false;
        players = Mathf.Clamp(players, 1, 4);
        if (TryGetBest(map, players, out int oldPct, out float oldSec))
        {
            bool better = pct > oldPct || (pct == oldPct && seconds < oldSec);
            if (!better) return false;
        }
        string key = BestKey(map, players);
        ES3.Save(key + "_pct", pct, kFile);
        ES3.Save(key + "_sec", seconds, kFile);
        return true;
    }

    // ── 2VS2 대전 전적(맵 단위 승/패 누적) ──
    private static string VersusKey(string map) => $"versus_{map}";

    public static void GetVersus(string map, out int wins, out int losses)
    {
        wins = 0;
        losses = 0;
        if (string.IsNullOrEmpty(map)) return;
        string key = VersusKey(map);
        try
        {
            wins = ES3.Load(key + "_w", kFile, 0);
            losses = ES3.Load(key + "_l", kFile, 0);
        }
        catch (System.IO.IOException) { wins = 0; losses = 0; }   // MPPM 등 동시 접근 시 방어
    }

    /// <summary>2vs2 결과 보고 — 해당 맵의 승 또는 패 카운트를 1 증가시킨다.</summary>
    public static void ReportVersus(string map, bool won)
    {
        if (string.IsNullOrEmpty(map)) return;
        string key = VersusKey(map);
        int w = ES3.Load(key + "_w", kFile, 0);
        int l = ES3.Load(key + "_l", kFile, 0);
        if (won) w++; else l++;
        ES3.Save(key + "_w", w, kFile);
        ES3.Save(key + "_l", l, kFile);
    }

    /// <summary>전적 표시용: "5승 2패".</summary>
    public static string FormatVersus(string map)
    {
        GetVersus(map, out int w, out int l);
        return $"{w}승 {l}패";
    }

    // ── 타임어택 보상표(커스터마이징 기획 문서 기준) ──
    // 0% → 0 / 100% → 200 / 별0 → pct / 별1 → pct+20 / 별2 → pct+50 / 별3 → pct+80
    public static int TimeAttackReward(int pct, int stars)
    {
        if (pct <= 0) return 0;
        if (pct >= 100) return 200;
        int bonus = stars <= 0 ? 0 : stars == 1 ? 20 : stars == 2 ? 50 : 80;
        return pct + bonus;
    }
}

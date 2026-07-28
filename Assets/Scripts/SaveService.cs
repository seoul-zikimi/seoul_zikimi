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
    private const string kTutorialPromptDontShow = "tutorialPromptDontShow";
    private const string kTutorialCompleted = "tutorialCompleted";

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
        string key = BestKey(map, players);
        if (!ES3.KeyExists(key + "_pct", kFile)) { pct = 0; seconds = 0f; return false; }
        pct = ES3.Load(key + "_pct", kFile, 0);
        seconds = ES3.Load(key + "_sec", kFile, 0f);
        return true;
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

    // ── 튜토리얼 안내 팝업 "다시 보지 않음" 여부 (체크 시 저장) ──
    public static bool TutorialPromptDontShow
    {
        get => ES3.Load(kTutorialPromptDontShow, kFile, false);
        set => ES3.Save(kTutorialPromptDontShow, value, kFile);
    }

    // ── 튜토리얼 완주 여부 (완료 시 저장) ──
    public static bool TutorialCompleted
    {
        get => ES3.Load(kTutorialCompleted, kFile, false);
        set => ES3.Save(kTutorialCompleted, value, kFile);
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

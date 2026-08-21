using UnityEditor;
using UnityEngine;

/// <summary>개발용 코인 치트 — Jobsnail ▸ Debug ▸ 코인 +10000.</summary>
public static class CoinCheatMenu
{
    [MenuItem("Jobsnail/Debug/코인 +10000")]
    private static void Add10000()
    {
        SaveService.AddCoins(10000);
        Debug.Log($"[Cheat] 코인 +10000 → 현재 {SaveService.Coins}");
    }
}

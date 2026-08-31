using System;
using System.Collections.Generic;
using SeoulZikimi.Gameplay;
using UnityEngine;

/// <summary>로딩 화면 팁 데이터. Resources/LoadingTips.csv(탭 구분)를 읽는다 —
/// 기획 엑셀(Assets/Docs/LoadingTips.xlsx) 수정 후 Tools ▸ UI ▸ 로딩 팁 갱신으로 재생성.
///
/// 노출 규칙: 후보 = 현재 맵의 MAP 팁 + 현재 모드의 MODE 팁(Enabled=TRUE만),
/// 그중 Weight 가중치 랜덤으로 1개. 후보가 없으면 null(호출부가 팁 박스를 숨긴다).</summary>
public static class LoadingTips
{
    private struct Tip
    {
        public string Category;   // MAP / MODE
        public string TargetKey;  // MapDef 에셋 이름(Map_Ddp…) 또는 GameModeKind 이름(TeamVersus…)
        public string Text;
        public int Weight;
    }

    private static List<Tip> s_Tips;

    public static string Pick(string mapDefName, GameModeKind mode)
    {
        Load();
        var pool = new List<Tip>();
        foreach (var t in s_Tips)
        {
            bool match = t.Category == "MAP"
                ? string.Equals(t.TargetKey, mapDefName, StringComparison.OrdinalIgnoreCase)
                : t.Category == "MODE" && string.Equals(t.TargetKey, mode.ToString(), StringComparison.OrdinalIgnoreCase);
            if (match) pool.Add(t);
        }
        if (pool.Count == 0)
            return null;

        int total = 0;
        foreach (var t in pool) total += t.Weight;
        int roll = UnityEngine.Random.Range(0, total);
        foreach (var t in pool)
        {
            roll -= t.Weight;
            if (roll < 0) return t.Text;
        }
        return pool[pool.Count - 1].Text;
    }

    private static void Load()
    {
        if (s_Tips != null)
            return;
        s_Tips = new List<Tip>();
        var asset = Resources.Load<TextAsset>("LoadingTips");
        if (asset == null)
        {
            Debug.LogWarning("[LoadingTips] Resources/LoadingTips.csv 없음 — 팁 미표시.");
            return;
        }

        var lines = asset.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)   // 0행 = 헤더
        {
            var cols = lines[i].TrimEnd('\r').Split('\t');
            if (cols.Length < 6 || string.IsNullOrWhiteSpace(cols[0]))
                continue;
            if (!cols[5].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                continue;
            s_Tips.Add(new Tip
            {
                Category = cols[1].Trim().ToUpperInvariant(),
                TargetKey = cols[2].Trim(),
                Text = cols[3].Trim(),
                Weight = int.TryParse(cols[4], out var w) && w > 0 ? w : 1,
            });
        }
    }
}

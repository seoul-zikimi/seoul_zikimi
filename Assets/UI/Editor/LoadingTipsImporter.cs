using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;

/// <summary>기획 엑셀(Assets/Docs/LoadingTips.xlsx)의 'Tips' 시트를
/// Resources/LoadingTips.csv(탭 구분, 런타임 LoadingTips가 읽는 파일)로 변환한다.
///
/// 엑셀 규칙(작성가이드 시트와 동일): 1행 헤더·2행 설명 → 3행부터 데이터,
/// 컬럼 A~F = TipID/Category/TargetKey/Text/Weight/Enabled. TipID 빈 행 무시.</summary>
public static class LoadingTipsImporter
{
    private const string kXlsxPath = "Assets/Docs/LoadingTips.xlsx";
    private const string kCsvPath = "Assets/Resources/LoadingTips.csv";

    [MenuItem("Tools/UI/로딩 팁 갱신 (Docs∕LoadingTips.xlsx → csv)")]
    public static void Import()
    {
        if (!File.Exists(kXlsxPath))
        {
            EditorUtility.DisplayDialog("로딩 팁 갱신", $"{kXlsxPath} 가 없습니다.\n기획 엑셀을 그 경로에 넣어주세요.", "확인");
            return;
        }

        List<string[]> rows;
        using (var fs = File.OpenRead(kXlsxPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            rows = ReadSheetRows(zip, "Tips");

        var sb = new StringBuilder();
        sb.Append("TipID\tCategory\tTargetKey\tText\tWeight\tEnabled\n");
        int count = 0;
        foreach (var cols in rows)
        {
            if (cols.Length == 0 || string.IsNullOrWhiteSpace(cols[0]))
                continue;
            var vals = new string[6];
            for (int i = 0; i < 6; i++)
                vals[i] = (i < cols.Length && cols[i] != null ? cols[i] : "").Replace('\t', ' ').Replace('\n', ' ').Replace("\r", "");
            sb.Append(string.Join("\t", vals)).Append('\n');
            count++;
        }

        File.WriteAllText(kCsvPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log($"[LoadingTipsImporter] {count}개 팁 → {kCsvPath} 갱신 완료.");
    }

    /// <summary>xlsx(zip) 안에서 이름이 sheetName인 시트의 3행 이후를 [행][열] 문자열로 읽는다.</summary>
    private static List<string[]> ReadSheetRows(ZipArchive zip, string sheetName)
    {
        // 시트 이름 → 시트 xml 경로 (workbook.xml의 r:id → workbook.xml.rels의 Target)
        string rid = null;
        var wbXml = LoadXml(zip, "xl/workbook.xml");
        foreach (XmlNode n in wbXml.GetElementsByTagName("sheet"))
            if (n.Attributes["name"]?.Value == sheetName)
                rid = n.Attributes["r:id"]?.Value ?? n.Attributes["id"]?.Value;
        if (rid == null)
            throw new FileNotFoundException($"'{sheetName}' 시트를 찾을 수 없습니다.");

        string target = null;
        var relXml = LoadXml(zip, "xl/_rels/workbook.xml.rels");
        foreach (XmlNode n in relXml.GetElementsByTagName("Relationship"))
            if (n.Attributes["Id"]?.Value == rid)
                target = n.Attributes["Target"]?.Value;
        if (target == null)
            throw new FileNotFoundException($"시트 관계(rid={rid})를 찾을 수 없습니다.");
        string sheetPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;

        // 공유 문자열 테이블(텍스트 셀은 t="s"로 여기 인덱스를 가리킨다)
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") != null)
        {
            var ssXml = LoadXml(zip, "xl/sharedStrings.xml");
            foreach (XmlNode si in ssXml.DocumentElement.ChildNodes)
                shared.Add(si.InnerText);
        }

        var rows = new List<string[]>();
        var sheetXml = LoadXml(zip, sheetPath);
        foreach (XmlNode rowNode in sheetXml.GetElementsByTagName("row"))
        {
            int rowIdx = int.Parse(rowNode.Attributes["r"].Value);
            if (rowIdx < 3)   // 1행 헤더, 2행 설명
                continue;
            var cols = new string[6];
            foreach (XmlNode c in rowNode.ChildNodes)
            {
                if (c.Name != "c") continue;
                int colIdx = ColumnIndex(c.Attributes["r"]?.Value);
                if (colIdx < 0 || colIdx >= 6) continue;
                string type = c.Attributes["t"]?.Value;
                string v = null;
                foreach (XmlNode child in c.ChildNodes)
                {
                    if (child.Name == "v") v = child.InnerText;
                    else if (child.Name == "is") v = child.InnerText;   // inline string
                }
                if (v == null) continue;
                cols[colIdx] = type == "s" && int.TryParse(v, out var si) && si < shared.Count ? shared[si] : v;
            }
            rows.Add(cols);
        }
        return rows;
    }

    private static XmlDocument LoadXml(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new FileNotFoundException($"xlsx 안에 {entryName} 이 없습니다.");
        var doc = new XmlDocument();
        using (var s = entry.Open())
            doc.Load(s);
        return doc;
    }

    /// <summary>"D5" 같은 셀 참조에서 0 기준 열 번호(A=0)를 뽑는다.</summary>
    private static int ColumnIndex(string cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (ch < 'A' || ch > 'Z') break;
            col = col * 26 + (ch - 'A' + 1);
        }
        return col - 1;
    }
}

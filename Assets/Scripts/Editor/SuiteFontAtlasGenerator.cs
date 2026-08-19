using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// SUITE 폰트(Assets/Font/SUITE/*.ttf) → TMP SDF 폰트 에셋(정적 아틀라스) 일괄 생성기.
/// Tools → Fonts → Generate SUITE TMP Atlases
///
/// - 폰트에 "실제 외곽선이 있는" 글자만 아틀라스에 굽는다. (SUITE는 한글 11,172자 중 2,668자만 그려져 있고
///   나머지는 빈 글리프라서, 그걸 그대로 구우면 희귀 음절이 공백으로 나옴 → 빼서 TMP 폴백(맑은고딕)으로 넘김)
/// - 아틀라스에 다 안 들어가면 포인트 크기를 단계적으로 줄여서 재시도.
/// - 결과: Assets/Font/SUITE/SUITE-{Weight} SDF.asset (텍스처/머티리얼 서브에셋 포함)
/// </summary>
public static class SuiteFontAtlasGenerator
{
    const string k_FontDir = "Assets/Font/SUITE";
    const string k_PendingMarker = "Library/SUITE_FontGen.pending"; // 존재하면 다음 도메인 리로드 때 1회 자동 실행

    // ── 아틀라스 설정 ─────────────────────────────────────────────
    const int k_AtlasSize = 2048;                       // 2048² : 그려진 글리프 ~2,900개 기준 충분 (4MB VRAM/폰트)
    const int k_Padding = 4;                            // 아웃라인/그림자 여유 (GradientScale = padding+1)
    static readonly int[] k_PointSizeCandidates = { 34, 32, 30, 28, 26, 24 }; // 큰 것부터, 안 들어가면 다음
    const GlyphRenderMode k_RenderMode = GlyphRenderMode.SDFAA;

    // 스캔할 유니코드 범위 (이 범위 중 폰트에 실제로 그려진 것만 채택)
    static readonly (uint lo, uint hi)[] k_ScanRanges =
    {
        (0x0020, 0x007E), // ASCII
        (0x00A0, 0x00FF), // Latin-1 (°, ×, ·, ± 등)
        (0x2000, 0x26AF), // 일반 구두점, 통화(₩), 화살표, 수학기호, 박스/도형, 기타 기호
        (0x2700, 0x27BF), // Dingbats (✓ 등)
        (0x3000, 0x303F), // CJK 기호/구두점 (、。「」『』 등)
        (0x3131, 0x318E), // 한글 호환 자모 (ㄱ ㄴ ㅏ …)
        (0x3200, 0x33FF), // 괄호 한글/CJK 호환 (㈜ ㎡ 등)
        (0xAC00, 0xD7A3), // 한글 음절 전체
        (0xFF01, 0xFFEE), // 전각 기호 (￦ 등)
    };

    // ── 자동 1회 실행 (마커 파일 방식) ──────────────────────────────
    [InitializeOnLoadMethod]
    static void AutoRunOnce()
    {
        if (!File.Exists(k_PendingMarker)) return;
        EditorApplication.delayCall += TryAutoRun;
    }

    static void TryAutoRun()
    {
        if (!File.Exists(k_PendingMarker)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoRun;
            return;
        }
        File.Delete(k_PendingMarker);
        GenerateAll();
    }

    [MenuItem("Tools/Fonts/Generate SUITE TMP Atlases")]
    public static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(k_FontDir))
        {
            Debug.LogError($"[SUITE] 폴더 없음: {k_FontDir}");
            return;
        }

        string[] ttfPaths = Directory.GetFiles(k_FontDir, "*.ttf")
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        if (ttfPaths.Length == 0)
        {
            Debug.LogError($"[SUITE] {k_FontDir} 에 .ttf 없음");
            return;
        }

        var report = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < ttfPaths.Length; i++)
            {
                string ttf = ttfPaths[i];
                EditorUtility.DisplayProgressBar("SUITE TMP Atlas", $"{Path.GetFileName(ttf)} ({i + 1}/{ttfPaths.Length})", (float)i / ttfPaths.Length);
                string line = GenerateOne(ttf);
                if (line != null) report.Add(line);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SUITE] TMP 폰트 에셋 생성 완료 ({sw.Elapsed.TotalSeconds:F0}s)\n" + string.Join("\n", report));
    }

    /// <returns>리포트 한 줄 (실패 시 null)</returns>
    static string GenerateOne(string ttfPath)
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            AssetDatabase.ImportAsset(ttfPath, ImportAssetOptions.ForceSynchronousImport);
            font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        }
        if (font == null)
        {
            Debug.LogError($"[SUITE] Font 로드 실패: {ttfPath}");
            return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(ttfPath); // SUITE-Bold
        string assetName = baseName + " SDF";
        string assetPath = $"{k_FontDir}/{assetName}.asset";

        // 1) 폰트에 실제로 그려진 글자만 수집
        uint[] unicodes = CollectDrawnUnicodes(font, out int emptyHangul);
        if (unicodes.Length == 0)
        {
            Debug.LogError($"[SUITE] 글리프 수집 실패: {ttfPath}");
            return null;
        }

        // 2) 포인트 크기 내림차순으로 시도
        TMP_FontAsset fontAsset = null;
        int usedPointSize = 0;
        foreach (int ps in k_PointSizeCandidates)
        {
            EditorUtility.DisplayProgressBar("SUITE TMP Atlas", $"{baseName}: {ps}pt 시도 ({unicodes.Length} glyphs)", 0.5f);

            var candidate = TMP_FontAsset.CreateFontAsset(font, ps, k_Padding, k_RenderMode, k_AtlasSize, k_AtlasSize,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (candidate == null)
            {
                Debug.LogError($"[SUITE] CreateFontAsset 실패: {ttfPath} (Include Font Data 확인)");
                return null;
            }

            candidate.TryAddCharacters(unicodes, out uint[] missing, includeFontFeatures: true);

            if (missing == null || missing.Length == 0)
            {
                fontAsset = candidate;
                usedPointSize = ps;
                break;
            }

            Debug.Log($"[SUITE] {baseName}: {ps}pt 에서 {missing.Length}자 넘침 → 축소 재시도");
            DestroyTemp(candidate);
        }

        if (fontAsset == null)
        {
            Debug.LogError($"[SUITE] {baseName}: 최소 크기({k_PointSizeCandidates.Last()}pt)로도 {k_AtlasSize}² 아틀라스에 안 들어감");
            return null;
        }

        // 3) 정적 아틀라스로 전환 + 서브에셋 구성
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
        fontAsset.isMultiAtlasTexturesEnabled = false;
        fontAsset.name = assetName;

        Texture2D atlas = fontAsset.atlasTexture;
        atlas.name = assetName + " Atlas";
        atlas.hideFlags = HideFlags.None;

        // 기존 에셋 있으면 덮어쓰기(레퍼런스/GUID 유지를 위해 같은 경로에 저장)
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null)
        {
            // 기존 서브에셋(아틀라스/머티리얼) 제거 후 본체를 새 데이터로 덮어씀
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
            {
                if (sub == existing) continue;
                AssetDatabase.RemoveObjectFromAsset(sub);
                UnityEngine.Object.DestroyImmediate(sub, true);
            }

            EditorUtility.CopySerialized(fontAsset, existing);
            UnityEngine.Object.DestroyImmediate(fontAsset);
            fontAsset = existing;
            fontAsset.name = assetName;
        }
        else
        {
            AssetDatabase.CreateAsset(fontAsset, assetPath);
        }

        AssetDatabase.AddObjectToAsset(atlas, fontAsset);

        // 머티리얼: 프로젝트 기존 SDF 에셋과 동일하게 "TextMeshPro/Distance Field" 사용
        Material runtimeMat = fontAsset.material; // CreateFontAsset 이 만든 Mobile SDF (버림)
        Shader sdfShader = Shader.Find("TextMeshPro/Distance Field");
        var mat = new Material(sdfShader) { name = assetName + " Material" };
        mat.SetTexture(ShaderUtilities.ID_MainTex, atlas);
        mat.SetFloat(ShaderUtilities.ID_TextureWidth, atlas.width);
        mat.SetFloat(ShaderUtilities.ID_TextureHeight, atlas.height);
        mat.SetFloat(ShaderUtilities.ID_GradientScale, k_Padding + 1);
        mat.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
        mat.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
        fontAsset.material = mat;
        AssetDatabase.AddObjectToAsset(mat, fontAsset);
        if (runtimeMat != null && runtimeMat != mat && !AssetDatabase.Contains(runtimeMat))
            UnityEngine.Object.DestroyImmediate(runtimeMat);

        // 아틀라스 텍스처 읽기 불가로 (정적 폰트 관례, 메모리 절약) — TMP 에디터의 SetAtlasTextureIsReadable 과 동일
        SetTextureReadable(atlas, false);

        // 폰트 에셋 크리에이터 창에서 다시 열 수 있게 생성 설정 기록
        fontAsset.creationSettings = new FontAssetCreationSettings
        {
            sourceFontFileName = font.name,
            sourceFontFileGUID = AssetDatabase.AssetPathToGUID(ttfPath),
            faceIndex = 0,
            pointSizeSamplingMode = 1, // Custom
            pointSize = usedPointSize,
            padding = k_Padding,
            paddingMode = 1,
            packingMode = 4,           // Optimum
            atlasWidth = k_AtlasSize,
            atlasHeight = k_AtlasSize,
            characterSetSelectionMode = 5, // Custom Range
            characterSequence = string.Join(",", k_ScanRanges.Select(r => $"{r.lo}-{r.hi}")),
            referencedFontAssetGUID = string.Empty,
            referencedTextAssetGUID = string.Empty,
            fontStyle = 0,
            fontStyleModifier = 2,
            renderMode = (int)k_RenderMode,
            includeFontFeatures = true,
        };

        fontAsset.ReadFontAssetDefinition();
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssetIfDirty(fontAsset);

        int chars = fontAsset.characterTable.Count;
        int hangul = fontAsset.characterTable.Count(c => c.unicode >= 0xAC00 && c.unicode <= 0xD7A3);
        return $"  {assetName}: {usedPointSize}pt / pad {k_Padding} / {k_AtlasSize}² / {chars} chars (한글 {hangul}자, 빈 글리프 {emptyHangul}자 제외→폴백)";
    }

    /// <summary>스캔 범위 안에서 폰트에 글리프가 있고 실제 외곽선(크기>0)이 있는 문자만 반환. 공백류는 유지.</summary>
    static uint[] CollectDrawnUnicodes(Font font, out int emptyHangulCount)
    {
        emptyHangulCount = 0;
        var list = new List<uint>(4096);

        if (FontEngine.LoadFontFace(font, 90) != FontEngineError.Success)
        {
            Debug.LogError($"[SUITE] FontEngine.LoadFontFace 실패: {font.name}");
            return Array.Empty<uint>();
        }

        foreach (var (lo, hi) in k_ScanRanges)
        {
            for (uint u = lo; u <= hi; u++)
            {
                if (!FontEngine.TryGetGlyphIndex(u, out uint glyphIndex) || glyphIndex == 0)
                    continue;

                bool isSpace = char.IsWhiteSpace((char)u);
                if (isSpace)
                {
                    list.Add(u);
                    continue;
                }

                if (!FontEngine.TryGetGlyphWithUnicodeValue(u, GlyphLoadFlags.LOAD_NO_BITMAP | GlyphLoadFlags.LOAD_NO_HINTING, out Glyph g))
                    continue;

                bool drawn = g.metrics.width > 0f && g.metrics.height > 0f;
                if (!drawn)
                {
                    if (u >= 0xAC00 && u <= 0xD7A3) emptyHangulCount++;
                    continue;
                }
                list.Add(u);
            }
        }

        return list.ToArray();
    }

    static void SetTextureReadable(Texture2D tex, bool readable)
    {
        var so = new SerializedObject(tex);
        var prop = so.FindProperty("m_IsReadable");
        if (prop != null)
        {
            prop.boolValue = readable;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void DestroyTemp(TMP_FontAsset fa)
    {
        if (fa == null) return;
        if (fa.atlasTextures != null)
            foreach (var t in fa.atlasTextures)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
        if (fa.material != null) UnityEngine.Object.DestroyImmediate(fa.material);
        UnityEngine.Object.DestroyImmediate(fa);
    }
}

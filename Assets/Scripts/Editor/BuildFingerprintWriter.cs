using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 빌드 직전에 BuildFingerprint(넷코드 관련 소스 해시)를 Resources/BuildFingerprint.txt로 굽는다.
/// 에디터 플레이 모드에서는 BuildFingerprint.EditorComputeHook으로 같은 계산을 제공한다.
///
/// 해시에 넣는 것: Editor 폴더 밖의 모든 .cs, .asmdef, 빌드 설정에 켜진 .unity 씬, NetworkObject가 들어 있는 .prefab.
/// 넣지 않는 것: 텍스처·모델·사운드 등(넷코드 구조와 무관), _Recovery, Tests.
/// 플랫폼별 차이 제거: CRLF→LF, UTF-8 BOM 제거, 경로 한글 NFC 정규화(macOS는 NFD로 나열됨) — 같은 커밋이면 맥/윈도 지문이 같다.
/// </summary>
public class BuildFingerprintWriter : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;

    private const string kOutputPath = "Assets/Resources/" + BuildFingerprint.ResourceName + ".txt";

    [InitializeOnLoadMethod]
    private static void InstallEditorHook() => BuildFingerprint.EditorComputeHook = Compute;

    public void OnPreprocessBuild(BuildReport report)
    {
        string fp = Compute();
        Directory.CreateDirectory(Path.GetDirectoryName(kOutputPath));
        File.WriteAllText(kOutputPath, fp + "\n");
        AssetDatabase.ImportAsset(kOutputPath, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[BuildFingerprint] 빌드 지문 기록: {fp} → {kOutputPath}");
    }

    [MenuItem("Jobsnail/Netcode/빌드 지문 보기")]
    private static void ShowFingerprint()
    {
        string fp = Compute();
        Debug.Log($"[BuildFingerprint] 현재 소스 지문: {fp}");
        EditorUtility.DisplayDialog("빌드 지문", $"{fp}\n\n같은 커밋·같은 로컬 수정 상태면 어느 플랫폼에서 빌드해도 이 값이 같아야 합니다.", "확인");
    }

    /// <summary>현재 프로젝트 소스의 지문(12자리 hex).</summary>
    public static string Compute()
    {
        var files = CollectFiles();
        using var sha = SHA1.Create();
        foreach (var rel in files)
        {
            var pathBytes = Encoding.UTF8.GetBytes(rel + "\n");
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            var content = NormalizeText(File.ReadAllBytes(rel));
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var sb = new StringBuilder();
        for (int i = 0; i < 6; i++) sb.Append(sha.Hash[i].ToString("x2"));
        return sb.ToString();
    }

    private static List<string> CollectFiles()
    {
        var result = new List<string>();

        foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
            if (!IsExcluded(f)) result.Add(Normalize(f));
        foreach (var f in Directory.GetFiles("Assets", "*.asmdef", SearchOption.AllDirectories))
            if (!IsExcluded(f)) result.Add(Normalize(f));

        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled && File.Exists(scene.path))
                result.Add(Normalize(scene.path));

        string netObjGuid = FindNetworkObjectScriptGuid();
        if (!string.IsNullOrEmpty(netObjGuid))
        {
            string needle = "guid: " + netObjGuid;
            foreach (var f in Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories))
            {
                if (IsExcluded(f)) continue;
                // 프리팹은 크기가 커서(맵 배경 등) 내용에 NetworkObject 참조가 있는 것만 넣는다.
                if (File.ReadAllText(f).Contains(needle))
                    result.Add(Normalize(f));
            }
        }
        else
        {
            Debug.LogWarning("[BuildFingerprint] NetworkObject 스크립트 GUID를 못 찾아 프리팹은 지문에서 뺍니다.");
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static bool IsExcluded(string path)
    {
        string p = path.Replace('\\', '/');
        return p.Contains("/Editor/") || p.Contains("/Tests/") || p.StartsWith("Assets/_Recovery/");
    }

    // 정렬·해시용 상대 경로: 슬래시 통일 + 한글 NFC(macOS 파일 시스템은 NFD로 돌려준다).
    private static string Normalize(string path)
        => path.Replace('\\', '/').Normalize(NormalizationForm.FormC);

    // 텍스트 파일의 플랫폼 차이(CRLF, BOM)를 지운다 — git autocrlf가 켜진 윈도 체크아웃과 맥이 같은 값을 내게.
    private static byte[] NormalizeText(byte[] raw)
    {
        int start = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
        var outBuf = new byte[raw.Length - start];
        int n = 0;
        for (int i = start; i < raw.Length; i++)
            if (raw[i] != (byte)'\r') outBuf[n++] = raw[i];
        if (n == outBuf.Length) return outBuf;
        Array.Resize(ref outBuf, n);
        return outBuf;
    }

    private static string FindNetworkObjectScriptGuid()
    {
        foreach (var guid in AssetDatabase.FindAssets("NetworkObject t:MonoScript"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == typeof(Unity.Netcode.NetworkObject))
                return guid;
        }
        return null;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 셀 셰이딩 전환 — "민짜 3D 폴리곤" 탈출의 본체. 맵 배경 머티리얼(URP Lit ≈125개)을
    /// 유니티 공식 툰 셰이더(UTS3, com.unity.toonshader)로 일괄 전환한다:
    ///  · 밝은 면/1차 그늘/2차 그늘이 계단식으로 딱딱 떨어지는 카툰 라이팅(2단 셰이드 + 페더)
    ///  · 그늘은 파란기 도는 카툰 관례 색(베이스색 × 한색 틴트) — 칙칙한 회색 그늘 방지
    ///  · UTS 자체 아웃라인은 0(스크린스페이스 SeoulToonEdge가 담당 — 이중 라인 방지)
    ///
    /// 전환 제외: 투명(_Surface 1)·알파컷(_AlphaClip 1)·에미션 켜진 것(_EMISSION — 가로등/네온류)·
    /// 하늘(Sky_*)·툰엣지. 원본 Lit 프로퍼티는 머티리얼에 그대로 남으므로 '되돌리기'는 셰이더만 복구하면 끝.
    ///
    /// 실행: Tools ▸ Map ▸ ★ 셀 셰이딩 전환(맵 배경 → 툰)   /   되돌리기: 같은 메뉴 아래
    /// 요구: Packages/manifest.json의 com.unity.toonshader (0.14.1-preview) 설치 완료 상태.
    /// </summary>
    public static class CelShadeTool
    {
        private const string kMatDir = "Assets/Map/Materials";
        private const string kBackupPath = "Library/CelShadeConverted.txt";   // 전환 목록(되돌리기용)
        private const string kLitGuid = "933532a4fcc9baf4fa0491de14d08ed7";   // URP Lit

        // 카툰 그늘 틴트 — 그늘은 살짝 파랗게(회색 그늘은 칙칙해 보임)
        private static readonly Color kShade1Tint = new Color(0.60f, 0.64f, 0.86f);   // 09/03 3차 "단차 더 세게" — 그늘 더 진하고 파랗게
        private static readonly Color kShade2Tint = new Color(0.40f, 0.44f, 0.68f);

        [MenuItem("Tools/Map/★ 셀 셰이딩 전환(맵 배경 → 툰)")]
        public static void Apply()
        {
            var toon = FindToonShader();
            if (toon == null)
            {
                Debug.LogError("[셀셰이딩] 유니티 툰 셰이더를 못 찾음 — Package Manager에서 com.unity.toonshader 설치를 확인하세요" +
                               " (Packages/manifest.json엔 이미 등록해 둠 — 에디터 재시작이면 충분할 것).");
                return;
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");

            var converted = new List<string>();
            int skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { kMatDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                bool already = mat.shader == toon;   // 재실행 = 수치 재조정(멱등)
                if (!already && mat.shader != lit) continue;
                if (!already && !IsCelTarget(mat)) { skipped++; continue; }

                var baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                var baseMap = mat.HasProperty(already ? "_MainTex" : "_BaseMap") ? mat.GetTexture(already ? "_MainTex" : "_BaseMap") : null;
                var scale = mat.HasProperty(already ? "_MainTex" : "_BaseMap") ? mat.GetTextureScale(already ? "_MainTex" : "_BaseMap") : Vector2.one;
                var offset = mat.HasProperty(already ? "_MainTex" : "_BaseMap") ? mat.GetTextureOffset(already ? "_MainTex" : "_BaseMap") : Vector2.zero;

                mat.shader = toon;
                // UTS 기본 입력 — _MainTex × _BaseColor가 밝은 면
                if (mat.HasProperty("_MainTex"))
                {
                    mat.SetTexture("_MainTex", baseMap);
                    mat.SetTextureScale("_MainTex", scale);
                    mat.SetTextureOffset("_MainTex", offset);
                }
                Set(mat, "_BaseColor", baseColor);
                Set(mat, "_Color", baseColor);
                // 그늘 2단 — 셰이드 맵은 베이스 재활용, 색은 베이스 × 한색 틴트
                SetF(mat, "_Use_BaseAs1st", 1f);
                SetF(mat, "_Use_1stAs2nd", 1f);
                Set(mat, "_1st_ShadeColor", baseColor * kShade1Tint);
                Set(mat, "_2nd_ShadeColor", baseColor * kShade2Tint);
                // 확인(09/03): URP DoubleShadeWithFeather HLSL이 읽는 실제 이름은 _BaseColor_Step 계열.
                // 박스 지오메트리는 면 단위 명암이라 단차를 세게 줘야 티가 난다 — step↑, feather 하드하게.
                SetF(mat, "_BaseColor_Step", 0.6f);        // 09/03 3차: 그늘 영역 넓게 + 경계 칼같이(만화 셀 느낌)
                SetF(mat, "_BaseShade_Feather", 0.005f);
                SetF(mat, "_ShadeColor_Step", 0.4f);
                SetF(mat, "_1st2nd_Shades_Feather", 0.005f);
                // 림 라이트는 금지(09/03 스크린샷 검증): 클로즈업 시 평평한 벽면 전체가 비스듬해져
                // 면이 통째로 허옇게 떴다("이질적 흰색"). 박스 지오메트리에 림은 역효과.
                SetF(mat, "_RimLight", 0f);
                // 라이트 색 반영(달빛 밤 맵 대응) + 앰비언트 약간
                SetF(mat, "_Is_LightColor_Base", 1f);
                SetF(mat, "_Is_LightColor_1st_Shade", 1f);
                SetF(mat, "_Is_LightColor_2nd_Shade", 1f);
                SetF(mat, "_GI_Intensity", 0.3f);
                SetF(mat, "_Unlit_Intensity", 1f);
                // 아웃라인은 최종 반려(09/03) — 폭 70에서 스케일 큰 오브젝트(덤불 GLB 등)의 헐 껍질이
                // 수십 배로 부풀어 "흐리멍텅한 덩어리"가 됐고 사용자가 "걍 하지 말자"로 확정. 다시 켜자고 제안 금지.
                SetF(mat, "_Outline_Width", 0f);

                EditorUtility.SetDirty(mat);
                converted.Add(path);
            }

            File.WriteAllLines(kBackupPath, converted);
            AssetDatabase.SaveAssets();
            Debug.Log($"[셀셰이딩] 완료 ✔ {converted.Count}개 전환, {skipped}개 제외(투명·알파컷·에미션·하늘). " +
                      $"되돌리기: Tools ▸ Map ▸ 셀 셰이딩 되돌리기 (목록: {kBackupPath})");
        }

        [MenuItem("Tools/Map/셀 셰이딩 되돌리기")]
        public static void Revert()
        {
            if (!File.Exists(kBackupPath)) { Debug.LogWarning("[셀셰이딩] 전환 기록이 없음 — 되돌릴 것 없음"); return; }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int n = 0;
            foreach (var path in File.ReadAllLines(kBackupPath).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == lit) continue;
                mat.shader = lit;   // 원본 Lit 프로퍼티는 머티리얼에 그대로 저장돼 있어 셰이더만 되돌리면 복구됨
                EditorUtility.SetDirty(mat);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[셀셰이딩] 되돌리기 ✔ {n}개 URP Lit 복귀");
        }

        /// <summary>전환 대상 판정 — 투명/알파컷/에미션/하늘/시스템 머티리얼은 Lit 유지.</summary>
        private static bool IsCelTarget(Material mat)
        {
            string name = mat.name;
            if (name.StartsWith("Sky_") || name == "Mat_ToonEdge") return false;
            // 원경(빌딩 격자·광통교 파사드·지평선 바닥)은 툰 금지 — UTS가 URP 안개를 안 먹어서
            // 세상은 하얀 안개로 녹는데 빌딩만 까만 실루엣으로 남는 사고(09/03 "미친듯이 발광").
            if (name.StartsWith("Mat_Bldg_") || name.StartsWith("Mat_GtgFacade") || name.StartsWith("Mat_Horizon")) return false;
            if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) return false;      // 투명(물·유리 등)
            if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f) return false;  // 컷아웃(나무 빌보드·실루엣 카드)
            if (mat.IsKeywordEnabled("_EMISSION")) return false;                                    // 가로등·네온(밤 발광 유지)
            return true;
        }

        private static Shader FindToonShader()
        {
            foreach (var n in new[] { "Universal Render Pipeline/Toon", "Toon", "Unity Toon Shader/Toon" })
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            // 이름이 버전마다 달라 패키지에서 직접 탐색(폴백)
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/com.unity.toonshader" }))
            {
                var s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.name.EndsWith("/Toon")) return s;
            }
            return null;
        }

        private static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        private static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    }
}

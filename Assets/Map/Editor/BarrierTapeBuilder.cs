using System.IO;
using UnityEditor;
using UnityEngine;

namespace MapTools
{
    /// <summary>
    /// 공사장 차단 라인 자동 생성 — 선택한 차단봉(또는 빈 오브젝트)들을 순서대로 노랑-검정 테이프로 잇는다.
    /// 사용:
    ///  1) 배경 프리팹(또는 씬)에 차단봉들을 원하는 자리에 배치
    ///  2) 이을 순서대로 차단봉들을 다중 선택(Ctrl+클릭)
    ///  3) Tools ▸ Map ▸ Connect Barrier Tape 실행 → 선택 순서대로 테이프 쿼드가 생성됨
    /// 테이프는 유니티 Quad 프리미티브(영구 메시)라 프리팹 저장에 안전. 텍스처·머티리얼은 자동 생성/재사용.
    /// </summary>
    public static class BarrierTapeBuilder
    {
        private const string kTexPath = "Assets/Map/01_GwangTongGyo/Props/Tex_BarrierTape.png";
        private const string kMatPath = "Assets/Map/01_GwangTongGyo/Props/Mat_BarrierTape.mat";
        private const float kTapeHeight = 0.72f;   // 봉 기준 테이프 높이(중심)
        private const float kTapeWidth = 0.16f;    // 테이프 폭
        private const float kStripePerMeter = 1.5f; // 1m당 무늬 반복 수

        [MenuItem("Tools/Map/Connect Barrier Tape")]
        public static void Connect()
        {
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length < 2)
            {
                Debug.LogError("[BarrierTape] 이을 차단봉을 '순서대로' 2개 이상 다중 선택하고 실행하세요.");
                return;
            }

            var mat = EnsureTapeMaterial();
            var parent = sel[0].transform.parent;
            var root = new GameObject("~BarrierTape");
            root.transform.SetParent(parent, false);

            int made = 0;
            for (int i = 0; i < sel.Length - 1; i++)
            {
                Vector3 a = sel[i].transform.position + Vector3.up * kTapeHeight;
                Vector3 b = sel[i + 1].transform.position + Vector3.up * kTapeHeight;
                float len = Vector3.Distance(a, b);
                if (len < 0.05f) continue;

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>());   // 장식 — 충돌 불필요
                quad.name = $"Tape_{i}";
                quad.transform.SetParent(root.transform, false);
                quad.transform.position = (a + b) * 0.5f;
                quad.transform.rotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up);
                quad.transform.localScale = new Vector3(len, kTapeWidth, 1f);

                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // 길이에 비례해 무늬 반복(머티리얼 인스턴스 없이 per-renderer 프로퍼티)
                var mpb = new MaterialPropertyBlock();
                mpb.SetVector("_BaseMap_ST", new Vector4(len * kStripePerMeter, 1f, 0f, 0f));
                mr.SetPropertyBlock(mpb);
                made++;
            }

            Undo.RegisterCreatedObjectUndo(root, "Barrier Tape");
            Selection.activeGameObject = root;
            Debug.Log($"[BarrierTape] 테이프 {made}구간 생성 ✔ (선택 순서대로 연결됨). 프리팹 모드였다면 저장 잊지 마세요.");
        }

        private const string kWarningTexPath = "Assets/Map/01_GwangTongGyo/Props/Tex_BarrierTape_Warning.png";

        // 테이프 머티리얼: 바르코 생성 WARNING 텍스처가 있으면 그걸, 없으면 절차 생성 스트라이프.
        private static Material EnsureTapeMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kMatPath);
            if (mat != null) return mat;

            var warning = AssetDatabase.LoadAssetAtPath<Texture2D>(kWarningTexPath);
            if (warning != null)
            {
                var wImp = (TextureImporter)AssetImporter.GetAtPath(kWarningTexPath);
                if (wImp != null && wImp.wrapMode != TextureWrapMode.Repeat)
                { wImp.wrapMode = TextureWrapMode.Repeat; wImp.SaveAndReimport(); }
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetTexture("_BaseMap", warning);
                mat.SetFloat("_Smoothness", 0.1f);
                mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                AssetDatabase.CreateAsset(mat, kMatPath);
                return mat;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(kTexPath) == null)
            {
                const int W = 256, H = 64;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                var yellow = new Color32(255, 205, 40, 255);
                var black = new Color32(35, 35, 35, 255);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        // 대각 45° 스트라이프
                        bool stripe = (((x + y) / 32) & 1) == 0;
                        tex.SetPixel(x, y, stripe ? yellow : black);
                    }
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(kTexPath));
                File.WriteAllBytes(kTexPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(kTexPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(kTexPath);
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.SaveAndReimport();
            }

            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(kTexPath));
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);   // 양면(뒤에서도 보임)
            AssetDatabase.CreateAsset(mat, kMatPath);
            return mat;
        }
    }
}

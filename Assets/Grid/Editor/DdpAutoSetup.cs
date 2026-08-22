using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// DDP 맵 자동 셋업 — 에디터 로드(컴파일) 직후 필요한 것만 1회 실행한다.
    /// ① 맵 카드(Map_Ddp.asset)가 없으면 DdpMapTool.Generate()
    /// ② Models 폴더 구성이 지난번과 달라졌으면(새 GLB·교체된 GLB·삭제)
    ///    DdpModelApplyTool.Apply() + 맵 재생성
    /// ③ 생성 툴 자체를 고쳤으면(kSetupVersion을 올리면) 역시 재적용 + 재생성
    /// 할 일이 없으면 아무것도 안 한다(수동 실행: Tools ▸ Map ▸ ★ DDP …).
    ///
    /// 이 프로젝트는 보통 에디터를 띄워둔 채로 작업해서 배치모드(-executeMethod)를 못 쓴다 —
    /// 그래서 롯데월드와 같은 [InitializeOnLoad] + delayCall 1회성 실행기 패턴을 쓴다.
    ///
    /// 왜 "_Fit이 없는 GLB가 있나?"로 판정하지 않는가:
    ///   · 리메시 등으로 GLB를 '교체'하면 _Fit은 이미 있어서 영영 재적용이 안 된다.
    ///   · 반대로 변환에 실패하는 GLB가 하나라도 있으면(렌더러 없음 등) 도메인 리로드마다 무한 재시도한다.
    ///   → 그래서 폴더 구성을 지문(이름+크기)으로 찍어 '달라졌을 때만' 한 번 돈다.
    /// </summary>
    [InitializeOnLoad]
    public static class DdpAutoSetup
    {
        private const string kMapDefPath = "Assets/Map/Maps/Map_Ddp.asset";
        private const string kDir = "Assets/Prefabs/Map/4_Ddp";
        private const string kModelDir = kDir + "/Models";

        /// <summary>생성 툴(DdpMapTool/DdpModelApplyTool)을 고쳐서 재생성이 필요할 때 올린다.</summary>
        private const int kSetupVersion = 2;

        private const string kStampKey = "SeoulZikimi.Ddp.SetupStamp";

        static DdpAutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                bool mapMissing = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath) == null;
                string stamp = BuildStamp();
                bool changed = EditorPrefs.GetString(kStampKey, string.Empty) != stamp;

                if (!mapMissing && !changed) return;

                if (mapMissing)
                    Debug.Log("[DDP] 맵 카드가 없어 자동 생성 실행 (Tools ▸ Map ▸ ★ DDP 맵 생성)");
                else
                    Debug.Log("[DDP] 모델 폴더/생성 툴이 바뀌어 재적용 실행 (Tools ▸ Map ▸ ★ DDP VARCO 모델 적용)");

                if (HasAnyModel())
                    DdpModelApplyTool.Apply();      // GLB → _Fit.prefab + def 연결
                DdpMapTool.Generate();              // 배경 소품 반영(멱등 — 재실행 안전)

                // 성공/실패와 무관하게 지문을 갱신한다 — 실패한 GLB 때문에 매번 다시 돌지 않게.
                EditorPrefs.SetString(kStampKey, stamp);
            };
        }

        private static bool HasAnyModel()
        {
            if (!Directory.Exists(kModelDir)) return false;
            foreach (var f in Directory.GetFiles(kModelDir))
                if (IsModel(f)) return true;
            return false;
        }

        private static bool IsModel(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".glb" || ext == ".fbx" || ext == ".obj";
        }

        // Models 폴더의 지문: 버전 + (파일명, 크기) 목록. GLB를 교체하면 크기가 달라져 지문이 바뀐다.
        // ⚠ FindAssets("t:Model")은 glTFast(ScriptedImporter)로 임포트된 .glb를 못 찾는다 — 파일 기준으로 훑는다.
        private static string BuildStamp()
        {
            var sb = new StringBuilder();
            sb.Append('v').Append(kSetupVersion).Append(';');

            if (Directory.Exists(kModelDir))
            {
                var files = Directory.GetFiles(kModelDir);
                System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    if (!IsModel(f)) continue;
                    sb.Append(Path.GetFileName(f)).Append(':').Append(new FileInfo(f).Length).Append(';');
                }
            }
            return sb.ToString();
        }
    }
}

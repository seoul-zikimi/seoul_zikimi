using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// DDP 맵 자동 셋업 — 에디터 로드(컴파일) 직후:
    /// ① 맵 카드(Map_Ddp.asset)가 없으면 DdpMapTool.Generate() 실행.
    /// ② Models 폴더에 아직 _Fit으로 변환 안 된 GLB가 있으면 DdpModelApplyTool.Apply()
    ///    + 맵 재생성(배경 소품 반영)까지 자동 실행.
    /// 할 일이 없으면 아무것도 안 함(수동 실행: Tools ▸ Map ▸ ★ DDP …).
    ///
    /// 이 프로젝트는 보통 에디터를 띄워둔 채로 작업해서 배치모드(-executeMethod)를 못 쓴다 —
    /// 그래서 롯데월드와 같은 [InitializeOnLoad] + delayCall 1회성 실행기 패턴을 쓴다.
    /// </summary>
    [InitializeOnLoad]
    public static class DdpAutoSetup
    {
        private const string kMapDefPath = "Assets/Map/Maps/Map_Ddp.asset";
        private const string kDir = "Assets/Prefabs/Map/4_Ddp";
        private const string kModelDir = kDir + "/Models";

        static DdpAutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                bool mapMissing = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath) == null;
                if (mapMissing)
                {
                    Debug.Log("[DDP] 맵 카드가 없어 자동 생성 실행 (Tools ▸ Map ▸ ★ DDP 맵 생성)");
                    DdpMapTool.Generate();
                }

                if (HasUnappliedModels())
                {
                    Debug.Log("[DDP] 적용 안 된 VARCO 모델 발견 — 자동 적용 실행 (Tools ▸ Map ▸ ★ DDP VARCO 모델 적용)");
                    DdpModelApplyTool.Apply();
                    DdpMapTool.Generate();   // 배경 소품 _Fit 반영(멱등 — 재실행 안전)
                }
            };
        }

        // Models 폴더의 GLB 중 대응 _Fit.prefab이 없는 것이 하나라도 있는가.
        // ⚠ FindAssets("t:Model")은 glTFast(ScriptedImporter)로 임포트된 .glb를 못 찾는다 — 파일 기준으로 훑는다.
        private static bool HasUnappliedModels()
        {
            if (!System.IO.Directory.Exists(kModelDir)) return false;
            foreach (var file in System.IO.Directory.GetFiles(kModelDir))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".glb" && ext != ".fbx" && ext != ".obj") continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (AssetDatabase.LoadAssetAtPath<GameObject>($"{kDir}/{name}_Fit.prefab") == null) return true;
            }
            return false;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// 롯데월드 맵 자동 셋업 — 에디터 로드(컴파일) 직후:
    /// ① 맵 카드(Map_LotteWorld.asset)가 없으면 LotteWorldMapTool.Generate() 실행.
    /// ② Models 폴더에 아직 _Fit으로 변환 안 된 GLB가 있으면 LotteModelApplyTool.Apply()
    ///    + 맵 재생성(배경 소품 반영)까지 자동 실행.
    /// 할 일이 없으면 아무것도 안 함(수동 실행: Tools ▸ Map ▸ ★ 롯데월드 …).
    /// </summary>
    [InitializeOnLoad]
    public static class LotteWorldAutoSetup
    {
        private const string kMapDefPath = "Assets/Map/Maps/Map_LotteWorld.asset";
        private const string kDir = "Assets/Prefabs/Map/3_LotteWorld";
        private const string kModelDir = kDir + "/Models";

        // _Fit 래핑 '규격'이 바뀌면 이미 구워둔 _Fit은 옛 규격 그대로다(파일은 멀쩡히 있으니
        // HasUnappliedModels가 못 잡는다). 규격을 고칠 때 이 번호를 올리면 각 에디터에서 딱 한 번 다시 굽는다.
        //   2 = 중앙첨탑 밑동 0.5칸 연장 + 깃발 깃대 축을 칸 중심에 정렬 (QA: 롯데월드 에셋 크기/위치조정)
        private const int kFitSpecVersion = 2;
        private const string kFitSpecKey = "LotteWorld.FitSpecVersion";

        static LotteWorldAutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                bool mapMissing = AssetDatabase.LoadAssetAtPath<MapDef>(kMapDefPath) == null;
                if (mapMissing)
                {
                    Debug.Log("[롯데월드] 맵 카드가 없어 자동 생성 실행 (Tools ▸ Map ▸ ★ 롯데월드 맵 생성)");
                    LotteWorldMapTool.Generate();
                }

                bool specStale = EditorPrefs.GetInt(kFitSpecKey, 0) < kFitSpecVersion;
                if (HasUnappliedModels() || specStale)
                {
                    Debug.Log(specStale
                        ? "[롯데월드] _Fit 래핑 규격이 바뀜 — 파츠 다시 굽기 (Tools ▸ Map ▸ ★ 롯데월드 VARCO 모델 적용)"
                        : "[롯데월드] 적용 안 된 VARCO 모델 발견 — 자동 적용 실행 (Tools ▸ Map ▸ ★ 롯데월드 VARCO 모델 적용)");
                    LotteModelApplyTool.Apply();
                    LotteWorldMapTool.Generate();   // 배경 소품 _Fit 반영(멱등 — 재실행 안전)
                    EditorPrefs.SetInt(kFitSpecKey, kFitSpecVersion);
                }

                // 프리팹 규약(피벗 min-corner + footprint 크기) 어긋난 def가 있으면 공식 칸맞춤 실행
                // — MaterialPrefabContractTests가 요구하는 바로 그 수정 경로(어긋난 것만 골라 고침).
                if (AnyDefOutOfContract())
                {
                    Debug.Log("[롯데월드] 규약 어긋난 재료 프리팹 발견 — 칸 맞춤 실행 (Tools ▸ Grid ▸ 재료 프리팹 칸 맞춤(전체))");
                    MaterialPrefabFitTool.FitAll();
                }
            };
        }

        private static bool AnyDefOutOfContract()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MaterialDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && def.Prefab != null && MaterialPrefabFitTool.Check(def) != null) return true;
            }
            return false;
        }

        // Models 폴더의 GLB 중 대응 _Fit.prefab(퍼레이드카는 Resources 프리팹)이 없는 것이 하나라도 있는가.
        // ⚠ FindAssets("t:Model")은 glTFast(ScriptedImporter)로 임포트된 .glb를 못 찾는다 — 파일 기준으로 훑는다.
        // ⚠ 산출 경로는 LotteModelApplyTool과 반드시 일치해야 한다 — 여기가 어긋난 이름(퍼레이드카2~4는
        //   Resources/ParadeCarN.prefab, 모노빔은 폐기)을 '영원히 미적용'으로 오판해, 에디터 로드마다
        //   맵 전체 재생성이 돌았고 그때마다 프리팹·썸네일 fileID가 바뀌어 브랜치 머지 충돌이 반복됐다.
        private static bool HasUnappliedModels()
        {
            if (!System.IO.Directory.Exists(kModelDir)) return false;
            foreach (var file in System.IO.Directory.GetFiles(kModelDir))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".glb" && ext != ".fbx" && ext != ".obj") continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (name == "롯데_모노빔") continue;   // 폐기 — 모노레일은 기둥 마커 방식이라 _Fit을 만들지 않는다
                string fitPath = name.StartsWith("롯데_퍼레이드카")
                    ? $"Assets/Resources/LotteWorld/ParadeCar{name.Substring("롯데_퍼레이드카".Length)}.prefab"
                    : $"{kDir}/{name}_Fit.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(fitPath) == null) return true;
            }
            return false;
        }
    }
}

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

        /// <summary>재적용이 필요할 때 올린다 — 생성 툴을 고쳤거나, GLB를 통째로 갈아끼웠을 때.
        /// (Models 폴더만 바꾸면 에셋 임포트는 일어나도 도메인 리로드가 안 걸려 이 실행기가 안 돈다.
        ///  이 상수를 건드리면 스크립트가 재컴파일되면서 리로드가 걸리고, 그때 지문 비교로 1회 재적용된다.)
        /// 3: VARCO 모델 11종을 30k tri 리메시본으로 교체(500k → 30k, 386MB → 211MB).
        /// 4: 정답을 '낮고 넓은' DDP 실루엣으로 재설계(파츠 footprint 전면 변경 → _Fit 전부 다시 만들어야 한다),
        ///    장미화단·유구터 프롭 제거 + 장미 개별 식재, 이간수문 비율/위치 교정,
        ///    Resources/Ddp 런타임 프리팹 추가(DigStake · Artifact0~2).
        /// 5: LED 장미 발판 기믹 제거(마커·부착 없앰) + 나선램프 폭 3.4 → 5.6m,
        ///    그리고 DDP 정답을 '통짜 모델 격자 절단'(DdpSliceTool) 방식으로 전환 —
        ///    Models/DDP_본관.glb 가 있으면 그걸 잘라 만든 고유 곡면 조각이 정답이 된다.
        /// 6: 절단 조각이 배치 시 제멋대로 90° 돌아가던 문제 수정(GridFootprint 자동 보정을 자유 형상엔 끔) +
        ///    통짜 크기 확대(span 11×4×7 → 13×5×10, 그리드 13 → 14).
        /// 7: (되돌림) 조각 병합·양면 머티리얼을 넣었다가 텍스처가 날아가 전부 새하얘져서 6 상태로 복구.
        ///    삼각형 보존 여부를 알려 주는 로그만 남겼다.
        /// 8: 위 되돌림 반영.
        /// 9: '완성체 교체' 도입 — 자르기 전 통짜를 DDP_본관_완성.prefab으로 따로 굽고,
        ///    완공 계획도(정답 UI)와 '다 지었을 때'를 그 원본으로 보여준다(조각 이음매를 안 보이게).
        /// 10: 배송존과 겹치던 성곽 유구(FortressWall) 그레이박스 5개 제거(재료가 블록 사이로 떨어져 가려짐) +
        ///     절단 조각에 '밀폐 스커트'(단면 커튼·바닥 뚜껑, 은색 별도 서브메시) 추가 —
        ///     주문 배달로 날아오는 조각이 속 빈 껍데기(기본 상자)처럼 보이던 문제. 조각 재절단 필요.
        /// 11: 원경(DDP_원경) 배경 모델 제거(완성본이 공중에 떠 보여 정답과 헷갈림) +
        ///     동쪽 곡면 나선램프 → 남쪽 직선 램프(다리) 교체 — 원호 초입이 물길을 바닥 높이로 관통하고
        ///     끝자락이 데크에 파묻혀 잔디 위로 흰 패널처럼 삐져나오던 문제.
        ///     이제 배송존 옆에서 출발해 물길 위 ~1.5m를 다리로 건너 데크에 오른다(물을 전혀 안 밟음).
        /// 12: 직선 램프를 다시 '나선(원호) 램프'로 — 단, 기하를 교정해 중심 (2,-4)·반지름 14·270°→360°.
        ///     입구가 물길 남쪽 배송존 옆(z≤-15.2)이라 시작점이 물길에 안 닿고, 물길은 상공 ~1.5m 다리로
        ///     건너며, 원호가 z>-4로 안 넘어가 끝자락이 데크에 파묻히지도 않는다. 난간 안팎 양쪽.
        ///     램프 발자국을 피해 발굴터 B(4→1)·C(14→20)와 스폰(2,-16 → -1,-16.5)을 이동.
        /// 13: 나선램프를 연속 리본에서 '낱장 패널 나선 계단'으로 — 원호·시작·도착은 12와 동일,
        ///     수평 패널 16장이 0.25m씩 층지며 틈(~0.27m)을 두고 떠 있는 형태(점프 없이 걸어 오름).
        /// 14: 완성체(DDP_본관_완성) 회전 교정 -90° → +90° — 절단 조각(정점 +90° 회전)과 반대라
        ///     완공 계획도·정답 고스트가 실제 정답과 180° 뒤집혀 보이던 문제. 완성체 재생성 필요.</summary>
        private const int kSetupVersion = 14;

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

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
        ///     완공 계획도·정답 고스트가 실제 정답과 180° 뒤집혀 보이던 문제. 완성체 재생성 필요.
        /// 15: ★ 야경 컨셉 전환 — 낮 그레이박스가 밋밋해서 실물 DDP의 본체인 '밤'으로.
        ///     MapNightAmbience(맵 로드 때만 밤 하늘·안개·앰비언트·달빛 오버라이드 — 씬·다른 맵은 낮 그대로),
        ///     가로등 11주 + 불빛 웅덩이(가산 쿼드), 옹벽 LED 미디어 스트립 2줄, 나선램프 난간 조명,
        ///     NightBuildGlow(지은 블록·LED 장미 자체 발광), Sky_SeoulNight 머티리얼, 썸네일도 밤으로.
        /// 16: 야경 1차 피드백 반영 — ① 가로등 밑 검은 사각형 수정(URP 머티리얼 검증이 _Blend=Alpha로
        ///     Src/DstBlend를 리셋 → _Blend=Additive 명시 + 웅덩이 텍스처에 알파 폴오프),
        ///     ② 장미 48 → ~100송이(간격 0.85, 밭 동쪽 x10까지) + 수로 볼라드 7 + 전구 줄 4스팬 + 나무 3그루,
        ///     ③ 북서쪽 '꼬리 동' 배경 매스(실물의 길게 흐르는 꼬리 — 정답·그리드와 안 겹치는 z≥14.4),
        ///     ④ 본관 투광 라이트 2 + 블록 발광 0.9 → 1.6(블룸 문턱을 넘겨 또렷하게),
        ///     LED 스트립 색 순환(EmissionCycler), VARCO 소품 슬롯 추가(DDP_가로등·DDP_나무).
        /// 17: 본관을 '파츠 3종 조립'으로 전환(DdpAssembleTool) — 한 방 생성 통짜는 전체 실루엣을
        ///     자꾸 틀리게 뽑아서(지붕 언덕이 웅덩이·배치 어긋남), 항공사진 3분할(윗동·중간동·꼬리동)대로
        ///     따로 뽑아 실물 배치로 이어 붙인 조립본을 절단 원본으로 쓴다. 파츠가 덜 모이면 통짜 폴백.
        ///     ⚠ 파츠 방향이 뒤집혀 보이면 DdpAssembleTool.kParts의 yaw ±180 후 재생성.
        /// 18: 유구 발굴터 기믹 제거(08/31 기획 결정 — 물길 하나만 남긴다) —
        ///     GameLoopManager가 ExcavationNetwork를 더 이상 부착하지 않고, Spot_DigSite* 마커와
        ///     DigStake·Artifact0~2 Resources 프리팹 생성을 뺐다. 코드 파일은 남긴다(장미 발판과 동일 처리).
        /// 19: 파츠 3종을 사용자 제작 GLB로 교체(윗동=workflow-result·중간동=Modern Organic·꼬리동=Modern Green)
        ///     + '맵이 엄청 작아진' 문제 수정: 실물 비율(높이/길이 ≈ 0.1)이 절단 후 팬케이크가 되던 것을
        ///     조립 시 XZ 비율 유지·Y만 강제(머리 ~6칸)로 부풀리고, 절단 스팬 13×5×10 → 14×6×12로 확대.
        /// 20: 파츠가 옆으로 자빠진 채 조립되던 사고 수정 — GLB '위' 축 복불복을 흡수(가장 얇은 축을 Y로
        ///     자동 눕힘)하고, 회전·비균등 스케일이 한 트랜스폼에 섞여 뒤틀리던 것을 래퍼 분리로 해결
        ///     (partRoot: yaw·위치 / scaleWrap: 스케일 / inst: 방향 교정). 파츠 간격도 겹치게 좁힘.
        /// 21: ★ 본관 모델 롤백(08/31 기획 결정) — 파츠 조립·재생성 통짜 모두 원본을 안 닮아 폐기.
        ///     '지어야 하는 것'은 폴리싱 전의 검증된 통짜(DDP_본관.glb 원본)로 복귀:
        ///     파츠 GLB(윗동·중간동·꼬리동)·조립 프리팹 제거(→ 절단이 통짜 폴백), 스팬 13×5×10 복귀.
        ///     야경 컨셉과 발굴 기믹 제거는 그대로 유지. 조립 툴(DdpAssembleTool)은 휴면 —
        ///     파츠 GLB 3종을 다시 넣으면 재가동된다.
        /// 22: 외관 폴리싱 2차 —
        ///     ① 초록 언덕 버그: 비주얼정리 스커트가 광장 남쪽까지 부풀어 LED 장미밭을 덮던 것 →
        ///        DDP 프로필 Skirt=false + 데크 밑을 은색 패널(DeckSkirt_W/E/N)로 마감
        ///     ② 투명 경계벽(~BoundaryWalls) — 광장·데크 둘레, 맵 이탈 방지
        ///     ③ 물길 연출 강화 — 반투명·에미션 물(회색 민짜 박스 탈출), 차오름 0.6→0.25초,
        ///        수문 물보라 파티클(예고 잔뿌림 → 개방 순간 90발 버스트 → 방류 중 연속)
        ///     ④ 야경 강화 — 미디어폴 6기(색 순환), 서치라이트 빔 2기(SlowSpin), 곡면 벤치 3,
        ///        가로등 3.0·장미 2.4·사이클러 3.4로 증폭. VARCO 슬롯 추가: DDP_미디어폴·DDP_벤치.
        /// 23: 야경 스크린샷 피드백(08/31→09/01) —
        ///     ① 가로등 위 '묻은 정육면체' 수정: 발광 헤드 큐브 → 모델 바운즈 꼭대기의 가산 헤일로(쿼드 3장)
        ///     ② 꼬리동 그레이박스 제거(통짜 롤백 후 중복 + 초록 박스 줄로 보임)
        ///     ③ 밤 앰비언트·달빛 톤 업(칙칙함 완화), 데크 나무 2그루 추가
        ///     ④ 바닥 텍스처 VARCO 생성(잔디지붕·광장바닥·은색패널 — ApplyTexture 슬롯에 꽂힘).
        /// 24: 야경 3차 피드백(09/01) —
        ///     ① 가로등 '위·아래만 빛' 교정: 헤드→웅덩이를 잇는 가산 빛 원뿔 메시(LampCone.asset) 추가
        ///     ② 텍스처 미적용 원인 수정: 자동 지문이 GLB만 보고 PNG를 무시 → 텍스처도 지문에 포함
        ///     ③ 물길: 뿅 등장 → 상류에서 하류로 전선(front)이 퍼지고, 끝나면 상류부터 빠지는 연출
        ///     ④ 수로 침수 구간의 미디어폴 제거·벤치 이사, ⑤ LED 장미 반짝임(NightBuildGlow.Twinkle).
        /// 25: 야경 4차(09/01) — ① 장미 안 반짝이던 원인 수정: glTFast 장미가 '에미션 없는 셰이더 변형'으로
        ///     구워져 emissiveFactor가 안 먹혔다 → URP Lit 에미션 인스턴스로 강제 교체(ForceLitEmissive)
        ///     ② 예고 중 물판 표시 제거 — "차기도 전에 하늘색 꽉 참"으로 보였다(예고는 토스트+잔뿌림만)
        ///     ③ 데크 윤곽 조명(둘레 4변 발광 라인) + 장미밭 반딧불 파티클(~RoseFireflies).
        /// 26: 맵 전체 밝기 업(09/01 "좀만 더 밝게") — 밤 앰비언트 3색·달빛(0.55→0.70)·안개색 상향,
        ///     데크 상공 라이트 0.75→1.05. 밤 분위기는 유지, 플레이 가시성만 올림.
        /// 27: '허공에 뜬 맵' 종결(09/01) — ① 재생성 후 ~Horizon 자동 재깔기(수동 재실행 까먹음 방지)
        ///     ② 데크 난간(서·동·북 — 실물 잔디지붕 가드레일, 비주얼 전용)
        ///     ③ 서치라이트 수리: 빔 쿼드가 90° 잘못 돌아 하늘에 수평으로 누워 안 보이던 버그 수정 +
        ///        '꺼먼 정육면체' 받침을 원기둥 받침+기울인 몸통+발광 렌즈 프로젝터로 교체.</summary>
        private const int kSetupVersion = 27;

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
                // 맵 재생성이 프리팹을 통째로 다시 쓰면서 ~Horizon(1km 바닥·원경 도시)이 매번 날아간다 —
                // 수동 재실행을 계속 까먹어 "맵이 허공에 떠 있다"(09/01)가 반복돼 여기서 자동으로 다시 깐다(멱등).
                MapVisualPolishTool.ApplyHorizonOnly();

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
            // 텍스처(텍스처_*.png)도 지문에 포함 — GLB만 보던 시절엔 텍스처를 나중에 넣으면
            // 재적용이 안 걸려 "바닥 텍스처 적용 안 됨"(09/01)이 됐다.
            return ext == ".glb" || ext == ".fbx" || ext == ".obj" || ext == ".png" || ext == ".jpg";
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

using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ 광통교(청계천) 그래픽 보강 — "벽 너머가 뚝 끊기고 텍스처가 안 입혀져 휑하다" 원클릭 해결.
    ///
    /// 조사 결과(09/01) 광통교 배경의 실태:
    ///  ① 덤불(Bushes 아래 GLB 94주)은 데이터로는 멀쩡히 살아 있는데 게임에서 안 보인다
    ///     — GLB 안에 초록 수풀 텍스처까지 정상인 걸 확인. 임포트된 머티리얼 쪽이 깨진 것이라
    ///     GLB 내장 텍스처로 URP Lit 머티리얼을 다시 조립해 강제로 입힌다(+ 진단 리포트 로그).
    ///  ② 둔치·옹벽 큐브들이 저장소에 존재한 적 없는 머티리얼 guid를 참조(→ 슬롯이 Missing).
    ///     콘크리트 석재 머티리얼을 새로 만들어 빈 슬롯 전부에 채운다.
    ///  ③ 원경 도시(BackgroundCity)의 Building_XX가 Autotiles3D '샘플 계단 FBX'의 체커
    ///     머티리얼을 쓰고 있어 까만 격자 타워처럼 보인다 → 비주얼 정리 툴의 창문 파사드
    ///     텍스처(Assets/Map/Horizon/Bldg_Facade.png)로 파스텔 틴트 6종을 입힌다.
    ///  ④ 비주얼 정리 툴의 원경 빌딩이 이 맵엔 0개 배치(기존 배경이 자리를 다 차지해 전부 스킵)
    ///     — 벽 너머가 맨 아스팔트 평면뿐이라, 물길·옹벽을 안개 라인(~320m)까지 연장하고
    ///     둔치 위에 가로수 줄(남산_나무·롯데_나무 재활용)과 보도 스트립을 깔아 청계천답게 잇는다.
    ///
    /// 생성물은 전부 "~GwangTongExtras" 그룹 아래(멱등 — 재실행 시 지우고 다시 만든다).
    /// ~Horizon(비주얼 정리 툴 소유)은 건드리지 않는다.
    ///
    /// 실행: Tools ▸ Map ▸ ★ 광통교 그래픽 보강(텍스처·연장·가로수·덤불)
    /// 되돌리기: git checkout Assets/Resources/MapPrefabs/MapBg_GwangTongGyo.prefab
    /// 진단 리포트: 콘솔 + Library/GwangTongGyoPolishReport.txt
    /// </summary>
    public static class GwangTongGyoPolishTool
    {
        private const string kPrefabPath = "Assets/Resources/MapPrefabs/MapBg_GwangTongGyo.prefab";
        private const string kMatDir     = "Assets/Map/Materials";
        private const string kGroupName  = "~GwangTongExtras";
        private const string kReportPath = "Library/GwangTongGyoPolishReport.txt";

        // 물길 단면(프리팹 실측): 물길 x -12.6~13, 둔치 윗면 y 5.4(비주얼 정리 툴 바닥 평면 5.42)
        private const float kChanXMin = -12.6f, kChanXMax = 13f;
        private const float kBankTopY = 5.42f;
        private const float kZPlayEdge = 33f;    // 여기까지가 기획 배경(옹벽·계단 디테일) — 그 너머가 '뚝 끊기는' 지점
        private const float kZFar = 330f;        // 안개 끝(320m) 너머까지 연장해 지평선에 녹인다

        [MenuItem("Tools/Map/★ 광통교 그래픽 보강(텍스처·연장·가로수·덤불)")]
        public static void Apply()
        {
            var report = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents(kPrefabPath);
            try
            {
                FixBushes(root, report);
                int missingFixed = FillMissingMaterials(root, report);
                int reskinned = ReskinBackgroundCity(root, report);
                BuildExtras(root, report);
                BuildBankFurniture(root, report);

                PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
                MapVisualPolishTool.ApplyHorizonFor(kPrefabPath);   // 물길 카브 폭이 바뀌면(흰 틈새 픽스) 바닥 평면도 같이 다시 깐다
                foreach (var leftover in new[] { "Mat_GtgStreamWater", "Mat_GtgLeaf", "Mat_GtgTrunk",
                                                 "Mat_GtgArchStone", "Mat_GtgTunnelMouth", "Mat_GtgWalkway" })   // 지난 버전들(워터·가로수·수문·보도) 잔재 정리
                    AssetDatabase.DeleteAsset($"{kMatDir}/{leftover}.mat");
                AssetDatabase.SaveAssets();
                Debug.Log($"[광통교보강] 완료 ✔ Missing 슬롯 {missingFixed}개 채움 · 원경 빌딩 {reskinned}개 파사드 교체. " +
                          $"상세 리포트: {kReportPath}\n{report}");
                File.WriteAllText(kReportPath, report.ToString());
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ───────────────────── ① 덤불 진단 + 복구 ─────────────────────
        // Bushes 아래 94주는 Bush1/bush2/bush3.glb 인스턴스. 프리팹 데이터·GLB 텍스처 모두 정상인데
        // 머티리얼만 갈아 끼워도(1차 시도) 여전히 안 보였다 — 낡은 인스턴스의 내부 참조(메시 등)까지
        // 의심해, 각 인스턴스의 로컬 TRS는 유지한 채 '지금의 GLB 프리팹'으로 전부 새로 인스턴스화한다.
        private static void FixBushes(GameObject root, StringBuilder report)
        {
            var bushGroups = FindAll(root, "Bushes");
            if (bushGroups.Count == 0) { report.AppendLine("[덤불] 'Bushes' 그룹을 못 찾음 — 건너뜀"); return; }

            var matCache = new System.Collections.Generic.Dictionary<string, Material>();
            var prefabCache = new System.Collections.Generic.Dictionary<string, GameObject>();
            int replaced = 0, nullMesh = 0, noSrc = 0, extraPlanted = 0;

            foreach (var group in bushGroups)
            {
                // 진단: 기존 인스턴스들의 메시 생존 여부(사라짐의 원인 기록용)
                foreach (var mf in group.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh == null) nullMesh++;

                // 기존 자식들의 (소스 GLB, 로컬 TRS) 수집 — GLB 인스턴스가 아닌 자식은 건드리지 않는다
                var specs = new System.Collections.Generic.List<(string src, Vector3 p, Quaternion r, Vector3 s)>();
                var doomed = new System.Collections.Generic.List<GameObject>();
                foreach (Transform child in group.transform)
                {
                    if (child.name.StartsWith("~BushExtra")) { doomed.Add(child.gameObject); continue; }   // 지난 실행의 밀도업 사본 — 갈아엎는다(스펙 수집 제외)
                    string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                    if (string.IsNullOrEmpty(src) || !src.EndsWith(".glb")) { noSrc++; continue; }
                    specs.Add((src, child.localPosition, child.localRotation, child.localScale));
                    doomed.Add(child.gameObject);
                }
                if (specs.Count == 0) continue;

                // 수집한 인스턴스만 지우고 같은 자리에서 새로 심는다
                foreach (var go in doomed) Object.DestroyImmediate(go);

                foreach (var (src, p, rot, s) in specs)
                {
                    if (!prefabCache.TryGetValue(src, out var prefab))
                    {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(src);
                        prefabCache[src] = prefab;
                    }
                    if (prefab == null) { report.AppendLine($"[덤불] GLB 로드 실패: {src}"); continue; }
                    if (!matCache.TryGetValue(src, out var mat))
                    {
                        mat = BuildBushMaterial(src);
                        matCache[src] = mat;
                    }

                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                    inst.transform.localPosition = p;
                    inst.transform.localRotation = rot;
                    inst.transform.localScale = s;
                    // 원본 회전은 그대로 둔다(QA 09/03 3차 결론): 이 GLB는 눕혀 제작돼 원 배치 회전이
                    // 이미 '세운 상태'다. 세우기 보정을 얹으면 오히려 통째로 눕는다 — 문제는 사본 쪽이었다.
                    foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                    }
                    replaced++;
                }

                // "다닥다닥" 밀도 업(09/03 지시): 덤불마다 사본 2주를 주변에 흩뿌린다.
                // 이름 ~BushExtra — 재실행 시 위에서 지우고 다시 심으므로 불어나지 않는다(멱등).
                var rng = new System.Random(77);
                foreach (var (src, p, rot, s) in specs)
                {
                    if (!prefabCache.TryGetValue(src, out var prefab) || prefab == null) continue;
                    matCache.TryGetValue(src, out var mat);
                    for (int k = 0; k < 4; k++)   // 09/03 2차 "더 빽빽" — 주당 사본 4주(사실상 생울타리)
                    {
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                        inst.name = $"~BushExtra_{extraPlanted}";
                        // ⚠ 오프셋은 반드시 '월드' 기준(09/03 사고): Bushes 그룹 로컬축이 회전·스케일돼 있어
                        // 로컬 z ±1.1가 월드에선 하늘 방향 10~20m로 튀었다(공중 부유 덤불의 정체 — 진단 리포트로 확인).
                        // 물길·화단 줄은 월드 z 방향이므로 월드 z로만 끼워 넣고 y는 원본과 동일하게 고정한다.
                        float dz = (k - 1.5f) * 0.75f + ((float)rng.NextDouble() - 0.5f) * 0.3f;   // -1.1 ~ +1.1 균등 4칸
                        var worldP = group.transform.TransformPoint(p);
                        worldP += new Vector3(((float)rng.NextDouble() - 0.5f) * 0.3f, 0f, dz);
                        inst.transform.position = worldP;
                        // 랜덤 yaw는 '월드 Y' 기준으로(QA 09/03 진범): 이 GLB는 눕혀 제작돼 로컬 Y로 돌리면
                        // 시각적으로 기울어진다 — 벽에서 옆으로 솟던 사본들의 정체. 원본 월드 회전을 얻은 뒤
                        // 월드 up 축으로만 돌려 '선 상태'를 유지한 채 방향만 섞는다.
                        // 360° 풀랜덤이면 길쭉한 덤불이 벽과 직각으로 틀어져 보도로 삐져나온다 —
                        // 생울타리 결(벽과 평행)을 유지하게 ±15°만 지터.
                        inst.transform.localRotation = rot;
                        inst.transform.rotation =
                            Quaternion.AngleAxis(((float)rng.NextDouble() - 0.5f) * 30f, Vector3.up) * inst.transform.rotation;
                        inst.transform.localScale = s * Mathf.Lerp(0.82f, 1.0f, (float)rng.NextDouble());
                        foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                        foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                            r.shadowCastingMode = ShadowCastingMode.Off;
                            r.receiveShadows = false;
                        }
                        extraPlanted++;
                    }
                }
            }

            report.AppendLine($"[덤불] 재인스턴스 {replaced}주 + 밀도업 사본 {extraPlanted}주 · (진단) 빈 메시 {nullMesh}개 · 소스 불명 {noSrc}개");
        }

        /// <summary>GLB의 서브에셋 중 컬러맵(diffuse/baseColor 우선, 노말맵 제외)을 찾아
        /// URP Lit 머티리얼(Mat_GtgBush_*)로 굽는다. 덤불 GLB엔 normal·diffuse 두 장이 같은 2048²라
        /// '가장 큰 것' 기준으론 노말맵이 걸릴 수 있다.</summary>
        private static Material BuildBushMaterial(string glbPath)
        {
            // GLB에서 추출해 둔 독립 PNG(Tex_*_Diffuse.png)가 있으면 그걸 최우선으로 —
            // 서브에셋 텍스처 참조는 glTFast 버전이 바뀌면 fileID가 어긋나 조용히 죽을 수 있다.
            string dir = System.IO.Path.GetDirectoryName(glbPath)?.Replace('\\', '/');
            string loosePath = $"{dir}/Tex_{System.IO.Path.GetFileNameWithoutExtension(glbPath)}_Diffuse.png";
            Texture2D best = AssetDatabase.LoadAssetAtPath<Texture2D>(loosePath);
            if (best == null)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(glbPath))
            {
                if (!(o is Texture2D t)) continue;
                string n = t.name.ToLowerInvariant();
                if (n.Contains("normal") || n.Contains("_nrm") || n.Contains("bump")) continue;
                bool bestPreferred = best != null && IsColorName(best.name);
                if (best == null || (IsColorName(t.name) && !bestPreferred) ||
                    (IsColorName(t.name) == bestPreferred && t.width * t.height > best.width * best.height))
                    best = t;
            }
            if (best == null) { Debug.LogWarning($"[광통교보강] {glbPath}에서 텍스처를 못 찾음"); return null; }

            string name = "Mat_GtgBush_" + Path.GetFileNameWithoutExtension(glbPath);
            var mat = EnsureLit(name, kBushTint);
            if (mat == null) return null;
            mat.SetTexture("_BaseMap", best);
            // 기존 머티리얼도 색 갱신(EnsureLit는 생성 때만 색을 먹인다) — 흰색 틴트면 원본 텍스처가
            // 너무 밝고 물빠져 보인다(QA 09/03). 곱하기 틴트로 톤을 누르고 초록 채도를 끌어올린다.
            mat.SetColor("_BaseColor", kBushTint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 덤불 틴트(곱셈색) — 1차 (0.55,0.78,0.42)는 "너무 퓨어 초록"(QA 09/03 2차).
        // G를 눌러 채도를 빼고 전체를 낮춰 어두운 올리브톤으로: 어둡게 = 전체↓, 물빠지게 = R·G·B 간격↓.
        private static readonly Color kBushTint = new Color(0.48f, 0.56f, 0.42f);

        private static bool IsColorName(string texName)
        {
            string n = texName.ToLowerInvariant();
            return n.Contains("diffuse") || n.Contains("basecolor") || n.Contains("albedo") || n.Contains("_col");
        }

        // ───────────────────── ② Missing 머티리얼 슬롯 채우기 ─────────────────────
        // 둔치/옹벽 큐브 3개가 guid 31321ba1…(저장소에 존재한 적 없음)를 참조 → 로드하면 슬롯이 null.
        // null 슬롯 전부를 석재 콘크리트 머티리얼로 채운다(~Horizon 밑은 정리 툴 소유라 제외).
        private static int FillMissingMaterials(GameObject root, StringBuilder report)
        {
            var stone = EnsureLit("Mat_GtgEmbankment", new Color(0.76f, 0.74f, 0.69f));
            int fixedSlots = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsUnder(r.transform, "~Horizon") || IsUnder(r.transform, kGroupName)) continue;
                var mats = r.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == null) { mats[i] = stone; touched = true; fixedSlots++; }
                if (touched)
                {
                    r.sharedMaterials = mats;
                    report.AppendLine($"[Missing] {ScenePath(r.transform)} 슬롯 채움");
                }
            }
            return fixedSlots;
        }

        // ───────────────────── ③ 원경 도시(BackgroundCity) 파사드 ─────────────────────
        private static readonly Color[] kFacadeTints =
        {
            new Color(0.93f, 0.90f, 0.84f), new Color(0.86f, 0.89f, 0.94f), new Color(0.90f, 0.86f, 0.82f),
            new Color(0.84f, 0.88f, 0.86f), new Color(0.95f, 0.93f, 0.90f), new Color(0.80f, 0.84f, 0.90f),
        };

        private static int ReskinBackgroundCity(GameObject root, StringBuilder report)
        {
            // 비주얼 정리 툴이 구워 둔 창문 파사드 타일(없으면 틴트만이라도)
            var facadeTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Map/Horizon/Bldg_Facade.png");
            var mats = new Material[kFacadeTints.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = EnsureLit($"Mat_GtgFacade{i}", kFacadeTints[i]);
                if (mats[i] != null && facadeTex != null)
                {
                    mats[i].SetTexture("_BaseMap", facadeTex);
                    EditorUtility.SetDirty(mats[i]);
                }
            }

            int count = 0;
            foreach (var t in root.transform.Cast<Transform>().Where(t => t.name.StartsWith("BackgroundCity")))
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                var pick = mats[Mathf.Abs(r.transform.position.GetHashCode() + r.name.GetHashCode()) % mats.Length];
                if (pick == null) continue;
                r.sharedMaterials = Enumerable.Repeat(pick, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                count++;
            }
            report.AppendLine($"[원경도시] 파사드 교체 {count}개");
            return count;
        }

        // ───────────────────── ④ 물길 끝 다리 반복 배치 + 보도 스트립 ─────────────────────
        // 1차(워터 1km 연장+가로수 107주)·2차(프리미티브 아치 수문) 모두 QA 반려("어색함").
        // 3차 지시: "수문 모델 뽑거나 이미 있는 걸 재활용해서 양옆으로 자연스럽게 반복 배치" —
        // 맵 남쪽 끝(z≈-34.9)에 이미 서 있는 광통교 아치교(bridge.glb 인스턴스)를 그대로 복제해
        // ① 북쪽 끝에 미러 배치(어색했던 수문 자리), ② 남북 안개 속(±약 105m)에 한 채씩 더 —
        // 청계천에 다리가 줄지어 놓인 실제 풍경 어법. 원본과 완전히 같은 룩(오버라이드 포함 클론).
        private static void BuildExtras(GameObject root, StringBuilder report)
        {
            var old = root.transform.Find(kGroupName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var grp = new GameObject(kGroupName);
            grp.transform.SetParent(root.transform, false);

            const float kChanCz = -0.343f;   // 물길 큐브 중심 z — 남쪽 다리를 이 축으로 미러하면 북쪽 끝

            // 원본 다리: 이름 'bridge' + 소스가 bridge.glb인 인스턴스(남쪽 끝)
            GameObject srcBridge = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "bridge" || IsUnder(t, kGroupName)) continue;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (!string.IsNullOrEmpty(src) && src.EndsWith("bridge.glb")) { srcBridge = t.gameObject; break; }
            }

            if (srcBridge != null)
            {
                var bp = srcBridge.transform.position;
                var br = srcBridge.transform.rotation;
                var mirrorRot = Quaternion.Euler(0f, 180f, 0f) * br;   // 북쪽은 180° 돌려 앞뒤 맞춤

                // (이름, 위치, 회전, 원경 여부) — 근경 북쪽 1 + 원경 남북 각 1
                var placements = new (string name, Vector3 pos, Quaternion rot, bool far)[]
                {
                    ("BridgeN",    new Vector3(bp.x, bp.y, 2f * kChanCz - bp.z), mirrorRot, false),
                    ("BridgeFarS", new Vector3(bp.x, bp.y, bp.z - 70f),          br,        true),
                    ("BridgeFarN", new Vector3(bp.x, bp.y, 2f * kChanCz - bp.z + 70f), mirrorRot, true),
                };
                foreach (var (name, pos, rot, far) in placements)
                {
                    var clone = Object.Instantiate(srcBridge, grp.transform);   // 오버라이드 포함, 원본과 동일 룩
                    clone.name = name;
                    clone.transform.position = pos;
                    clone.transform.rotation = rot;
                    foreach (var c in clone.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                    {
                        if (far)   // 원경 다리는 그림자·레이캐스트 끔(근경 북쪽은 남쪽 원본과 같은 룩 유지)
                        {
                            r.shadowCastingMode = ShadowCastingMode.Off;
                            r.receiveShadows = false;
                        }
                        r.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                    }
                }
                report.AppendLine($"[연장] 남쪽 다리(bridge.glb) 재활용 — 북쪽 미러 1 + 원경 남북 각 1 (원본 pos={bp}, 프리미티브 수문은 제거)");
            }
            else
            {
                report.AppendLine("[연장] ⚠ 원본 다리(bridge.glb 인스턴스)를 못 찾아 다리 복제 생략");
            }

            // ── 물길·계단 연장: 회랑 콘텐츠(Plane 물바닥·Walls 석축·바닥돌·계단·징검다리·덤불)를
            //    렌더러 단위로 떠서 남북 ±70m 평행이동 복제 1장씩. 이음새(z≈±33~37)는 다리(±34.9)
            //    뒤에 숨고, 겹치는 구간은 사본을 y -0.03 내려 z-파이트를 피한다.
            //    실측 바운즈: Plane z -37.5~36.8 · Walls -30.5~60 · GameObject -38.7~27.2 —
            //    '완전 포함' 필터(v7)는 이들을 다 놓쳤으므로 '코어 교차 + 방향별 침범 가드'로 고른다.
            // 조인트 = 회랑 콘텐츠의 실제 끝단(Plane 바닥 메시 실측: 남 -37.5 / 북 36.8).
            // 거울 복제는 '어떤 곡선이든 끝단에서 자기 자신과 정확히 이어진다' — 물 S커브·연석·계단이
            // 전부 맞물린다(±70m 평행이동은 곡선이 어긋나 "물길이 깨져 보인다" QA 반려).
            var planeT = root.transform.Find("Plane");
            var planeR = planeT != null ? planeT.GetComponentInChildren<MeshRenderer>() : null;
            float jointN = planeR != null ? planeR.bounds.max.z : kZPlayEdge + 4f;
            float jointS = planeR != null ? planeR.bounds.min.z : -kZPlayEdge - 4f;

            int copied = 0;
            var tileN = new GameObject("ChannelTileN"); tileN.transform.SetParent(grp.transform, false);
            var tileS = new GameObject("ChannelTileS"); tileS.transform.SetParent(grp.transform, false);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (IsUnder(r.transform, "~Horizon") || IsUnder(r.transform, kGroupName) ||
                    IsUnder(r.transform, "Answer Layer") || IsUnder(r.transform, "BackgroundCity")) continue;
                bool banned = false;   // 게임플레이/마커류 + 다리(따로 배치함) 제외
                for (var p = r.transform; p != null && !banned; p = p.parent)
                    banned = p.name.StartsWith("Spot_") || p.name.Contains("Hammer") || p.name.Contains("Delivery") ||
                             p.name.Contains("Paint") || p.name.StartsWith("Anchor") || p.name == "bridge";
                if (banned) continue;
                var b = r.bounds;
                if (b.min.x < -18f || b.max.x > 19f) continue;      // 회랑 폭 밖(둔치 큐브 등) 제외
                if (b.max.y > 8f) continue;                          // 둔치 위 구조물 제외
                if (b.size.z > 120f) continue;                       // 1km 스트레치 큐브 제외
                if (b.max.z < -kZPlayEdge || b.min.z > kZPlayEdge) continue;   // 코어 회랑과 무관한 조각 제외
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                foreach (int dir in new[] { -1, 1 })
                {
                    // 미러 후 실제로 플레이 영역(z ±33) 안까지 접혀 들어오는 조각만 스킵(북쪽 z60 벽 등).
                    // '조인트를 살짝 걸친' 조각(남쪽 -38.7 계단 등)은 접혀도 영역 밖 — 스킵하면
                    // 연장부에 구멍이 생긴다(v12에서 남쪽이 뚝 끊겨 보였던 원인).
                    if (dir > 0 && 2f * jointN - b.max.z < kZPlayEdge + 1f) continue;
                    if (dir < 0 && 2f * jointS - b.min.z > -kZPlayEdge - 1f) continue;
                    var copy = new GameObject(r.gameObject.name);
                    copy.transform.SetParent((dir < 0 ? tileS : tileN).transform, false);
                    copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    copy.transform.localScale = r.transform.lossyScale;   // 컨테이너 플립 전 = 월드 TRS 그대로
                    copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var mr = copy.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = r.sharedMaterials;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    copy.layer = LayerMask.NameToLayer("Ignore Raycast");
                    copied++;
                }
            }

            // ── 물(WindingWater): ProBuilder라 메시가 에셋이 아니라 로드 때 컴포넌트가 재구축 —
            //    렌더러 복제로는 못 뜨고(툴 시점 sharedMesh=null), 그룹을 컴포넌트째 복제해야 한다.
            //    타일 컨테이너 안에 원본 TRS 그대로 넣으면 아래 플립이 물길도 같이 미러한다.
            var waterGrp = root.transform.Find("Water");
            if (waterGrp != null)
            {
                foreach (var tile in new[] { tileS, tileN })
                {
                    var wclone = Object.Instantiate(waterGrp.gameObject, tile.transform);
                    wclone.name = "Water";
                    wclone.transform.position = waterGrp.position;
                    wclone.transform.rotation = waterGrp.rotation;
                    wclone.transform.localScale = waterGrp.lossyScale;
                    foreach (var c in wclone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    foreach (var r in wclone.GetComponentsInChildren<Renderer>(true))
                    {
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        r.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                    }
                }
            }

            // 컨테이너째 미러: z' = 2*joint - z, y는 살짝 내려 원본과 겹치는 부분의 z-파이트 방지
            tileN.transform.localScale = new Vector3(1f, 1f, -1f);
            tileN.transform.position = new Vector3(0f, -0.03f, 2f * jointN);
            tileS.transform.localScale = new Vector3(1f, 1f, -1f);
            tileS.transform.position = new Vector3(0f, -0.03f, 2f * jointS);
            report.AppendLine($"[연장] 회랑 거울-타일 남북 각 1장 (조인트 z {jointS:F1}/{jointN:F1}) — 사본 {copied}개 + Water(ProBuilder) {(waterGrp != null ? "포함" : "없음⚠")}");

            // (진단) 루트 그룹별 렌더러 바운즈 — 타일 소스 검증용
            foreach (Transform t in root.transform)
            {
                if (t.name == kGroupName || t.name == "~Horizon") continue;
                Bounds gb = default; bool ghas = false;
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                { if (!ghas) { gb = r.bounds; ghas = true; } else gb.Encapsulate(r.bounds); }
                if (ghas) report.AppendLine($"[바운즈] {t.name}: x {gb.min.x:F1}~{gb.max.x:F1}, y {gb.min.y:F1}~{gb.max.y:F1}, z {gb.min.z:F1}~{gb.max.z:F1}");
            }

            // (진단) 회랑 개별 렌더러 상세 — 어떤 조각이 물/바닥/석축인지 식별용(머티리얼 이름 포함)
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (IsUnder(r.transform, "~Horizon") || IsUnder(r.transform, kGroupName) ||
                    IsUnder(r.transform, "BackgroundCity") || IsUnder(r.transform, "Bushes")) continue;
                var b = r.bounds;
                if (b.min.x < -20f || b.max.x > 21f || b.max.y > 9f) continue;
                if (b.max.z < -90f || b.min.z > 90f) continue;
                string mats = string.Join(",", r.sharedMaterials.Select(m => m == null ? "(null)" : m.name));
                string pathStr = r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name;
                report.AppendLine($"[조각] {pathStr}: y {b.min.y:F1}~{b.max.y:F1}, z {b.min.z:F1}~{b.max.z:F1}, x {b.min.x:F1}~{b.max.x:F1} · {mats}");
            }

            // (보도 스트립은 v10에서 제거 — 회랑 복제로 덤불·석축이 이어지자 흰 띠가 오히려 이질적으로 떠 보임)
        }

        // ───────────────────── 진단 스냅샷 ─────────────────────
        /// <summary>프리팹을 프리뷰 씬에 세워 남북 물길 끝을 카메라로 찍는다 → Library/GtgShot_*.png.
        /// 게임을 안 띄우고도 연장 결과를 눈으로 검증하기 위한 QA용.</summary>
        [MenuItem("Tools/Map/광통교 스냅샷(진단)")]
        public static void Snapshot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath);
            if (prefab == null) { Debug.LogWarning("[광통교보강] 프리팹 없음 — 스냅샷 생략"); return; }

            var pru = new PreviewRenderUtility();
            try
            {
                pru.camera.farClipPlane = 2000f;
                pru.camera.nearClipPlane = 0.3f;
                pru.camera.fieldOfView = 55f;
                pru.camera.clearFlags = CameraClearFlags.Color;
                pru.camera.backgroundColor = new Color(0.75f, 0.85f, 0.95f);
                pru.ambientColor = new Color(0.55f, 0.55f, 0.58f);
                if (pru.lights.Length > 0)
                {
                    pru.lights[0].intensity = 1.2f;
                    pru.lights[0].transform.rotation = Quaternion.Euler(50f, -32f, 0f);
                }
                pru.InstantiatePrefabInScene(prefab);

                var shots = new (string name, Vector3 eye, Vector3 look)[]
                {
                    ("N_inside",  new Vector3(0f, 16f, -8f), new Vector3(0f, 0f, 60f)),   // 플레이 영역에서 북쪽 다리 너머
                    ("N_beyond",  new Vector3(0f, 35f, 75f), new Vector3(0f, 0f, 40f)),   // 북쪽 연장부 부감
                    ("S_inside",  new Vector3(0f, 16f, 8f),  new Vector3(0f, 0f, -60f)),
                    ("S_beyond",  new Vector3(0f, 35f, -75f), new Vector3(0f, 0f, -40f)),
                };
                foreach (var (name, eye, look) in shots)
                {
                    pru.camera.transform.position = eye;
                    pru.camera.transform.rotation = Quaternion.LookRotation(look - eye);
                    pru.BeginStaticPreview(new Rect(0f, 0f, 1280f, 720f));
                    pru.camera.Render();
                    var tex = pru.EndStaticPreview();
                    File.WriteAllBytes($"Library/GtgShot_{name}.png", tex.EncodeToPNG());
                }
                Debug.Log("[광통교보강] 진단 스냅샷 4장 → Library/GtgShot_*.png");
            }
            finally { pru.Cleanup(); }
        }

        private static Material EnsureLit(string name, Color color)
        {
            Directory.CreateDirectory(kMatDir);
            string path = $"{kMatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogWarning("[광통교보강] URP Lit 셰이더를 못 찾음"); return null; }
            mat = new Material(sh);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void Prep(GameObject go, Transform parent, string name, Material mat)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("Ignore Raycast");   // 시야가림 페이드·클릭 레이 제외
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (mat != null) mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static bool IsUnder(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        private static string ScenePath(Transform t)
        {
            var sb = t.name;
            for (var p = t.parent; p != null; p = p.parent) sb = p.name + "/" + sb;
            return sb;
        }

        /// <summary>이름이 일치하는(또는 name==null이면 전부) GameObject를 루트 아래에서 모두 찾는다.</summary>
        private static System.Collections.Generic.List<GameObject> FindAll(GameObject root, string name)
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (name == null || t.name == name) list.Add(t.gameObject);
            return list;
        }

        // ───────────────────── ⑦ 둔치 가구(난간·가로등) — 09/03 "위쪽이 텅 비고 휑하다" ─────────────────────
        // 청계천 실물처럼: 물길 양안 상단 보도에 짙은 초록 난간(기둥+가로대 2단) + 가로등(지그재그).
        // 전부 ~GtgFurniture 아래(멱등 — 재실행 시 갈아엎음), 장식이라 콜라이더 없음.
        private const string kFurnitureGroup = "~GtgFurniture";

        private static void BuildBankFurniture(GameObject root, StringBuilder report)
        {
            var old = root.transform.Find(kFurnitureGroup);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var grp = new GameObject(kFurnitureGroup).transform;
            grp.SetParent(root.transform, false);

            // 난간 색: 초록 → 금속 회색(QA 09/03). EnsureLit는 생성 때만 색을 먹여 기존 에셋도 갱신한다.
            var kRailGray = new Color(0.47f, 0.48f, 0.51f);
            var railMat = EnsureLit("Mat_GtgRail", kRailGray);
            if (railMat != null) { railMat.SetColor("_BaseColor", kRailGray); EditorUtility.SetDirty(railMat); }
            var poleMat = EnsureLit("Mat_GtgLampPole", new Color(0.22f, 0.23f, 0.25f));
            var headMat = EnsureLit("Mat_GtgLampHead", new Color(1f, 0.92f, 0.72f));
            if (headMat != null)
            {   // 은은한 발광 — _EMISSION이 켜져 있으면 셀 셰이딩 전환에서도 자동 제외된다
                headMat.EnableKeyword("_EMISSION");
                headMat.SetColor("_EmissionColor", new Color(1f, 0.83f, 0.5f) * 1.6f);
                EditorUtility.SetDirty(headMat);
            }

            int posts = 0, lamps = 0;
            foreach (int side in new[] { -1, 1 })
            {
                // 난간은 덤불 화단 뒤(물길 반대쪽) 보도 위 — 화단과 겹치지 않게 바깥으로 0.6m
                float railX = side < 0 ? kChanXMin - 0.6f : kChanXMax + 0.6f;
                // 기둥 간격 2.5 → 1.0m + 가로대 3단(QA 09/03 "더 촘촘하게") — 실물 안전난간 밀도
                for (float z = -kZPlayEdge; z <= kZPlayEdge + 0.01f; z += 1.0f)
                { Box(grp, "RailPost", new Vector3(railX, kBankTopY + 0.5f, z), new Vector3(0.07f, 1.0f, 0.07f), railMat); posts++; }
                Box(grp, "RailTop", new Vector3(railX, kBankTopY + 0.98f, 0f), new Vector3(0.07f, 0.07f, kZPlayEdge * 2f), railMat);
                Box(grp, "RailMid", new Vector3(railX, kBankTopY + 0.66f, 0f), new Vector3(0.05f, 0.05f, kZPlayEdge * 2f), railMat);
                Box(grp, "RailLow", new Vector3(railX, kBankTopY + 0.34f, 0f), new Vector3(0.05f, 0.05f, kZPlayEdge * 2f), railMat);

                // 가로등 — 난간보다 한 발 더 보도 안쪽, 양안 지그재그(8m 간격).
                // DDP의 VARCO 가로등 모델을 재활용(09/03 지시 "DDP에 썼던 에셋 재활용") — 없으면 박스 폴백.
                var lampPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/4_Ddp/DDP_가로등_Fit.prefab");
                float lampX = side < 0 ? kChanXMin - 1.8f : kChanXMax + 1.8f;
                for (float z = -kZPlayEdge + 2f + (side < 0 ? 0f : 4f); z <= kZPlayEdge - 1f; z += 8f)
                {
                    if (lampPrefab != null)
                    {
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, grp);
                        inst.name = "Lamp";
                        inst.transform.localPosition = new Vector3(lampX, kBankTopY, z);
                        inst.transform.localRotation = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);   // 헤드가 물길 쪽을 보게
                        foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                        foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                        { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                    }
                    else
                    {
                        Box(grp, "LampPole", new Vector3(lampX, kBankTopY + 1.6f, z), new Vector3(0.12f, 3.2f, 0.12f), poleMat);
                        Box(grp, "LampHead", new Vector3(lampX, kBankTopY + 3.35f, z), new Vector3(0.38f, 0.3f, 0.38f), headMat);
                    }
                    lamps++;
                }
            }
            // 벽 위 흰 빈공간 가림막(QA 09/03): 석축 꼭대기와 위 도로 사이 틈으로 배경이 새하얗게 뚫려 보인다 —
            // 석축과 같은 톤(살짝 어둡게 — 뒷켜로 읽히게)의 벽을 난간·가로등 '뒤'에 한 겹 덧댄다.
            var kFillerGray = new Color(0.58f, 0.59f, 0.61f);   // 베이지 → 회색(QA 09/03 4차)
            var fillerMat = EnsureLit("Mat_GtgGapFiller", kFillerGray);
            if (fillerMat != null) { fillerMat.SetColor("_BaseColor", kFillerGray); EditorUtility.SetDirty(fillerMat); }
            // 위치(QA 09/03 3차 확정): 석축 '꼭대기 아래', 벽 바로 뒤 — 벽 너머 허공이 윗모서리 틈으로
            // 새하얗게 비쳐 보이는 걸 막는 덧벽이다. 꼭대기 위로는 한 치도 안 올라간다(도로 쪽 침범 금지).
            foreach (int side in new[] { -1, 1 })
            {
                float x = side < 0 ? kChanXMin - 0.5f : kChanXMax + 0.5f;
                // 윗모서리를 석축 꼭대기보다 살짝 아래(-0.1)로 — +0.4로 올렸더니 띠가 삐죽 보였다(4차)
                Box(grp, "GapFillerWall", new Vector3(x, kBankTopY - 2.0f, 0f),
                    new Vector3(0.3f, 3.8f, (kZPlayEdge + 8f) * 2f), fillerMat);
            }

            report.AppendLine($"[가구] 난간 기둥 {posts}개(양안 z ±{kZPlayEdge:F0}) + 가로등 {lamps}주 + 가림막 2면");
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.isStatic = true;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
    }

    /// <summary>
    /// 광통교 보강 자동 실행기 — 에디터 로드(컴파일) 직후 1회. 보강 내용을 고치면 kVersion을 올린다.
    /// (이 프로젝트는 에디터를 띄워둔 채 작업해 배치모드를 못 쓴다 — DDP·롯데월드와 같은 패턴.)
    /// 1: 최초 도입 — 덤불 복구·Missing 머티리얼·원경 파사드·물길 연장·가로수.
    /// 2: 덤불 텍스처를 diffuse 우선으로 선택 — GLB의 normal·diffuse가 같은 2048²라
    ///    '가장 큰 것' 기준으로는 노말맵이 컬러 슬롯에 걸릴 수 있었다.
    /// 3: 덤불을 머티리얼 교체가 아니라 GLB 프리팹 '재인스턴스'로 복구(QA: 여전히 안 보임 —
    ///    낡은 인스턴스의 내부 참조가 죽은 것으로 판단) + 텍스처를 추출 PNG로 교체.
    /// 4: QA 반려 반영 — 워터 연장·가로수 107주 제거, 물길 남북 끝을 '아치 수문'(석벽+아치 링+
    ///    교각+어두운 통수구)으로 막아 물·돌계단과 자연스럽게 잇는다.
    /// 5: 프리미티브 수문도 반려("여전히 어색") — 남쪽 끝의 실제 광통교 다리(bridge.glb 인스턴스)를
    ///    복제해 북쪽 미러 + 남북 원경(±105m)에 반복 배치. 기존 에셋 재활용이라 톤이 정확히 같다.
    /// 6: 다리 너머 물길 연장 — Water(WindingWater)·Stairs 그룹을 남북으로 거울-타일 1장씩
    ///    (끝단 미러라 곡선 이음새 없음). QA "물길 양옆 연장 + 석재 계단 이어서".
    /// 7: v6의 그룹 이름 기반 선택이 회랑 일부만 집어 빗나감(측정 z -27~-16) — 공간 슬라이스
    ///    (회랑 |x|≤16 · z ±33 · y≤8 안의 렌더러 전부)로 교체, 렌더러 단위 사본 + z=±33 미러.
    /// 8: v7 '완전 포함' 필터가 핵심(Plane 물바닥 z -37.5~36.8, Walls, 바닥돌)을 전부 놓침 —
    ///    '코어 교차 + 방향별 침범 가드'로 선별해 ±70m 평행이동 복제(이음새는 다리 뒤, y -0.03 겹침 가드).
    /// </summary>
    [InitializeOnLoad]
    public static class GwangTongGyoAutoSetup
    {
        private const int kVersion = 18;   // 18: 덤불 주당 4주(생울타리 밀도) — 09/03 2차 "더 빽빽" 지시
        private const string kKey = "GwangTongGyo.PolishVersion";

        static GwangTongGyoAutoSetup()
        {
            EditorApplication.delayCall += TryRun;
            // 컴파일 직후 곧장 플레이에 들어가면 위 delayCall이 스킵되고, 플레이 종료는 도메인 리로드가
            // 없어 영영 안 돌았다(09/03 v16 미적용 사고 — 삼각형 픽스가 프리팹에 안 실린 채 QA됨).
            // 플레이가 끝나 에디트 모드로 돌아온 시점에 재시도한다.
            EditorApplication.playModeStateChanged += s =>
            { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryRun; };
        }

        private static void TryRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorPrefs.GetInt(kKey, 0) >= kVersion) return;
            Debug.Log("[광통교보강] 자동 실행 (Tools ▸ Map ▸ ★ 광통교 그래픽 보강)");
            GwangTongGyoPolishTool.Apply();
            GwangTongGyoPolishTool.Snapshot();
            EditorPrefs.SetInt(kKey, kVersion);
        }
    }
}

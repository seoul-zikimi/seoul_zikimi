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

                PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
                foreach (var leftover in new[] { "Mat_GtgStreamWater", "Mat_GtgLeaf", "Mat_GtgTrunk" })   // 1차 버전(워터·가로수) 잔재 정리
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
            int replaced = 0, nullMesh = 0, noSrc = 0;

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
                    foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                    foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mat != null) r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                    }
                    replaced++;
                }
            }

            report.AppendLine($"[덤불] 재인스턴스 {replaced}주 · (진단) 기존 인스턴스의 빈 메시 {nullMesh}개 · 소스 불명 {noSrc}개");
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
            var mat = EnsureLit(name, Color.white);
            if (mat == null) return null;
            mat.SetTexture("_BaseMap", best);
            EditorUtility.SetDirty(mat);
            return mat;
        }

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

        // ───────────────────── ④ 물길 끝 아치 수문 + 보도 스트립 ─────────────────────
        // 1차 버전(워터 1km 연장 + 가로수 107주)은 QA 반려("물이 이상함", "나무 삭제").
        // 2차 지시: "밑의 물·돌계단과 자연스럽게 이으면서 벽이나 아치 몇 개 추가" —
        // 물길 남북 끝을 석벽으로 막되, 앞면에 타원 아치 링 + 교각 + 어두운 통수구를 세워
        // 물이 아치 밑으로 흘러 나가는 수문처럼 보이게 한다(맵의 광통교 아치교와 같은 어법).
        private static void BuildExtras(GameObject root, StringBuilder report)
        {
            var old = root.transform.Find(kGroupName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var grp = new GameObject(kGroupName);
            grp.transform.SetParent(root.transform, false);

            var stone  = EnsureLit("Mat_GtgEmbankment", new Color(0.76f, 0.74f, 0.69f));
            var stoneD = EnsureLit("Mat_GtgArchStone",  new Color(0.68f, 0.66f, 0.61f));   // 아치 링 — 살짝 어두운 석재(윤곽 대비)
            var mouth  = EnsureLit("Mat_GtgTunnelMouth", new Color(0.10f, 0.11f, 0.13f));  // 통수구 속 어둠
            var walkMat = EnsureLit("Mat_GtgWalkway", new Color(0.84f, 0.82f, 0.77f));

            float chanW  = kChanXMax - kChanXMin + 1.5f;      // 개구부 폭 + 양쪽 여유
            float chanCx = (kChanXMin + kChanXMax) * 0.5f;
            const float kFloorY = -1f;                        // 물길 바닥보다 살짝 아래(틈 방지)
            const float kSpringY = 1.0f;                      // 아치 스프링 라인(수면 위)
            const float kArchHalfW = 8f, kArchH = 3.5f;       // 타원 아치 반폭·높이 → 꼭대기 4.5(둔치 5.42 아래)

            for (int dir = -1; dir <= 1; dir += 2)
            {
                string tag = dir < 0 ? "S" : "N";
                float capZ = dir * (kZPlayEdge + 1.2f);
                float faceZ = capZ - dir * 1.3f;              // 플레이 쪽 앞면

                // 몸통 벽: 물길 단면 전체(바닥 ~ 둔치 윗면)
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prep(cap, grp.transform, $"ChannelCap{tag}", stone);
                float h = kBankTopY - kFloorY;
                cap.transform.position = new Vector3(chanCx, kFloorY + h * 0.5f, capZ);
                cap.transform.localScale = new Vector3(chanW, h, 2.4f);

                // 갓돌: 벽 위 밝은 마감 슬래브(실제 옹벽 마감처럼 살짝 돌출)
                var coping = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prep(coping, grp.transform, $"CapCoping{tag}", walkMat);
                coping.transform.position = new Vector3(chanCx, kBankTopY + 0.14f, capZ);
                coping.transform.localScale = new Vector3(chanW + 0.6f, 0.3f, 3f);

                // 통수구: 아치 안쪽을 채우는 어두운 면 — 물이 그 속으로 흘러드는 깊이감
                var dark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prep(dark, grp.transform, $"TunnelMouth{tag}", mouth);
                float mouthH = kSpringY + kArchH - 0.25f - kFloorY;
                dark.transform.position = new Vector3(chanCx, kFloorY + mouthH * 0.5f, faceZ + dir * 0.02f);
                dark.transform.localScale = new Vector3((kArchHalfW - 0.3f) * 2f, mouthH, 0.25f);

                // 아치 링: 타원(반폭 8 × 높이 3.5)을 따라 석재 세그먼트 11개
                const int kSegs = 11;
                for (int i = 0; i < kSegs; i++)
                {
                    float t0 = Mathf.PI * (i + 0.5f) / kSegs;   // 10°~170° 구간을 균등 분할
                    float x = chanCx + kArchHalfW * Mathf.Cos(t0);
                    float y = kSpringY + kArchH * Mathf.Sin(t0);
                    // 세그먼트 길이 ≈ 타원 호 길이/개수 + 겹침 여유, 기울기 = 타원 접선
                    float segLen = 2.6f;
                    float ang = Mathf.Atan2(kArchH * Mathf.Cos(t0), -kArchHalfW * Mathf.Sin(t0)) * Mathf.Rad2Deg;
                    var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Prep(seg, grp.transform, $"ArchSeg{tag}{i}", stoneD);
                    seg.transform.position = new Vector3(x, y, faceZ);
                    seg.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                    seg.transform.localScale = new Vector3(segLen, 0.85f, 0.6f);
                }

                // 교각: 아치 양끝 밑을 받치는 돌기둥(앞으로 살짝 돌출)
                foreach (float px in new[] { chanCx - kArchHalfW - 0.7f, chanCx + kArchHalfW + 0.7f })
                {
                    var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Prep(pier, grp.transform, $"ArchPier{tag}{(px < chanCx ? "W" : "E")}", stoneD);
                    float ph = kSpringY + 0.9f - kFloorY;
                    pier.transform.position = new Vector3(px, kFloorY + ph * 0.5f, capZ - dir * 0.6f);
                    pier.transform.localScale = new Vector3(1.7f, ph, 3.4f);
                }
            }

            // ── 보도 스트립: 물길 양안 위 2m 폭 밝은 콘크리트 띠(맨 아스팔트와 물길 사이 완충)
            for (int dir = -1; dir <= 1; dir += 2)
            foreach (float x in new[] { kChanXMin - 1.6f, kChanXMax + 1.6f })
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Prep(strip, grp.transform, $"Walkway{(dir < 0 ? "S" : "N")}{(x < 0 ? "W" : "E")}", walkMat);
                strip.transform.position = new Vector3(x, kBankTopY + 0.02f, dir * (kZPlayEdge + kZFar) * 0.5f);
                strip.transform.localScale = new Vector3(2.4f, 0.05f, kZFar - kZPlayEdge);
            }

            report.AppendLine("[연장] 남북 아치 수문(벽+아치 링 11segs+교각+통수구+갓돌) + 보도 스트립 — 워터·가로수는 QA 반려로 제거");
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
    /// </summary>
    [InitializeOnLoad]
    public static class GwangTongGyoAutoSetup
    {
        private const int kVersion = 4;
        private const string kKey = "GwangTongGyo.PolishVersion";

        static GwangTongGyoAutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;   // 플레이 끝난 다음 리로드 때 다시 시도
                if (EditorPrefs.GetInt(kKey, 0) >= kVersion) return;
                Debug.Log("[광통교보강] 자동 실행 (Tools ▸ Map ▸ ★ 광통교 그래픽 보강)");
                GwangTongGyoPolishTool.Apply();
                EditorPrefs.SetInt(kKey, kVersion);
            };
        }
    }
}

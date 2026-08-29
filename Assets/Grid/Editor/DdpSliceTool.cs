using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ DDP 본관 통짜 모델을 격자로 잘라 '고유 파츠'로 만든다.
    ///
    /// 왜 이렇게 하나:
    ///   DDP는 직선도 모서리도 없는 하나의 연속된 곡면 덩어리다. 파츠를 8종 따로 생성해서 쌓으면
    ///   조각끼리 곡면이 이어지지 않아 무슨 짓을 해도 '블록 쌓기'로 보인다.
    ///   그래서 통짜 DDP 하나를 뽑아 두고, 그걸 칸 경계로 잘라 파츠를 만든다.
    ///   조각들이 원래 한 몸이라 다 맞추면 곡면이 그대로 이어진다.
    ///
    /// 자르는 방향은 평면(X·Z)뿐이고 높이(Y)로는 자르지 않는다:
    ///   · 모든 조각이 바닥에서 시작하므로 지지(support) 문제가 없다
    ///   · 잘린 단면이 전부 수직면이라, 붙여 놓으면 이웃 조각이 가려 준다
    ///   · DDP 자체가 낮고 넓어서 층으로 쪼갤 만한 높이가 아니다
    ///
    /// 조각이 칸을 꽉 채우지 않으므로 MaterialDef.FreeformVisual을 켠다
    /// (MaterialPrefabContractTests의 피벗·크기 검사를 건너뛴다. 대신 이 툴이 피벗을
    ///  '칸의 min-corner'에 정확히 맞출 책임을 진다 — 안 그러면 조각이 어긋난다).
    ///
    /// 통짜 GLB가 없으면 조용히 null을 돌려주고, DdpMapTool이 예전 블록 파츠로 폴백한다.
    ///
    /// ⚠ 조각의 머티리얼은 원본 GLB 것을 그대로 쓴다. 새 머티리얼로 갈아끼우면 텍스처가 날아가
    ///   전부 새하얀 덩어리가 된다(한 번 겪었다).
    /// </summary>
    public static class DdpSliceTool
    {
        private const string kDir      = "Assets/Prefabs/Map/4_Ddp";
        private const string kModelDir = kDir + "/Models";
        private const string kOutDir   = kDir + "/Sliced";
        private const string kSourceName = "DDP_본관";

        /// <summary>파츠 id 시작값. 기존 블록 파츠(31~38)와 겹치지 않게 40번대를 쓴다.</summary>
        private const int kIdBase = 40;

        /// <summary>통짜가 들어갈 최대 칸 수. 그리드(14×6×14) 안에서 이 상자에 비율 유지로 맞춘다.
        /// 비율 유지라 셋 중 가장 빡빡한 축이 크기를 결정한다 — DDP는 옆으로 길어서 보통 X가 잡는다.</summary>
        private static readonly Vector3Int kSpan = new Vector3Int(13, 5, 10);
        /// <summary>통짜의 min-corner가 앉을 셀.</summary>
        private static readonly Vector3Int kAnchor = new Vector3Int(0, 0, 2);

        /// <summary>평면 절단선(칸 단위, kSpan 기준 비율). X는 4구간, Z는 3구간 → 최대 12조각.
        /// 빈 구간(곡면이 안 지나가는 오목한 자리)은 자동으로 버려져서, 정답 평면이 직사각형이 아니게 된다.</summary>
        private static readonly float[] kCutX = { 0f, 0.27f, 0.5f, 0.74f, 1f };
        private static readonly float[] kCutZ = { 0f, 0.36f, 0.68f, 1f };

        /// <summary>이보다 삼각형이 적은 조각은 버린다(부스러기 방지).</summary>
        private const int kMinTrisPerTile = 40;

        public struct Piece
        {
            public MaterialDef Def;
            public Vector3Int Anchor;     // 그리드 셀(정답 앵커)
            public Vector3Int Footprint;
        }

        /// <summary>다 지었을 때 조각 대신 얹을 '자르기 전 통짜' 프리팹. 마지막 Slice() 실행 결과.</summary>
        public static GameObject CompletedModel { get; private set; }
        /// <summary>완성체를 놓을 기준 셀 — 조각들의 기준과 같다(kAnchor).</summary>
        public static Vector3Int CompletedAnchor => kAnchor;

        [MenuItem("Tools/Map/★ DDP 통짜 모델 격자 절단")]
        public static void SliceMenu()
        {
            var pieces = Slice();
            if (pieces == null)
            {
                EditorUtility.DisplayDialog("DDP 절단",
                    $"통짜 모델이 없습니다:\n{kModelDir}/{kSourceName}.glb\n\nVARCO에서 뽑아 넣고 다시 실행하세요.", "확인");
                return;
            }
            Debug.Log($"[DDP절단] 조각 {pieces.Count}개 생성 — Tools ▸ Map ▸ ★ DDP 맵 생성 을 실행해 정답에 반영하세요.");
        }

        /// <summary>통짜 모델이 있으면 잘라서 파츠 목록을 돌려준다. 없으면 null.</summary>
        public static List<Piece> Slice()
        {
            var model = LoadSource();
            if (model == null) return null;

            if (!BakeTriangles(model, out var verts, out var norms, out var uvs,
                               out var tris, out var triMat, out var mats))
                return null;

            // ── 긴 축이 X가 되도록 세운다 ──
            // Generate3D 결과의 방향은 매번 다르다. 우리 절단 격자는 'X가 긴 축'을 전제하므로,
            // 깊이가 폭보다 크면 Y축으로 90° 돌려 맞춘다(회전이라 형태는 안 망가진다).
            {
                var pre = new Bounds(verts[0], Vector3.zero);
                foreach (var v in verts) pre.Encapsulate(v);
                if (pre.size.z > pre.size.x)
                {
                    for (int i = 0; i < verts.Count; i++)
                        verts[i] = new Vector3(verts[i].z, verts[i].y, -verts[i].x);
                    for (int i = 0; i < norms.Count; i++)
                        norms[i] = new Vector3(norms[i].z, norms[i].y, -norms[i].x);
                    Debug.Log("[DDP절단] 긴 축이 Z였어서 90° 회전해 X로 맞춤");
                }
            }

            // ── 통짜를 kSpan 상자에 '비율 유지'로 맞춘다(1유닛 = 1칸) ──
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            float k = Mathf.Min(kSpan.x / b.size.x, kSpan.y / b.size.y, kSpan.z / b.size.z);

            // min-corner가 원점에 오도록 스케일 + 이동 (이제 좌표 = 칸 좌표)
            for (int i = 0; i < verts.Count; i++)
                verts[i] = (verts[i] - b.min) * k;

            var span = b.size * k;                                  // 실제 점유 크기(칸)
            int cw = Mathf.Max(1, Mathf.CeilToInt(span.x - 1e-3f));
            int cd = Mathf.Max(1, Mathf.CeilToInt(span.z - 1e-3f));
            Debug.Log($"[DDP절단] 통짜 크기 {span.x:F2}×{span.y:F2}×{span.z:F2}칸 → 평면 {cw}×{cd}칸에 분할");

            Directory.CreateDirectory(kOutDir);

            // ── 자르기 전 통짜를 '완성체' 프리팹으로 따로 저장 ──
            // 조각을 아무리 잘 맞춰도 잘린 단면 때문에 완성본이 매끈하게 안 보인다.
            // 그래서 다 지으면 조각을 감추고 이 원본 하나로 갈아 끼운다(완공 계획도도 이걸 쓴다).
            // 좌표계는 조각과 완전히 동일 — 원점이 kAnchor 셀의 min-corner다.
            CompletedModel = BuildCompletedPrefab(model, b.min, k);

            var xs = CutCells(kCutX, cw);
            var zs = CutCells(kCutZ, cd);
            var pieces = new List<Piece>();
            int id = kIdBase;
            int emitted = 0, triCount = tris.Count / 3;

            for (int xi = 0; xi < xs.Length - 1; xi++)
            for (int zi = 0; zi < zs.Length - 1; zi++)
            {
                int x0 = xs[xi], x1 = xs[xi + 1];
                int z0 = zs[zi], z1 = zs[zi + 1];
                if (x1 <= x0 || z1 <= z0) continue;

                var piece = BuildTile(verts, norms, uvs, tris, triMat, mats,
                                      x0, x1, z0, z1, kSpan.y, id, xi, zi, out int used);
                if (piece == null) continue;
                pieces.Add(piece.Value);
                emitted += used;
                id++;
            }

            // 버려진 삼각형이 있으면 그만큼 껍데기에 구멍이 남는다 — 숫자로 알려 준다.
            if (emitted != triCount)
                Debug.LogWarning($"[DDP절단] 삼각형 {triCount - emitted}개가 조각에 안 들어감 " +
                                 $"(원본 {triCount} → 합계 {emitted}). 부스러기 타일(kMinTrisPerTile 미만)이 버려진 결과입니다.");
            else
                Debug.Log($"[DDP절단] ✔ 삼각형 {triCount}개 전부 분배됨 — 다 맞추면 원본과 같은 형태가 됩니다.");

            AssetDatabase.SaveAssets();
            return pieces.Count > 0 ? pieces : null;
        }

        /// <summary>자르기 전 통짜를 조각과 똑같은 좌표계(원점 = kAnchor 셀 min-corner, 1유닛 = 1칸)로
        /// 래핑한 프리팹. 머티리얼은 원본 그대로 둔다 — 갈아끼우면 텍스처가 날아간다.</summary>
        private static GameObject BuildCompletedPrefab(GameObject model, Vector3 rawMin, float k)
        {
            var root = new GameObject("DDP_본관_완성");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            // 조각과 같은 변환: (v - rawMin) * k. 단 조각 쪽에서 '긴 축이 Z면 90° 회전'을 했다면 여기도 맞춰야 한다.
            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Object.DestroyImmediate(root); return null; }
            var wb = rends[0].bounds;
            foreach (var r in rends) wb.Encapsulate(r.bounds);
            // 조각 절단은 정점을 (z, y, -x)로 돌린다 = Euler(0, +90°, 0). 완성체도 같은 방향이어야
            // 완공 계획도·정답 고스트가 조각 배치와 일치한다 — -90°로 돌리면 180° 뒤집혀
            // '뾰족한 끝이 반대편에 표시되는' 정답 UI가 된다.
            bool yaw = wb.size.z > wb.size.x;
            if (yaw) inst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            inst.transform.localScale = Vector3.one * k;

            // 스케일·회전 반영 뒤 바운즈 min이 원점에 오도록 이동
            wb = rends[0].bounds;
            foreach (var r in rends) wb.Encapsulate(r.bounds);
            inst.transform.localPosition -= (wb.min - root.transform.position);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{kOutDir}/DDP_본관_완성.prefab");
            Object.DestroyImmediate(root);
            Debug.Log("[DDP절단] 완성체 프리팹 저장 — 다 지으면 조각 대신 이게 얹힌다");
            return prefab;
        }

        // 비율(0~1) 절단선을 칸 인덱스로. 중복 제거 후 오름차순.
        private static int[] CutCells(float[] frac, int cells)
        {
            var set = new SortedSet<int>();
            foreach (var f in frac) set.Add(Mathf.Clamp(Mathf.RoundToInt(f * cells), 0, cells));
            set.Add(0); set.Add(cells);
            var arr = new int[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        // 한 타일: 삼각형 중심이 [x0,x1)×[z0,z1) 안에 드는 것만 모아 메시·프리팹·def을 만든다.
        private static Piece? BuildTile(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                                        List<int> tris, List<int> triMat, List<Material> mats,
                                        int x0, int x1, int z0, int z1, int maxH,
                                        int id, int xi, int zi, out int usedTris)
        {
            usedTris = 0;
            var map = new Dictionary<int, int>();          // 원본 정점 index → 새 index
            var nv = new List<Vector3>();
            var nn = new List<Vector3>();
            var nu = new List<Vector2>();
            var sub = new List<List<int>>();
            for (int i = 0; i < mats.Count; i++) sub.Add(new List<int>());

            float maxY = 0f;
            int kept = 0;

            for (int t = 0; t < tris.Count; t += 3)
            {
                var a = verts[tris[t]]; var b2 = verts[tris[t + 1]]; var c = verts[tris[t + 2]];
                var ctr = (a + b2 + c) / 3f;
                if (ctr.x < x0 || ctr.x >= x1 || ctr.z < z0 || ctr.z >= z1) continue;

                int m = triMat[t / 3];
                for (int e = 0; e < 3; e++)
                {
                    int oi = tris[t + e];
                    if (!map.TryGetValue(oi, out int ni))
                    {
                        ni = nv.Count;
                        map[oi] = ni;
                        // 피벗 = '칸의 min-corner' (메시 바운즈가 아니다 — 그래야 조각들이 제자리에 맞는다)
                        nv.Add(verts[oi] - new Vector3(x0, 0f, z0));
                        nn.Add(oi < norms.Count ? norms[oi] : Vector3.up);
                        nu.Add(oi < uvs.Count ? uvs[oi] : Vector2.zero);
                    }
                    sub[m].Add(ni);
                    maxY = Mathf.Max(maxY, verts[oi].y);
                }
                kept++;
            }

            if (kept < kMinTrisPerTile) return null;       // 부스러기 타일은 버린다
            usedTris = kept;

            // 밀폐 스커트(단면 커튼 + 바닥 뚜껑) — 정점 배열 뒤에 이어붙이고 전용 서브메시로 넣는다.
            var silver = EnsureSilver();
            var skirt = silver != null ? BuildSkirt(nv, nn, nu, sub) : null;
            bool hasSkirt = skirt != null && skirt.Count > 0;

            var mesh = new Mesh { name = $"DDP_조각_{xi}{zi}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(nv);
            mesh.SetNormals(nn);
            mesh.SetUVs(0, nu);
            int used = 0;
            foreach (var s in sub) if (s.Count > 0) used++;
            mesh.subMeshCount = used + (hasSkirt ? 1 : 0);
            var usedMats = new List<Material>();
            int si = 0;
            for (int m = 0; m < sub.Count; m++)
            {
                if (sub[m].Count == 0) continue;
                mesh.SetTriangles(sub[m], si++);
                usedMats.Add(mats[m]);
            }
            if (hasSkirt)
            {
                mesh.SetTriangles(skirt, si);
                usedMats.Add(silver);
            }
            mesh.RecalculateBounds();

            string baseName = $"DDP_조각_{xi}{zi}";
            AssetDatabase.CreateAsset(mesh, $"{kOutDir}/{baseName}.asset");

            // 프리팹 — 머티리얼은 원본 GLB 것을 그대로 쓴다(갈아끼우면 텍스처가 날아간다).
            var go = new GameObject(baseName);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = usedMats.ToArray();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{kOutDir}/{baseName}.prefab");
            Object.DestroyImmediate(go);

            int h = Mathf.Clamp(Mathf.CeilToInt(maxY - 1e-3f), 1, maxH);
            var fp = new Vector3Int(x1 - x0, h, z1 - z0);

            // 조각이 자기 칸을 벗어나면 옆 조각과 겹쳐 보인다 — 벗어난 양을 그대로 찍어 준다.
            var mb = mesh.bounds;
            float over = Mathf.Max(Mathf.Max(-mb.min.x, -mb.min.z, 0f),
                                   Mathf.Max(mb.max.x - fp.x, mb.max.z - fp.z));
            if (over > 0.15f)
                Debug.LogWarning($"[DDP절단] {baseName}: 칸을 {over:F2} 벗어남 " +
                                 $"(바운즈 {mb.min}~{mb.max}, footprint {fp})");

            // MaterialDef — 조각마다 고유 id. 공정은 번갈아 줘서 도구를 다 쓰게 한다.
            var def = LoadOrCreate<MaterialDef>($"{kOutDir}/{baseName}_Def.asset");
            var so = new SerializedObject(def);
            so.FindProperty("m_Id").intValue = id;
            so.FindProperty("m_Footprint").vector3IntValue = fp;
            so.FindProperty("m_Prefab").objectReferenceValue = prefab;
            so.FindProperty("m_FreeformVisual").boolValue = true;
            var procs = so.FindProperty("m_RequiredProcesses");
            procs.arraySize = 1;
            procs.GetArrayElementAtIndex(0).intValue = (int)((id % 2 == 0) ? ProcessType.Fixed : ProcessType.Painted);
            so.FindProperty("m_MustBeFixed").boolValue = false;
            so.FindProperty("m_Walkable").boolValue = false;
            so.FindProperty("m_IsBreakable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);

            Debug.Log($"[DDP절단] {baseName}: 삼각형 {kept}, footprint {fp}, id {id}");
            return new Piece { Def = def, Anchor = kAnchor + new Vector3Int(x0, 0, z0), Footprint = fp };
        }

        // ── 밀폐 스커트 ──────────────────────────────────────────────────
        // 조각은 통짜 '껍데기'의 삼각형을 통째로 나눠 가진 것이라, 잘린 단면과 밑면이 뻥 뚫려 있다.
        // 그대로 두면 주문 배달로 날아오며 구를 때·밑에서 올려다볼 때 속 빈 '기본 상자'처럼 보인다.
        // 경계 간선(한 삼각형만 쓰는 간선)마다 바닥(y=0)까지 은색 커튼을 내리고 바닥 뚜껑을 덮어
        // 어느 각도에서 봐도 '잘라낸 덩어리'로 보이게 한다. 단면이 은색인 건 의도 — 금속 패널 건물의 절단면.
        // ⚠ 원본 서브메시·머티리얼은 절대 건드리지 않는다(별도 서브메시로만 추가) —
        //   머티리얼을 갈아끼우면 텍스처가 날아간다(kSetupVersion 7의 '전부 새하얀 덩어리' 사고).
        private static List<int> BuildSkirt(List<Vector3> nv, List<Vector3> nn, List<Vector2> nu, List<List<int>> sub)
        {
            const float kGroundEps = 0.02f;   // 이 아래 간선은 이미 바닥에 닿아 있다 — 커튼 불필요
            const float kInset = 0.012f;      // 이웃 조각의 커튼(같은 절단면 공유)과 z-파이팅 나지 않게 제 안쪽으로

            // 0.5mm 양자화 — UV 솔기로 정점이 갈라져 있어도 같은 자리면 같은 간선으로 본다.
            // (정점 인덱스 기준으로 세면 솔기가 전부 '가짜 경계'로 잡혀 껍데기 한복판에 커튼이 선다)
            static (int, int, int) Q(Vector3 v)
                => (Mathf.RoundToInt(v.x * 2000f), Mathf.RoundToInt(v.y * 2000f), Mathf.RoundToInt(v.z * 2000f));

            var edges = new Dictionary<((int, int, int), (int, int, int)), (int a, int b, int uses)>();
            foreach (var tris in sub)
                for (int t = 0; t < tris.Count; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = tris[t + e], ib = tris[t + (e + 1) % 3];
                        var ka = Q(nv[ia]); var kb = Q(nv[ib]);
                        if (ka.Equals(kb)) continue;                          // 퇴화 간선
                        var key = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
                        edges[key] = edges.TryGetValue(key, out var cur) ? (cur.a, cur.b, cur.uses + 1) : (ia, ib, 1);
                    }

            var shell = new Bounds(nv[0], Vector3.zero);
            foreach (var v in nv) shell.Encapsulate(v);
            var center = shell.center;

            var idx = new List<int>();
            foreach (var kv in edges)
            {
                if (kv.Value.uses != 1) continue;                             // 경계 간선만
                var A = nv[kv.Value.a]; var B = nv[kv.Value.b];
                if (A.y < kGroundEps && B.y < kGroundEps) continue;           // 바닥 둘레는 이미 닫혀 있다

                var mid = (A + B) * 0.5f;
                var outDir = new Vector3(mid.x - center.x, 0f, mid.z - center.z);
                outDir = outDir.sqrMagnitude > 1e-6f ? outDir.normalized : Vector3.forward;
                var shift = -outDir * kInset;

                var a  = A + shift;
                var b2 = B + shift;
                var a0 = new Vector3(a.x, 0f, a.z);
                var b0 = new Vector3(b2.x, 0f, b2.z);

                int i0 = nv.Count;
                nv.Add(a); nv.Add(b2); nv.Add(b0); nv.Add(a0);
                for (int i = 0; i < 4; i++) { nn.Add(outDir); nu.Add(Vector2.zero); }

                // 커튼이 조각 바깥을 향하게 감기 방향 결정(뒷면 컬링 대비)
                var nrm = Vector3.Cross(b2 - a, b0 - a);
                if (Vector3.Dot(nrm, outDir) >= 0f) idx.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                else                                idx.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }

            if (idx.Count == 0) return idx;   // 열린 단면이 없으면 뚜껑도 필요 없다

            // 바닥 뚜껑(아래를 향하는 사각형) — 커튼 둘레보다 살짝 안쪽. 공중에서 구를 때 밑면이 뚫려 보이지 않게.
            float x0 = shell.min.x + 0.05f, x1 = shell.max.x - 0.05f;
            float z0 = shell.min.z + 0.05f, z1 = shell.max.z - 0.05f;
            if (x1 > x0 && z1 > z0)
            {
                int i0 = nv.Count;
                nv.Add(new Vector3(x0, 0.004f, z0)); nv.Add(new Vector3(x0, 0.004f, z1));
                nv.Add(new Vector3(x1, 0.004f, z1)); nv.Add(new Vector3(x1, 0.004f, z0));
                for (int i = 0; i < 4; i++) { nn.Add(Vector3.down); nu.Add(Vector2.zero); }
                idx.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });   // 순방향은 +Y라서 뒤집어 아래를 본다
            }
            return idx;
        }

        // 스커트용 은색 머티리얼 — DdpMapTool.EnsureMaterial("Mat_DdpSilver")과 같은 에셋을 쓴다(중복 생성 방지).
        private static Material EnsureSilver()
        {
            const string kPath = "Assets/Map/Materials/Mat_DdpSilver.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kPath);
            if (mat != null) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) { Debug.LogWarning("[DDP절단] URP Lit 셰이더를 못 찾음 — 스커트 생략"); return null; }
            mat = new Material(sh);
            mat.SetColor("_BaseColor", new Color(0.78f, 0.80f, 0.83f));
            Directory.CreateDirectory("Assets/Map/Materials");
            AssetDatabase.CreateAsset(mat, kPath);
            return mat;
        }

        // GLB의 모든 MeshFilter를 하나의 삼각형 수프로 굽는다(월드 변환 적용, 머티리얼 인덱스 유지).
        private static bool BakeTriangles(GameObject model,
            out List<Vector3> verts, out List<Vector3> norms, out List<Vector2> uvs,
            out List<int> tris, out List<int> triMat, out List<Material> mats)
        {
            verts = new List<Vector3>(); norms = new List<Vector3>(); uvs = new List<Vector2>();
            tris = new List<int>(); triMat = new List<int>(); mats = new List<Material>();

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (inst == null) return false;
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;

            try
            {
                var matIndex = new Dictionary<Material, int>();
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var l2w = mf.transform.localToWorldMatrix;

                    int vBase = verts.Count;
                    var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv;
                    for (int i = 0; i < mv.Length; i++)
                    {
                        verts.Add(l2w.MultiplyPoint3x4(mv[i]));
                        norms.Add(i < mn.Length ? l2w.MultiplyVector(mn[i]).normalized : Vector3.up);
                        uvs.Add(i < mu.Length ? mu[i] : Vector2.zero);
                    }

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var m = (mr != null && s < mr.sharedMaterials.Length) ? mr.sharedMaterials[s] : null;
                        if (m == null) m = mats.Count > 0 ? mats[0] : null;
                        int mi;
                        if (m == null) { mi = 0; if (mats.Count == 0) mats.Add(null); }
                        else if (!matIndex.TryGetValue(m, out mi)) { mi = mats.Count; matIndex[m] = mi; mats.Add(m); }

                        var st = mesh.GetTriangles(s);
                        for (int i = 0; i < st.Length; i += 3)
                        {
                            tris.Add(vBase + st[i]); tris.Add(vBase + st[i + 1]); tris.Add(vBase + st[i + 2]);
                            triMat.Add(mi);
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(inst); }

            if (mats.Count == 0) mats.Add(null);
            return verts.Count > 0 && tris.Count >= 3;
        }

        private static GameObject LoadSource()
        {
            foreach (var ext in new[] { "glb", "fbx", "obj" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{kModelDir}/{kSourceName}.{ext}");
                if (go != null) return go;
            }
            return null;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }
    }
}

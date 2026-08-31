using System.IO;
using UnityEditor;
using UnityEngine;

namespace GridSystem.EditorTools
{
    /// <summary>
    /// ★ DDP 본관 '파츠 조립' — VARCO 파츠 3종을 실물 배치대로 이어 붙여 절단용 통짜를 만든다.
    ///
    /// 왜: 통짜 한 방 생성(이미지→3D)은 전체 실루엣을 자꾸 틀리게 뽑았다(지붕 언덕이 웅덩이가 되거나,
    /// 파츠 배치가 실물과 다르게 나옴). 그래서 기획 결정(08/31)대로 항공사진의 3분할을 그대로 따라
    /// 파츠를 따로 뽑고 여기서 배치만 실물대로 맞춘다:
    ///   · DDP_윗동   — 북쪽 부메랑 밴드(안쪽에 황갈색 지붕 골짜기)
    ///   · DDP_중간동 — 은색 돔 머리(동쪽, 최고 높이) + 갈색 지붕 몸통
    ///   · DDP_꼬리동 — 남서로 길게 흐르는 낮은 갈색 지붕 날개
    ///
    /// 결과는 DDP_본관_조립.prefab 하나 — DdpSliceTool.LoadSource()가 통짜 GLB보다 이걸 우선한다.
    /// 이후 파이프라인(긴 축 X 정규화 → kSpan 비율 맞춤 → 격자 절단 → 완성체)은 그대로 재사용.
    ///
    /// ⚠ Generate3D 결과의 방향은 매번 다르다. 파츠마다 긴 축을 X로 자동 정규화(z>x면 90° 회전)하지만
    ///   '어느 끝이 어느 쪽인가'(180° 뒤집힘)는 알 수 없다 — 씬 미리보기를 보고 아래 kParts의
    ///   yaw에 ±180을 더해 교정하라. 파츠 GLB를 새로 뽑아 갈 때마다 확인할 것.
    /// </summary>
    public static class DdpAssembleTool
    {
        private const string kDir      = "Assets/Prefabs/Map/4_Ddp";
        private const string kModelDir = kDir + "/Models";
        private const string kPrefabPath = kDir + "/DDP_본관_조립.prefab";

        /// <summary>파츠 배치(항공사진 3분할 기준, 1유닛 ≈ 1m 감각 — 절단 툴이 전체를 kSpan에 다시 맞춘다).
        /// size = 목표 상자(길이×높이×깊이), pos = 바운즈 중심 XZ, yaw = 긴 축 X 정규화 후 회전.
        ///
        /// ⚠ 높이는 일부러 과장돼 있다(실물 DDP는 높이/길이 ≈ 0.1의 납작한 건물 —
        ///   그대로 두면 절단 후 1~2칸짜리 팬케이크가 돼 "맵이 엄청 작아" 보인다).
        ///   XZ는 비율 유지로 맞추고 Y만 size.y로 강제해(BuildAssembly ②) 최종 머리가 ~6칸이 되게 한다.</summary>
        private static readonly (string name, Vector3 size, Vector2 pos, float yaw)[] kParts =
        {
            // 북쪽 밴드 — 동쪽 끝이 갈고리처럼 남쪽으로 감긴다. 살짝 기울여(-8°) 동단이 남으로 처지게.
            ("DDP_윗동",   new Vector3(23f, 11f, 8f), new Vector2(25f, 7.2f),  -8f),
            // 머리+몸통 — 은색 돔 머리가 동쪽(최고 높이). 밴드 남쪽에 물려 이음새를 겹친다.
            ("DDP_중간동", new Vector3(22f, 15f, 9f), new Vector2(26f, 0.2f),  -10f),
            // 꼬리 — 북동에서 남서로 흐르는 대각선(-43°). 넓은 끝이 중간동 서측에 파고든다.
            ("DDP_꼬리동", new Vector3(24f, 8f, 8f),  new Vector2(10f, -8f),   -43f),
        };

        [MenuItem("Tools/Map/★ DDP 파츠 3종 조립(절단용 통짜)")]
        public static void AssembleMenu()
        {
            var prefab = BuildAssembly();
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("DDP 조립",
                    $"파츠 GLB 3종이 모두 있어야 합니다:\n{kModelDir}/DDP_윗동·DDP_중간동·DDP_꼬리동.glb", "확인");
                return;
            }
            Selection.activeObject = prefab;
            Debug.Log("[DDP조립] 완료 ✔ Tools ▸ Map ▸ ★ DDP 맵 생성 을 실행하면 이 조립본이 잘려 정답이 됩니다.");
        }

        /// <summary>파츠 3종이 모두 있으면 배치대로 조립한 프리팹을 (재)생성해 돌려준다. 하나라도 없으면 null.</summary>
        public static GameObject BuildAssembly()
        {
            var models = new GameObject[kParts.Length];
            for (int i = 0; i < kParts.Length; i++)
            {
                models[i] = LoadModel(kParts[i].name);
                if (models[i] == null) return null;   // 파츠가 덜 모였으면 통짜 GLB 폴백(DdpSliceTool)
            }

            var root = new GameObject("DDP_본관_조립");
            try
            {
                for (int i = 0; i < kParts.Length; i++)
                {
                    var (name, size, pos, yaw) = kParts[i];
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(models[i], root.transform);
                    inst.name = name;

                    // ① 긴 축을 X로 정규화 — Generate3D 방향 복불복 흡수(180° 뒤집힘은 yaw로 교정)
                    var b = RendererBounds(inst);
                    if (b.size.z > b.size.x)
                        inst.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                    // ② XZ는 비율 유지, Y는 size.y로 강제 — 실물 비율(높이/길이 ≈ 0.1)대로 두면
                    //    절단 후 1~2칸 팬케이크가 된다. 곡면은 세로로 늘어나도 '더 DDP답게' 부풀 뿐 안 깨진다.
                    b = RendererBounds(inst);
                    float k  = Mathf.Min(size.x / b.size.x, size.z / b.size.z);
                    float ky = size.y / b.size.y;
                    inst.transform.localScale = Vector3.Scale(inst.transform.localScale, new Vector3(k, ky, k));

                    // ③ 실물 배치 회전
                    inst.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * inst.transform.localRotation;

                    // ④ 바운즈 중심 XZ = pos, 밑면 = y0 (전 파츠가 같은 바닥에 앉는다)
                    b = RendererBounds(inst);
                    inst.transform.localPosition += new Vector3(pos.x - b.center.x, -b.min.y, pos.y - b.center.z);
                }

                Directory.CreateDirectory(kDir);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
                var total = new Bounds();
                bool first = true;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>())
                {
                    if (first) { total = r.bounds; first = false; }
                    else total.Encapsulate(r.bounds);
                }
                Debug.Log($"[DDP조립] 파츠 3종 조립 ✔ 전체 {total.size.x:F1}×{total.size.y:F1}×{total.size.z:F1} → {kPrefabPath}\n" +
                          "방향이 뒤집힌 파츠가 있으면 DdpAssembleTool.kParts의 yaw에 ±180을 더해 재실행하세요.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            return b;
        }

        private static GameObject LoadModel(string name)
        {
            foreach (var ext in new[] { "glb", "fbx", "obj" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{kModelDir}/{name}.{ext}");
                if (go != null) return go;
            }
            return null;
        }
    }
}

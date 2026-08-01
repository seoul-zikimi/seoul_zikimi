using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 네트워크 없이 굴리는 샌드박스 테스터(테스트 씬 전용).
    /// · WASD = 테스트 플레이어 이동 → 블록 색으로 2칸 사거리 판정 확인(초록=닿음, 빨강=안 닿음)
    ///   1×1 / 3×3 / 9×9 블록을 놔서, 큰 블록도 가장자리에 서면 닿는지 바로 보인다.
    /// · Spot_DeliveryZone 마커를 끌어 옮기면 노란 마커가 '실제 배송될 위치'(클램프 적용 후)를 보여준다.
    ///   마커가 안 따라오면 그리드 밖으로 잘린 것.
    /// 아이템 파티클/사운드는 같은 씬의 ItemFxTester(숫자키)가 담당.
    /// </summary>
    public class SandboxTester : MonoBehaviour
    {
        [SerializeField] private float m_ReachCells = 2f;      // PlayerCarry의 사거리와 같은 값
        [SerializeField] private Vector3Int m_GridSize = new(8, 6, 8);
        [SerializeField] private float m_MoveSpeed = 6f;
        [SerializeField] private Transform m_DeliveryPoint;    // 씬의 "Spot_DeliveryZone" 마커

        private Transform m_Player;
        private Transform m_DeliveryMarker;
        private readonly List<(Transform go, List<Vector3Int> cells)> m_Blocks = new();
        private static readonly Color kIn = new(0.3f, 0.85f, 0.4f), kOut = new(0.85f, 0.3f, 0.3f);

        private void Start()
        {
            GridContract.Origin = Vector3.zero;

            m_Player = MakeBox("TestPlayer", new Vector3(0.5f, 0.9f, 0.5f), new Vector3(0.6f, 1.8f, 0.6f),
                               new Color(0.25f, 0.55f, 0.95f)).transform;

            AddBlock("Block_1x1", 3, 0, 1, 1);
            AddBlock("Block_3x3", 3, 4, 3, 3);
            AddBlock("Block_9x9", 10, 0, 9, 9);   // 중심이 멀어도 가장자리에 서면 닿아야 하는 케이스

            m_DeliveryMarker = MakeBox("~DeliveryResult", Vector3.zero, new Vector3(1.2f, 0.1f, 1.2f),
                                       new Color(0.95f, 0.8f, 0.2f)).transform;

            // 테스트 씬 전용: 블록·배송 지점이 한 화면에 다 들어오게 카메라를 잡아준다.
            if (Camera.main != null)
                Camera.main.transform.SetPositionAndRotation(new Vector3(7f, 22f, -12f), Quaternion.Euler(48f, 0f, 0f));

            Debug.Log("[Sandbox] WASD=이동 / 블록 초록=사거리 안, 빨강=밖 / V=2vs2 대칭 미리보기 토글 / " +
                      "Spot_DeliveryZone을 끌면 노란 마커가 실제 착지 위치");
        }

        private void Update()
        {
            MovePlayer();
            PaintBlocks();
            UpdateDelivery();

            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame) ToggleVersus();
        }

        // V: 2vs2 구성(더미 배경 + 180° 미러 복제 + 가운데 투명벽 + 팀 구역)을 Play 중에 켜고 끈다.
        private GameObject m_VersusRoot;
        private void ToggleVersus()
        {
            if (m_VersusRoot != null)
            {
                Destroy(m_VersusRoot);
                m_VersusRoot = null;
                Debug.Log("[Sandbox] 2vs2 미리보기 끔");
                return;
            }

            var zone = m_GridSize;
            var effective = new Vector3Int(zone.x * 2, zone.y, zone.z);

            m_VersusRoot = new GameObject("~SandboxVersus");
            var bg = VersusPreviewBuilder.BuildDummyBackground(zone);
            bg.transform.SetParent(m_VersusRoot.transform, true);

            var mirror = VersusBackground.CreateMirror(bg, VersusBackground.MirrorPivot(zone, effective));
            if (mirror != null) mirror.transform.SetParent(m_VersusRoot.transform, true);

            VersusPreviewBuilder.BuildOverlay(zone, effective).transform.SetParent(m_VersusRoot.transform, true);
            Debug.Log("[Sandbox] 2vs2 미리보기 켬 — 왼쪽(파랑)=팀A, 오른쪽(빨강)=팀B. 더미 구조물이 점대칭이어야 정상");
        }

        private void MovePlayer()
        {
            var kb = Keyboard.current;
            if (kb == null || m_Player == null) return;
            var d = new Vector3(
                (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), 0f,
                (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            if (d.sqrMagnitude > 0f) m_Player.position += d.normalized * (m_MoveSpeed * Time.deltaTime);
        }

        private void PaintBlocks()
        {
            foreach (var (go, cells) in m_Blocks)
            {
                bool inReach = GridReach.InReach(m_Player.position, cells, GridContract.Origin, GridContract.Unit, m_ReachCells);
                SetColor(go.gameObject, inReach ? kIn : kOut);
            }
        }

        private void UpdateDelivery()
        {
            if (m_DeliveryPoint == null || m_DeliveryMarker == null) return;
            var landed = MaterialDropField.ClampToFloorWorld(
                m_DeliveryPoint.position, m_GridSize, GridContract.Origin, GridContract.Unit, 60f);
            m_DeliveryMarker.position = new Vector3(landed.x, landed.y + 0.05f, landed.z);
        }

        // ── 헬퍼 ────────────────────────────────────────────────
        private void AddBlock(string name, int x0, int z0, int w, int d)
        {
            float u = GridContract.Unit;
            var center = GridContract.Origin + new Vector3((x0 + w * 0.5f) * u, 0.5f * u, (z0 + d * 0.5f) * u);
            var go = MakeBox(name, center, new Vector3(w * u, u, d * u), kOut);

            var cells = new List<Vector3Int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < d; z++)
                    cells.Add(new Vector3Int(x0 + x, 0, z0 + z));
            m_Blocks.Add((go.transform, cells));
        }

        private static GameObject MakeBox(string name, Vector3 pos, Vector3 scale, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            var c = go.GetComponent<Collider>(); if (c != null) Object.Destroy(c);
            SetColor(go, col);
            return go;
        }

        private static Material s_Mat;
        private static void SetColor(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (s_Mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh != null) s_Mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (s_Mat != null) r.sharedMaterial = s_Mat;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), c);
            mpb.SetColor(Shader.PropertyToID("_Color"), c);
            r.SetPropertyBlock(mpb);
        }
    }
}

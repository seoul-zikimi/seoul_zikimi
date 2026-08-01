using UnityEngine;
using UnityEngine.InputSystem;

namespace GridSystem
{
    /// <summary>
    /// 아이템 FX/사운드 단독 확인용(네트워크 불필요). ItemFxTest 씬에서 Play 후 숫자키로 재생.
    /// 1=등장 2=획득 3=발동 4=소멸 5=구슬 하나 놓기 0=구슬 전부 지우기 / 좌우 방향키=아이템 종류 변경.
    /// </summary>
    public class ItemFxTester : MonoBehaviour
    {
        [SerializeField] private Transform m_Anchor;   // FX 기준 위치(비면 자기 자신)

        private int m_Kind;   // CompetitiveItemKind 인덱스
        private readonly System.Collections.Generic.List<GameObject> m_Orbs = new();
        private static readonly SeoulZikimi.Gameplay.CompetitiveItemKind[] s_Kinds =
            (SeoulZikimi.Gameplay.CompetitiveItemKind[])System.Enum.GetValues(typeof(SeoulZikimi.Gameplay.CompetitiveItemKind));

        private Vector3 Pos => (m_Anchor != null ? m_Anchor : transform).position;
        private SeoulZikimi.Gameplay.CompetitiveItemKind Kind => s_Kinds[m_Kind];
        private Color Col => ItemNetwork.KindColor(Kind);

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.leftArrowKey.wasPressedThisFrame) m_Kind = (m_Kind - 1 + s_Kinds.Length) % s_Kinds.Length;
            if (kb.rightArrowKey.wasPressedThisFrame) m_Kind = (m_Kind + 1) % s_Kinds.Length;

            if (kb.digit1Key.wasPressedThisFrame) ItemFx.Spawned(Pos, Col);
            if (kb.digit2Key.wasPressedThisFrame) ItemFx.PickedUp(Pos, Col);
            if (kb.digit3Key.wasPressedThisFrame) ItemFx.Used(Pos, Col);
            if (kb.digit4Key.wasPressedThisFrame) ItemFx.Expired(Pos, Col);
            if (kb.digit5Key.wasPressedThisFrame) SpawnOrb();
            if (kb.digit0Key.wasPressedThisFrame) ClearOrbs();
        }

        private void SpawnOrb()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"~TestOrb({ItemNetwork.KindName(Kind)})";
            go.transform.position = Pos + new Vector3(Random.Range(-2f, 2f), 0.6f, Random.Range(-2f, 2f));
            go.transform.localScale = Vector3.one * 0.55f;
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null) go.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = Col };
            ItemFx.DecorateOrb(go, Col);
            ItemFx.Spawned(go.transform.position, Col);
            m_Orbs.Add(go);
        }

        private void ClearOrbs()
        {
            foreach (var o in m_Orbs)
                if (o != null) { ItemFx.Expired(o.transform.position, Col); Destroy(o); }
            m_Orbs.Clear();
        }

        // 조작 안내는 로그로(게임 UI는 프리팹 전용 규칙 — 테스트 씬도 OnGUI 안 씀)
        private void Start() =>
            Debug.Log("[ItemFxTest] 1=등장 2=획득 3=발동 4=소멸 5=구슬 0=지우기 / ←→ 종류 변경");

        // 종류 바뀔 때만 현재 선택을 로그로 알림
        private int m_LoggedKind = -1;
        private void LateUpdate()
        {
            if (m_LoggedKind == m_Kind) return;
            m_LoggedKind = m_Kind;
            Debug.Log($"[ItemFxTest] 선택: {ItemNetwork.KindName(Kind)}");
        }
    }
}

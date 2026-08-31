using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 밤 맵에서 원경(~Horizon)의 '언릿' 실루엣 카드를 밤색으로 눌러준다.
    ///
    /// MapVisualPolishTool의 실루엣 카드·나무는 URP Unlit이라 앰비언트를 안 받는다 —
    /// MapNightAmbience가 하늘·앰비언트를 밤으로 바꿔도 카드만 낮 하늘색으로 밝게 떠 버린다.
    /// 머티리얼 에셋은 전 맵이 공유하므로 에셋을 못 건드리고, 이 맵 인스턴스의
    /// 렌더러에만 인스턴스 머티리얼로 틴트를 곱한다(Lit 오브젝트는 앰비언트가 알아서 어두워진다).
    ///
    /// ~Horizon은 비주얼 정리 툴이 '나중에' 프리팹에 깔 수도 있어 이름으로 늦게 찾는다 — 없으면 조용히 무시.
    /// </summary>
    public class NightHorizonTint : MonoBehaviour
    {
        [Tooltip("원경 그룹 이름(이 프리팹 안). 비주얼 정리 툴(MapVisualPolishTool)이 만든다.")]
        public string HorizonName = "~Horizon";

        [Tooltip("언릿 카드에 곱할 밤색 — 안개색(MapNightAmbience.FogColor)과 비슷해야 지평선에 자연히 녹는다.")]
        public Color Tint = new Color(0.30f, 0.35f, 0.55f);

        private void Start()
        {
            var horizon = transform.Find(HorizonName);
            if (horizon == null) return;

            foreach (var r in horizon.GetComponentsInChildren<MeshRenderer>())
            {
                // ⚠ .materials — 인스턴스. 공유 에셋(다른 낮 맵들도 쓴다)을 더럽히면 안 된다.
                foreach (var mat in r.materials)
                {
                    if (mat == null || mat.shader == null) continue;
                    if (!mat.shader.name.Contains("Unlit")) continue;   // Lit은 앰비언트가 처리
                    if (!mat.HasProperty("_BaseColor")) continue;
                    var c = mat.GetColor("_BaseColor");
                    mat.SetColor("_BaseColor", new Color(c.r * Tint.r, c.g * Tint.g, c.b * Tint.b, c.a));
                }
            }
        }
    }
}

using Unity.Netcode;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 경복궁 기믹 공통 베이스 — NamsanGimmickBase와 같은 계약.
    /// 맵 카드(MapDef)에 GyeongbokgungGimmickConfig가 연결된 맵에서만 깨어나고, 아니면 잠잔다(Active=false).
    /// GameLoopManager·GridManager와 같은 오브젝트에 붙는다(GameLoopManager.Awake에서 AddComponent).
    /// </summary>
    public abstract class GyeongbokgungGimmickBase : NetworkBehaviour
    {
        protected GameLoopManager Loop { get; private set; }
        protected GridManager Grid { get; private set; }
        protected GridNetwork Net { get; private set; }
        protected GyeongbokgungGimmickConfig Config { get; private set; }

        /// <summary>이 맵에 경복궁 기믹이 켜져 있는가. false면 모든 로직이 잠잔다.</summary>
        public bool Active => Config != null;

        protected float Now => NetworkManager.ServerTime.TimeAsFloat;

        protected virtual void Awake()
        {
            Loop = GetComponent<GameLoopManager>();
            Grid = GetComponent<GridManager>();
            Net = GetComponent<GridNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            var cat = MapCatalog.Instance;
            var def = (cat != null && Loop != null) ? cat.Get(Loop.MapIndex) : null;
            Config = def != null ? def.GyeongbokgungGimmicks : null;
            Debug.Log($"[경복궁] {GetType().Name}: 맵 {Loop?.MapIndex}({def?.DisplayName}) → {(Active ? "기믹 켜짐" : "기믹 없음(잠자기)")}");
            if (Active) OnGimmickSpawn();
        }

        protected virtual void OnGimmickSpawn() { }

        // 배경 프리팹이 늦게 스폰되므로 0.5초 간격으로 재탐색(NamsanGimmickBase.FindSpot 패턴).
        private float m_NextFindAt;
        protected Transform FindMarker(string markerName)
        {
            if (Time.time < m_NextFindAt) return null;
            m_NextFindAt = Time.time + 0.5f;
            var go = GameObject.Find(markerName);
            return go != null ? go.transform : null;
        }
    }
}

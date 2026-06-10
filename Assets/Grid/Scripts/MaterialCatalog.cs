using System.Collections.Generic;
using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 게임의 모든 재료 정의 + Autotiles3D TileID ↔ MaterialId 매핑 테이블.
    /// (A)오서링(Autotiles3D 타일) 과 (B)런타임(MaterialId) 을 같은 재료로 묶는 연결고리.
    /// 직렬화는 List로, 런타임 조회는 Dictionary로 재구성한다(SoundLibrarySO 패턴).
    /// </summary>
    [CreateAssetMenu(fileName = "MaterialCatalog", menuName = "Grid/MaterialCatalog")]
    public class MaterialCatalog : ScriptableObject
    {
        public const int NoMaterial = -1;

        [Tooltip("이 게임의 모든 재료 정의")]
        [SerializeField] private List<MaterialDef> m_Materials = new();

        [System.Serializable]
        private struct TileIdMap
        {
            public int autotilesTileId;
            public int materialId;
        }

        [Tooltip("Autotiles3D Tile ID → MaterialId (정답 익스포트용)")]
        [SerializeField] private List<TileIdMap> m_TileIdMap = new();

        // 조회용 캐시 (직렬화 X, OnEnable에서 재구성)
        private Dictionary<int, MaterialDef> m_ById;
        private Dictionary<int, int> m_TileToMaterial;

        private void OnEnable() => RebuildLookup();

        /// <summary>인스펙터에서 목록/매핑을 바꾼 뒤 캐시를 다시 만든다.</summary>
        public void RebuildLookup()
        {
            m_ById = new Dictionary<int, MaterialDef>();
            foreach (var def in m_Materials)
            {
                if (def == null) continue;
                m_ById[def.Id] = def;
            }

            m_TileToMaterial = new Dictionary<int, int>();
            foreach (var m in m_TileIdMap)
                m_TileToMaterial[m.autotilesTileId] = m.materialId;
        }

        /// <summary>MaterialId로 정의 조회. 없으면 null.</summary>
        public MaterialDef GetById(int materialId)
        {
            if (m_ById == null) RebuildLookup();
            return m_ById.TryGetValue(materialId, out var def) ? def : null;
        }

        /// <summary>Autotiles3D TileID → MaterialId. 매핑 없으면 NoMaterial(-1).</summary>
        public int TileIdToMaterialId(int autotilesTileId)
        {
            if (m_TileToMaterial == null) RebuildLookup();
            return m_TileToMaterial.TryGetValue(autotilesTileId, out var id) ? id : NoMaterial;
        }
    }
}

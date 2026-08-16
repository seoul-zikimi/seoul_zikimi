using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 2vs2 배경 대칭 처리(런타임 MapLoader와 에디터 미리보기 공용).
    /// 전용 대칭 맵은 루트에 "VersusSymmetric" 빈 오브젝트를 두면 복제를 건너뛴다.
    /// </summary>
    public static class VersusBackground
    {
        public const string kSymmetricMarker = "VersusSymmetric";

        /// <summary>이미 대칭으로 제작된 전용 맵인가(복제 불필요).</summary>
        public static bool IsAuthoredSymmetric(GameObject bg)
            => bg != null && bg.transform.Find(kSymmetricMarker) != null;

        /// <summary>분할벽 중심(=팀 경계선의 그리드 중앙). 여기를 축으로 180° 회전 복제하면 점대칭이 된다.</summary>
        public static Vector3 MirrorPivot(Vector3Int zoneSize, Vector3Int effectiveSize)
        {
            float u = GridContract.Unit;
            Vector3 baseW = GridCoordinates.CellToWorld(Vector3Int.zero);
            return new Vector3(baseW.x + zoneSize.x * u, 0f, baseW.z + effectiveSize.z * 0.5f * u);
        }

        /// <summary>배경을 분할벽 기준 180° 회전 복제(비주얼 전용 — 콜라이더·스크립트 제거, 마커 이름 무력화).</summary>
        public static GameObject CreateMirror(GameObject bg, Vector3 pivot)
        {
            if (bg == null) return null;
            var clone = Object.Instantiate(bg);
            clone.name = $"~VersusMirror({bg.name})";
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var s in clone.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(s);
            // 복제본의 마커 이름을 죽인다 — 안 그러면 GameObject.Find("Spot_DeliveryZone") 등이 반대편 사본을 잡는다.
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Spot_")) t.name = "~Mirror_" + t.name;
            clone.transform.RotateAround(pivot, Vector3.up, 180f);
            return clone;
        }
    }
}

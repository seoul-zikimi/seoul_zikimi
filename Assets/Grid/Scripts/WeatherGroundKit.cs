using UnityEngine;

namespace GridSystem
{
    /// <summary>
    /// 바닥 날씨 연출(물웅덩이·눈 쌓임·눈 자국·낙엽·꽃잎)에 쓰는 메쉬/머티리얼 묶음.
    /// 에디터의 Weather3DAssetBuilder가 생성하며, 런타임은 Resources에서 읽기만 한다.
    /// </summary>
    public sealed class WeatherGroundKit : ScriptableObject
    {
        [Header("Meshes")]
        public Mesh Disc;
        public Mesh Quad;

        [Header("Materials")]
        public Material Puddle;
        public Material SnowPatch;
        public Material SnowTrail;
        public Material[] Leaves;
        public Material Petal;

        [Header("Counts (base, 2vs2는 자동 2배)")]
        // QA: 바닥 데칼이 너무 많아 웅덩이·눈 쌓임을 약 65%로 줄임(14→9, 22→14, 22→14).
        public int PuddleCount = 9;
        public int TyphoonPuddleCount = 14;
        public int SnowPatchCount = 14;
        public int LeafCount = 70;
        public int PetalCount = 80;

        [Header("Sizes (min, max)")]
        public Vector2 PuddleSize = new Vector2(0.9f, 1.8f);
        public Vector2 SnowPatchSize = new Vector2(1.6f, 3.2f);
        public Vector2 LeafSize = new Vector2(0.17f, 0.28f);
        public Vector2 PetalSize = new Vector2(0.10f, 0.17f);

        [Header("Snow trail")]
        [Tooltip("플레이어가 이만큼 움직일 때마다 자국 하나")]
        public float TrailStride = 0.45f;
        public Vector2 TrailSize = new Vector2(0.42f, 0.62f);
        public float TrailLifetime = 14f;
        public float TrailFadeDuration = 4f;
        public int TrailMax = 160;

        [Header("Snowman (겨울 눈 날씨 한정)")]
        public GameObject Snowman;
        public int SnowmanCount = 3;
        public Vector2 SnowmanScale = new Vector2(0.8f, 1.15f);

        [Header("Petal atlas (2x2)")]
        public int PetalTilesX = 2;
        public int PetalTilesY = 2;
    }
}

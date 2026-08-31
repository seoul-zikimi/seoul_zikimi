using UnityEngine;

namespace GridSystem
{
    /// <summary>맵 배경 프리팹에 붙는 노을 분위기 전환기(공터 대결장 전용).
    ///
    /// 하늘(FastSky)은 전 맵이 전역으로 공유하므로, 이 맵이 스폰될 때만
    /// 스카이박스·안개·앰비언트·태양(방향광)을 노을 톤으로 바꾸고
    /// 파괴될 때(맵 언로드) 원래 값으로 되돌린다. 다른 맵엔 영향 없음.</summary>
    public sealed class SunsetAmbience : MonoBehaviour
    {
        [SerializeField] private Material m_SunsetSky;   // 노을용 스카이박스(없으면 하늘은 유지)

        // 원복용 스냅샷
        private Material m_PrevSky;
        private Color m_PrevFog;
        private Color m_PrevAmbSky, m_PrevAmbEq, m_PrevAmbGround;
        private Light m_Sun;
        private Color m_PrevSunColor;
        private Quaternion m_PrevSunRot;
        private bool m_Applied;

        private void OnEnable()
        {
            m_PrevSky = RenderSettings.skybox;
            m_PrevFog = RenderSettings.fogColor;
            m_PrevAmbSky = RenderSettings.ambientSkyColor;
            m_PrevAmbEq = RenderSettings.ambientEquatorColor;
            m_PrevAmbGround = RenderSettings.ambientGroundColor;

            if (m_SunsetSky != null) RenderSettings.skybox = m_SunsetSky;
            RenderSettings.fogColor = new Color(0.99f, 0.72f, 0.52f);          // 지평선 노을빛(원경이 안개에 자연스럽게 녹게)
            RenderSettings.ambientSkyColor = new Color(0.95f, 0.62f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.74f, 0.50f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.24f, 0.22f);

            m_Sun = RenderSettings.sun;
            if (m_Sun != null)
            {
                m_PrevSunColor = m_Sun.color;
                m_PrevSunRot = m_Sun.transform.rotation;
                m_Sun.color = new Color(1.00f, 0.66f, 0.38f);                  // 낮은 주황 태양
                m_Sun.transform.rotation = Quaternion.Euler(14f, 250f, 0f);    // 서쪽 저녁 해 각도
            }
            m_Applied = true;
        }

        private void OnDisable()
        {
            if (!m_Applied) return;
            RenderSettings.skybox = m_PrevSky;
            RenderSettings.fogColor = m_PrevFog;
            RenderSettings.ambientSkyColor = m_PrevAmbSky;
            RenderSettings.ambientEquatorColor = m_PrevAmbEq;
            RenderSettings.ambientGroundColor = m_PrevAmbGround;
            if (m_Sun != null)
            {
                m_Sun.color = m_PrevSunColor;
                m_Sun.transform.rotation = m_PrevSunRot;
            }
            m_Applied = false;
        }
    }
}

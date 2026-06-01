using System.Collections.Generic;
using UnityEngine;

namespace Player.Test
{
    public class PlayerTestUI : MonoBehaviour
    {
        [SerializeField] private ConcretePlayerFactory m_Factory;
        [SerializeField] private float m_SpawnRadius = 5f;
        [SerializeField] private int m_SpawnCount = 4;
        [SerializeField] private float m_GatherSpeed       = 5f;
        [SerializeField] private float m_SprintGatherSpeed = 9f;  // SprintSpeed 기본값과 맞춤

        private List<PlayerUnit> m_SpawnedPlayers = new();

        public void OnSpawnClicked()
        {
            for (int i = 0; i < m_SpawnCount; i++)
            {
                float angle=i*(360f/m_SpawnCount)*Mathf.Deg2Rad;
                Vector3 pos = new(Mathf.Cos(angle) * m_SpawnRadius, 0, Mathf.Sin(angle) * m_SpawnRadius);
                m_SpawnedPlayers.Add(m_Factory.GetProduct(pos) as PlayerUnit);
            }
        }

        public void OnGatherClicked()
        {
            foreach (var u in m_SpawnedPlayers)
            {
                if (u == null) continue;
                
                GatherDriver driver = u.gameObject.AddComponent<GatherDriver>();
                driver.Begin(Vector3.zero, m_GatherSpeed);
            }
        }
        
        public void OnSprintGatherClicked()
        {
            foreach (var u in m_SpawnedPlayers)
            {
                if (u == null) continue;
                GatherDriver driver = u.gameObject.AddComponent<GatherDriver>();
                driver.Begin(Vector3.zero, m_SprintGatherSpeed);
            }
        }

        public void OnClearClicked()
        {
            foreach (var u in m_SpawnedPlayers)
                if (u != null) Destroy(u.gameObject);
            m_SpawnedPlayers.Clear();
        }
    }
}
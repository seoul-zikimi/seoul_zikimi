using UnityEngine;

namespace Player
{
    public class ConcretePlayerFactory : PlayerFactory
    {
        [SerializeField] private PlayerUnit m_PlayerPrefab;
        [SerializeField] private PlayerConfigSO m_Config;

        public override IPlayerProduct GetProduct(Vector3 position)
        {
            PlayerUnit unit = Instantiate(m_PlayerPrefab, position, Quaternion.identity);
            unit.Initialize(m_Config);
            return unit;
        }
    }
}
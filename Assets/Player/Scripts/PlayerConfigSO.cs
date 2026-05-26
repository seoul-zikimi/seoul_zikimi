using UnityEngine;

namespace Player
{
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Player/PlayerConfig")]
    public class PlayerConfigSO  : ScriptableObject
    {
        [Header("Movement")] public float MoveSpeed = 5f;

        [Header("Bounce")]
        public float ReboundForce   = 8f;
        public float BounceHeight   = 1.5f;
        public float BounceDuration = 0.4f;

        [Header("Dust Trail")]
        public float DustSize = 1.5f;
    }
}
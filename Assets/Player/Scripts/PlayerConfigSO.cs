using UnityEngine;

namespace Player
{
    public enum BounceEffectType { None, HitDYellow, HitMiscA, HitMiscFSmoke, Boing }
   

    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Player/PlayerConfig")]
    public class PlayerConfigSO : ScriptableObject
    {
        [Header("Movement")]
        public float MoveSpeed   = 5f;
        public float SprintSpeed = 9f;   // Shift 달리기 속도

        [Header("Bounce")]
        public float ReboundForce   = 8f;
        public float BounceHeight   = 1.5f;
        public float BounceDuration = 0.4f;

        [Header("Bounce Effect")]
        public BounceEffectType BounceEffect         = BounceEffectType.HitDYellow;
        public float            BounceEffectDuration = 2f;
        public GameObject BounceEffectHitDYellow;
        public GameObject BounceEffectHitMiscA;
        public GameObject BounceEffectHitMiscFSmoke;
        public GameObject BounceEffectBoing;

        public GameObject GetBounceEffectPrefab() => BounceEffect switch
        {
            BounceEffectType.HitDYellow    => BounceEffectHitDYellow,
            BounceEffectType.HitMiscA      => BounceEffectHitMiscA,
            BounceEffectType.HitMiscFSmoke => BounceEffectHitMiscFSmoke,
            BounceEffectType.Boing         => BounceEffectBoing,
            _                              => null
        };

        [Header("Dust Trail")]
        public float DustSize = 1.5f;

        [Header("Sprint Trail")]
        public float SprintCoreWidth = 0.12f;
        public float SprintGlowWidth = 0.45f;
        public float SprintTrailTime = 0.30f;

       
    }
}

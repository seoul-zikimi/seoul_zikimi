using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// A demo component using the AlignWithVelocity and LookAt components to simulate projectiles
    /// that start aligned with the velocity but then lerp into looking right at the target.
    /// </summary>
    [RequireComponent(typeof(AlignWithVelocity))]
    [RequireComponent(typeof(LookAt))]
    public class AlignLookLerp : MonoBehaviour
    {
        protected IProjectile _projectile;
        public IProjectile Projectile
        {
            get
            {
                if (_projectile == null)
                {
                    TryGetComponent<IProjectile>(out _projectile);
                }
                return _projectile;
            }
        }

        protected AlignWithVelocity _alignWithVelocity;
        public AlignWithVelocity AlignWithVelocity
        {
            get
            {
                if (_alignWithVelocity == null)
                {
                    TryGetComponent<AlignWithVelocity>(out _alignWithVelocity);
                }
                return _alignWithVelocity;
            }
        }

        protected LookAt _lookAt;
        public LookAt LookAt
        {
            get
            {
                if (_lookAt == null)
                {
                    TryGetComponent<LookAt>(out _lookAt);
                }
                return _lookAt;
            }
        }

        public float StartTime = 0.5f;
        public float Duration = 1.0f;
        public bool Active = true;

        public void Update()
        {
            if (!Active)
                return;

            float t = Mathf.Clamp01(Projectile.Time - StartTime / Duration);
            if (AlignWithVelocity.HasResult && AlignWithVelocity.Active && LookAt.HasResult && LookAt.Active)
            {
                AlignWithVelocity.Target.transform.rotation = Quaternion.Lerp(AlignWithVelocity.Result, LookAt.Result, t);
            }
        }
    }
}
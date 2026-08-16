using System.Collections.Generic;
using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Keeps track of all spawned projectiles. Use this to get a list of your projectiles.<br />
    /// This class is for convenience only. It is not strcitly necessary for the system to work.
    /// </summary>
    [HelpURL("https://kamgam.com/unity/HitMeManual.pdf")]
    public partial class ProjectileRegistry
    {
        #region Singleton
        protected bool _destroyed = false;

        static ProjectileRegistry _instance;
        public static ProjectileRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProjectileRegistry();
                }

                return _instance;
            }
        }
        #endregion

        public List<AnimationProjectile> AnimationProjectiles = new List<AnimationProjectile>();
        public List<BallisticProjectile> BallisticProjectiles = new List<BallisticProjectile>();

        public void RegisterProjectile(AnimationProjectile projectile)
        {
            if (!AnimationProjectiles.Contains(projectile))
            {
                AnimationProjectiles.Add(projectile);
            }
        }

        public void UnregisterProjectile(AnimationProjectile projectile)
        {
            if (AnimationProjectiles.Contains(projectile))
                AnimationProjectiles.Remove(projectile);
        }

        public void RegisterProjectile(BallisticProjectile projectile)
        {
            if (BallisticProjectiles.Contains(projectile))
            {
                BallisticProjectiles.Add(projectile);
            }
        }

        public void UnregisterProjectile(BallisticProjectile projectile)
        {
            if (BallisticProjectiles.Contains(projectile))
                BallisticProjectiles.Remove(projectile);
        }

        public void ExecuteOnAll(System.Action<IProjectile> func)
        {
            int count = AnimationProjectiles.Count - 1;
            for (int i = count; i >= 0; i--)
            {
                func.Invoke(AnimationProjectiles[i]);
            }

            count = BallisticProjectiles.Count - 1;
            for (int i = count; i >= 0; i--)
            {
                func.Invoke(BallisticProjectiles[i]);
            }
        }
    }
}
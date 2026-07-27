using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class BallisticSimpleDemo3D : MonoBehaviour
    {
        public Transform Source;
        public Transform Target;

        public GameObject ProjectilePrefab;
        public BallisticProjectileConfig Config;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            var projectile = BallisticProjectile.Spawn(ProjectilePrefab, Source, Target, Config);
            
            if (projectile == null)
            {
                Debug.Log("It is impossible to hit the target with the current configuration. That's why the projetile is null.");
            }
        }
    }
}
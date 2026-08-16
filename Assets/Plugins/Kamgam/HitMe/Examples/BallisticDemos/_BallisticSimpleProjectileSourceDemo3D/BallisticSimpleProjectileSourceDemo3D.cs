using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class BallisticSimpleProjectileSourceDemo3D : MonoBehaviour
    {
        public BallisticProjectileSource ProjectileSource;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            ProjectileSource.Spawn();
        }
    }
}
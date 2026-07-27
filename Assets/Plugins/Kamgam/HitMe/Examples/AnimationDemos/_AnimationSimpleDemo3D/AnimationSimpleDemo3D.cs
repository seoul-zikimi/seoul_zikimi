using System.Collections;
using UnityEngine;

namespace Kamgam.HitMe
{
    public class AnimationSimpleDemo3D : MonoBehaviour
    {
        public Transform Source;
        public Transform Target;

        public GameObject ProjectilePrefab;
        public AnimationProjectileConfig Config;

        public IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);

            AnimationProjectile.Spawn(ProjectilePrefab, Source, Target, Config);
        }
    }
}
using System.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;

namespace Kamgam.HitMe
{
    public partial class ProjectileEvents : MonoBehaviour
    {
        /// <summary>
        /// The age of the projectile evetns object since spawning in seconds.
        /// </summary>
        public float Age { get; private set; } = 0f;

        protected bool _destroyed = false;

        /*
#pragma warning disable CS0067
        public event System.Action<Projectile> OnHit;
        public UnityEvent<Projectile> OnHitEvent;

        public delegate void OnUpdateDelegate(Projectile projectile, float progress);
        public event OnUpdateDelegate OnUpdate;
        public UnityEvent<Projectile, float> OnUpdateEvent;

        // OnDestroy
#pragma warning restore CS0067
        */

        public void Start()
        {
            
        }

        public void Update()
        {
            if (_destroyed)
                return;

            Age += Time.deltaTime;
        }

        protected int _collisionCounter = 0;

        public void OnCollisionEnter(Collision collision)
        {
            if (_destroyed)
                return;

            _collisionCounter++;
        }

        public void OnCollisionEnter2D(Collision2D collision)
        {
            if (_destroyed)
                return;

            _collisionCounter++;
        }

        public void Reset()
        {
            Age = 0f;
            _collisionCounter = 0;
            _destroyed = false;
        }

        public void OnDestroy()
        {
            _destroyed = true;
        }
    }
}
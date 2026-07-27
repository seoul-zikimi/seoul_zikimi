using System;
using UnityEngine;

namespace Kamgam.HitMe
{
    [AddComponentMenu("Hit Me/Align/Align To Ballistic Source Direction")]
    public class AlignToBallisticSourceDirection : MonoBehaviour
    {
        public BallisticProjectileSource ProjectileSource;

        /// <summary>
        /// Invert the direction?
        /// </summary>
        [Tooltip("Invert the direction?")]
        public bool Invert = false;

        /// <summary>
        /// Leave empty to align self.
        /// </summary>
        [Tooltip("Leave empty to align self.")]
        public Transform AlignmentTarget = null;

        public Vector3 WorldUpAxis = Vector3.up;

        public bool CheckConstraints = true;

        public void Update()
        {
            // Evaluate the config etc.
            bool possible = ProjectileSource.Evaluate(out var startVelocity, out _, CheckConstraints);
            if (!possible)
                return;
            
            // Make the target look at the start velocity direction.
            var dir = Invert ? -startVelocity : startVelocity;
            var target = AlignmentTarget != null ? AlignmentTarget : this.transform;
            target.LookAt(target.position + dir, WorldUpAxis);
        }
    }
}
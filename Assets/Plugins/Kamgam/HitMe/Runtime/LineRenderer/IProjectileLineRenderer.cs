using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// Implement this interface  in your custom LineRenderer so you can use it for drawing projectile paths.
    /// </summary>
    public interface IProjectileLineRenderer
    {
        void SetPosition(int index, Vector3 position);
        int positionCount { get; set; }
    }
}
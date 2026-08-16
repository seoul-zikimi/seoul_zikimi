using UnityEngine;

namespace Kamgam.HitMe
{
    /// <summary>
    /// If enabled it will control the TARGET OFFSET of teh source (calculate the offset based on the given angle).
    /// It's completely optional and disabled by default in the demo.
    /// I Just added it because some users asked about it.
    /// </summary>
    [ExecuteAlways]
    public class TankDemoAngleToDirection : MonoBehaviour
    {
        public BallisticDemo3D Spawner;
        public bool Active = true;
        
        protected float _angle = 0f;
        public float Angle = 0f;

        public void Update()
        {
            if (Active && Spawner != null)
                Spawner.Config.TargetOffset = Quaternion.Euler(Angle, 0f, 0f) * Vector3.forward;
        }
    }
}
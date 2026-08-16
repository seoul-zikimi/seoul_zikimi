using UnityEngine;
using UnityEngine.Serialization;

namespace Kamgam.HitMe
{
    public class TankDemoConstraintAngle2D : MonoBehaviour, IBallisticProjectileSpawnConstraint
    {
        [Header("Constrain Up/Down")]
        public float MinAngleXToTarget = 0f;
        public float MaxAngleXToTarget = 45f;
        
        [Header("Constrain Left/Right")]
        public float MinAngleYToTarget = -30f;
        public float MaxAngleYToTarget = 30f;
        
        protected BallisticDemo3D _ballisticDemo3D;
        public BallisticDemo3D BallisticDemo3D
        {
            get
            {
                if (_ballisticDemo3D == null)
                {
                    _ballisticDemo3D = this.GetComponent<BallisticDemo3D>();
                }

                return _ballisticDemo3D;
            }
        }
        
        public BallisticProjectileSpawnConstraintAngle2D AngleConstraint;

        public void OnEnable()
        {
            BallisticDemo3D.SpawnConstraint = this;
        }

        public bool Allow(Vector3 startVelocity, float angle2D, BallisticProjectileConfig config,
            Transform source, Transform target,
            Vector3? sourcePos, Vector3? targetPos,
            IMovementPredictor predictor)
        {
            // Here we check if the source is pointing towards the target.
            var dir = source.InverseTransformDirection(target.position - source.position).normalized;
            var dirZY = new Vector2(dir.z, dir.y);
            var dirZX = new Vector2(dir.z, dir.x);
            
            var angleX = Vector2.SignedAngle(new Vector2(1f, 0f), dirZY);
            var angleY = Vector2.SignedAngle(new Vector2(1f, 0f), dirZX);

            if (angleX < MinAngleXToTarget || angleX > MaxAngleXToTarget)
                return false;
            
            if (angleY < MinAngleYToTarget || angleY > MaxAngleYToTarget)
                return false;
            
            // And finally here is a default constraint on the angle2D in addition to the look direction.
            return AngleConstraint.Allow(startVelocity, angle2D, config, source, target, sourcePos, targetPos,predictor);
        }
    }
}
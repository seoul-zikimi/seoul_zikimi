using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    public enum BallisticCalculation
    {
        Time = 0,
        Altitude = 1,
        Angle = 2,
        Speed = 3,
        LowestEnergy = 4,
        
        // Fully defined
        DirectionAndSpeed = 5
    }

    public enum PhysicsDimensions
    {
        Physics3D = 0,
        Physics2D = 1
    }

    public enum AltitudeBase
    {
        World = 0,
        Source = 1,
        Target = 2,
        Highest = 3,
        Lowest = 4
    }

    public static class BallisticUtils
    {
        public static Vector3 GetDefaultGravity(PhysicsDimensions dimensions)
        {
            return dimensions == PhysicsDimensions.Physics3D ? Physics.gravity : Physics2D.gravity;
        }

        public static float GetDefaultGravityMagnitude(PhysicsDimensions dimensions)
        {
            return GetDefaultGravity(dimensions).magnitude;
        }

        public static float ResolveGravity(Vector3? gravity, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            return gravity.HasValue ? -gravity.Value.magnitude : -GetDefaultGravityMagnitude(dimensions);
        }

        /// <summary>
        /// An attempt to counteract the physics simulation error compare to the mathematical formula.<br /><br />
        /// CONTEXT: The physics simulation is an approximation of the mathematical result and thus differs even after a short time.
        /// The smaller the Time.fixedDeltaTime is the more accurate it will be.<br /><br />
        /// This method given you an offset position for ballistics based on the start velocity. Use this as the delta to the default 
        /// start poistion (0/0/0) to counteract the simulation divergence. This is NOT a logical solution. It is based on tests done in
        /// Unity 2021 through Unity 2023. More: https://forum.unity.com/threads/physics-prediction-slightly-off-compared-to-physics-simulation.1450759/#post-9094963
        /// </summary>
        /// <param name="startVelocity"></param>
        /// <returns></returns>
        public static Vector3 CalcStartPositionDelta(Vector3 startVelocity, Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            // Start at the position at half a physics update.
            float dT = Time.fixedDeltaTime * 0.5f;
            float g = ResolveGravity(gravity, dimensions);

            return new Vector3(
                0f,
                dT * startVelocity.y + g * dT * dT * 0.5f,
                dT * startVelocity.z
            );
        }

        public static float CalcDuration(float angle2D, Vector3 startVelocity, Vector3 path, Vector3 gravity)
        {
            return CalcDuration(angle2D, startVelocity.magnitude, path.y, new Vector2(path.x, path.z).magnitude, gravity.magnitude);
        }

        public static float CalcMaxDistance(Vector3 startVelocity, Vector3 gravity)
        {
            float angle2D = GetAngle2D(startVelocity);
            float speed = startVelocity.magnitude;
            float g = gravity.magnitude;
            return CalcMaxDistance(angle2D, speed, g);
        }

        public static float GetAngle2D(Vector3 startVelocity)
        {
            float angle2D;
            if (Mathf.Approximately(startVelocity.x, 0f) && Mathf.Approximately(startVelocity.z, 0f))
                angle2D = 90f;
            else
                angle2D = Vector3.Angle(new Vector3(startVelocity.x, 0f, startVelocity.z), startVelocity);

            return angle2D;
        }

        public static float CalcMaxDistance(float angle2D, Vector3 startVelocity, Vector3 gravity)
        {
            float speed = startVelocity.magnitude;
            float g = gravity.magnitude;
            return CalcMaxDistance(angle2D, speed, g);
        }

        public static float CalcMaxDistance(float angle2D, float speed, float gravity)
        {
            return speed * speed * Mathf.Sin(2f * Mathf.Deg2Rad * angle2D) / gravity;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="angle2D">Angle in degrees.</param>
        /// <param name="startVelocity"></param>
        /// <param name="deltaY"></param>
        /// <param name="distanceXZ">The distance is required to choose one of the two possible solutions. NOTICE: This has to be the distance in the XZ plane.</param>
        /// <param name="gravity"></param>
        /// <returns>May return float.NaN</returns>
        public static float CalcDuration(float angle2D, Vector3 startVelocity, float deltaY, float distanceXZ, Vector3 gravity)
        {
            return CalcDuration(angle2D, startVelocity.magnitude, deltaY, distanceXZ, gravity.magnitude);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="angle2D">Angle in degrees.</param>
        /// <param name="speed"></param>
        /// <param name="deltaY"></param>
        /// <param name="distanceXZ">The distance is required to choose one of the two possible solutions. NOTICE: This has to be the distance in the XZ plane.</param>
        /// <param name="gravity"></param>
        /// <returns>May return float.NaN if not possible.</returns>
        public static float CalcDuration(float angle2D, float speed, float deltaY, float distanceXZ, float gravity)
        {
            var tmp0 = speed * Mathf.Sin(angle2D * Mathf.Deg2Rad);
            var sqrValue = tmp0 * tmp0 - 2 * deltaY * gravity;

            // Check if possible
            if (sqrValue < 0f)
            {
                return float.NaN;
            }

            // Two possible solutions
            float solutionA = (tmp0 + Mathf.Sqrt(sqrValue)) / gravity;
            float solutionB = (tmp0 - Mathf.Sqrt(sqrValue)) / gravity;

            if (solutionA < 0f)
                return solutionB;

            if (solutionB < 0f)
                return solutionA;

            // Pick the solution that is closest to the given distance.
            float c = speed * Mathf.Cos(angle2D * Mathf.Deg2Rad);
            float distanceADelta = Mathf.Abs(c * solutionA - distanceXZ);
            float distanceBDelta = Mathf.Abs(c * solutionB - distanceXZ);

            if (distanceADelta < distanceBDelta)
                return solutionA;
            else
                return solutionB;
        }

        public static float CalcMaxHeight(float angle2D, float speed, float gravity)
        {
            return Mathf.Pow(speed * Mathf.Sin(angle2D * Mathf.Deg2Rad), 2) / (2 * gravity);
        }

        /// <summary>
        /// Returns the start velocity Vector3 needed to reach the target in the given time.<br />
        /// One of the advantages of the time based solution is that it always has a single solution.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="time"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns the start velocity Vector3 needed to reach the target in the given time.</returns>
        public static Vector3 CalcStartVelocityByTime(
            Vector3 source, Vector3 target,
            float time,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            return CalcStartVelocityByTime(out _, source, target, time, gravity, dimensions);
        }

        /// <summary>
        /// Returns the start velocity Vector3 needed to reach the target in the given time.<br />
        /// One of the advantages of the time based solution is that it always has a single solution.
        /// </summary>
        /// <param name="angleIn2D">The shooting angle in 2D in degrees.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="time"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns the start velocity Vector3 needed to reach the target in the given time.</returns>
        public static Vector3 CalcStartVelocityByTime(
            out float angleIn2D,
            Vector3 source, Vector3 target,
            float time,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            // Short cut if zero or below
            if (time < 0.0001f)
            {
                angleIn2D = 0f;
                return target - source * 10000f; // leads to ultra high velocity.
            }

            var path = target - source;
            if (Mathf.Approximately(path.sqrMagnitude, 0f))
            {
                angleIn2D = 0f;
                return Vector3.zero;
            }

            var distanceXZ = new Vector3(path.x, 0f, path.z); // Project onto xz plane
            float distance = distanceXZ.magnitude;
            float altitude = path.y;

            // Solve in 2D (YZ plane)
            //  Derived from equation for motion under constant acceleration.
            //  var velocity = distanceVector / time - new Vector3(0, g, 0) * 0.5f * time;
            float g = ResolveGravity(gravity, dimensions);
            var velocity = new Vector3(
                0f
                , altitude / time - g * time * 0.5f
                , distance / time
            );

            // Rotate back to 3D and return
            float angleY = Vector3.SignedAngle(distanceXZ, Vector3.forward, Vector3.up);
            var finalVelocity = Quaternion.Euler(0f, -angleY, 0f) * velocity;

            angleIn2D = Mathf.Asin(velocity.normalized.y) * Mathf.Rad2Deg; // True as long as velocity x = 0
            return finalVelocity;
        }

        /// <summary>
        /// Returns false if the target can not be reached.
        /// </summary>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="altitude"></param>
        /// <param name="altitudeBase"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByAltitude(
            out Vector3 startVelocity,
            Vector3 source, Vector3 target,
            float altitude, AltitudeBase altitudeBase = AltitudeBase.Source,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D,
            bool allowLowerAltitudes = true)
        {
            return CalcStartVelocityByAltitude(
                out startVelocity,
                out _,
                source, target,
                altitude, altitudeBase,
                gravity, dimensions,
                allowLowerAltitudes
            );
        }

        /// <summary>
        /// Returns false if the target can not be reached.
        /// </summary>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="angleIn2D">The shooting angle in 2D in degrees.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="altitude"></param>
        /// <param name="altitudeBase"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByAltitude(
            out Vector3 startVelocity,
            out float angleIn2D,
            Vector3 source, Vector3 target,
            float altitude, AltitudeBase altitudeBase = AltitudeBase.Source,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D,
            bool allowLowerAltitudes = true)
        {
            var distanceVector = target - source;
            if (Mathf.Approximately(distanceVector.sqrMagnitude, 0f))
            {
                startVelocity = Vector3.zero;
                angleIn2D = 0f;
                return false;
            }

            var distanceXZ = new Vector3(distanceVector.x, 0f, distanceVector.z); // Project onto xz plane
            float distanceInXZ = distanceXZ.magnitude;

            // Convert the altitude to always be relative to source.y
            altitude = CalcDeltaAltitude(source, target, altitude, altitudeBase);

            // Allow lowering the altitude
            if (allowLowerAltitudes && altitude < 0.01f)
            {
                altitude = 0.01f;
            }

            // Stop if the altitude is lower than needed to reach the target (that's just impossible).
            if (altitude < 0.001f || altitude < distanceVector.y)
            {
                startVelocity = Vector3.zero;
                angleIn2D = 0f;
                return false;
            }

            // Solve in 2D (YZ plane)
            float g = ResolveGravity(gravity, dimensions);
            float angle;
            if (Mathf.Approximately(distanceVector.y, 0f))
            {
                // Shortcut solution if the delta y is zero.
                angle = Mathf.Atan(4 * altitude / distanceInXZ);
            }
            else
            {
                // Derived from the "If the projectile's position (x,y) and launch angle are known ..." formula from wikipedia:
                // https://en.wikipedia.org/wiki/Projectile_motion
                // TODO: find a more optimized solution.
                var h = altitude;
                var x = distanceInXZ;
                var y = distanceVector.y;
                angle = Mathf.Atan((2 * (Mathf.Sqrt(h * h * x * x - h * x * x * y) + h * x)) / (x * x));
            }
            var sqrValue = 2f * altitude * -g / (Mathf.Sin(angle) * Mathf.Sin(angle));
            var speed = Mathf.Sqrt(sqrValue);

            // Rotate back to 3D and return
            angle *= Mathf.Rad2Deg;
            angleIn2D = angle;

            float angleY = Vector3.SignedAngle(distanceXZ, Vector3.forward, Vector3.up);
            startVelocity = Quaternion.Euler(-angle, -angleY, 0f) * new Vector3(0f, 0f, speed);
            return true;
        }

        public static float CalcDeltaAltitude(
            Vector3 source, Vector3 target,
            float altitude, AltitudeBase altitudeBase = AltitudeBase.Source)
        {
            // Convert the altitude to always be relative to source.y
            switch (altitudeBase)
            {
                case AltitudeBase.Source:
                    // altitude = altitude; // No change needed. It's already relative to source.y
                    return altitude;

                case AltitudeBase.Target:
                    return target.y + altitude - source.y;

                case AltitudeBase.Highest:
                    return Mathf.Max(altitude, target.y + altitude - source.y);
                    
                case AltitudeBase.Lowest:
                    return Mathf.Min(altitude, target.y + altitude - source.y);

                case AltitudeBase.World:
                default:
                    return altitude - source.y;
            }
        }

        /// <summary>
        /// Returns false if the target can not be reached.
        /// </summary>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="angle"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByAngle(
            out Vector3 startVelocity,
            Vector3 source, Vector3 target, float angle,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            return CalcStartVelocityByAngle(
                out startVelocity, out _,
                source, target, angle,
                gravity, dimensions
            );
        }

        /// <summary>
        /// Returns false if the target can not be reached.
        /// </summary>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="angleIn2D">The shooting angle in 2D in degrees.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="angle"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByAngle(
            out Vector3 startVelocity,
            out float angle2D,
            Vector3 source, Vector3 target, float angle,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            var path = target - source;

            if (Mathf.Approximately(path.sqrMagnitude, 0f))
            {
                startVelocity = Vector3.zero;
                angle2D = 0f;
                return true;
            }

            var distanceXZ = new Vector3(path.x, 0f, path.z);  // Project onto xz plane
            float distance = distanceXZ.magnitude;
            float altitude = path.y;

            // Solve in 2D (YZ plane)
            float g = ResolveGravity(gravity, dimensions);
            var cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            var value = distance * distance * -g /
                        (distance * Mathf.Sin(2f * angle * Mathf.Deg2Rad) - 2 * altitude * cos * cos);
            // If value < 0f then the target can not be reached with the given angle.
            if (value < 0)
            {
                startVelocity = Vector3.zero;
                angle2D = 0f;
                return false;
            }
            var speed = Mathf.Sqrt(value);

            // Rotate back to 3D and return
            float angleY = Vector3.SignedAngle(distanceXZ, Vector3.forward, Vector3.up);
            startVelocity = Quaternion.Euler(-angle, -angleY, 0f) * new Vector3(0f, 0f, speed);

            angle2D = angle;
            return true;
        }

        /// <summary>
        /// Returns false if the target can not be reached.<br />
        /// This has two possible solutions. One with a high altitude (useHighSolution = true) and one with a low altitude (useHighSolution = false).
        /// </summary>
        /// <param name="startVelocity"></param>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="speed"></param>
        /// <param name="useHighSolution"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityBySpeed(
            out Vector3 startVelocity,
            Vector3 source, Vector3 target, float speed, bool useHighSolution = false,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            return CalcStartVelocityBySpeed(
                out startVelocity, out _,
                source, target, speed, useHighSolution,
                gravity, dimensions
            );
        }

        /// <summary>
        /// Returns false if the target can not be reached.<br />
        /// This has two possible solutions. One with a high altitude (useHighSolution = true) and one with a low altitude (useHighSolution = false).
        /// </summary>
        /// <param name="startVelocity"></param>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="angleIn2D">The shooting angle in 2D in degrees.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="speed"></param>
        /// <param name="useHighSolution"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityBySpeed(
            out Vector3 startVelocity,
            out float angle2D,
            Vector3 source, Vector3 target, float speed, bool useHighSolution = false,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            var path = target - source;

            if (Mathf.Approximately(path.sqrMagnitude, 0f))
            {
                startVelocity = Vector3.zero;
                angle2D = 0f;
                return true;
            }

            var distanceXZ = new Vector3(path.x, 0f, path.z);  // Project onto xz plane
            float distance = distanceXZ.magnitude;

            // Solve in 2D (ZY plane)
            var sqrSpeed = Mathf.Pow(speed, 2);
            var qadSpeed = Mathf.Pow(speed, 4);
            var x = distance;
            var y = path.y;
            float g = ResolveGravity(gravity, dimensions);
            var sqrValue = qadSpeed - g * (g * x * x + 2 * -y * sqrSpeed); // Not sure why -y (wikipedia says +y but hey, it works).

            // If value < 0f then the target can not be reached with the given angle.
            if (sqrValue < 0f)
            {
                startVelocity = Vector3.zero;
                angle2D = 0f;
                return false;
            }

            // Has two solutions, see: https://en.wikipedia.org/wiki/Projectile_motion
            var angleA = Mathf.Atan((sqrSpeed + Mathf.Sqrt(sqrValue)) / (g * x)) * Mathf.Rad2Deg;
            var angleB = Mathf.Atan((sqrSpeed - Mathf.Sqrt(sqrValue)) / (g * x)) * Mathf.Rad2Deg;
            float angle = useHighSolution ? angleA : angleB;

            // Rotate back to 3D and return
            float angleY = Vector3.SignedAngle(distanceXZ, Vector3.forward, Vector3.up);
            startVelocity = Quaternion.Euler(angle, -angleY, 0f) * new Vector3(0f, 0f, speed);

            angle2D = -angle;
            return true;
        }

        /// <summary>
        /// Returns false if the target can not be reached.<br />
        /// Finds the angle with the lowest needed start speed and uses that.
        /// </summary>
        /// <param name="velocity"></param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByMinEnergy(
            out Vector3 startVelocity,
            Vector3 source, Vector3 target,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            return CalcStartVelocityByMinEnergy(
                out startVelocity,
                out _,
                source, target,
                gravity, dimensions
            );
        }

        /// <summary>
        /// Returns false if the target can not be reached.<br />
        /// Finds the angle with the lowest needed start speed and uses that.
        /// </summary>
        /// <param name="startVelocity">The start velocity vector in 3D.</param>
        /// <param name="angleIn2D">The shooting angle in 2D in degrees.</param>
        /// <param name="source"></param>
        /// <param name="target"></param>
        /// <param name="gravity">Override for gravity. If your gravity does not point straight down you will have to correct the results for teh rotation.</param>
        /// <param name="dimensions">Physics mode which defines where to take the default gravity value from if not specified.</param>
        /// <returns>Returns false if the target can not be reached.</returns>
        public static bool CalcStartVelocityByMinEnergy(
            out Vector3 startVelocity,
            out float angleIn2D,
            Vector3 source, Vector3 target,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D
            )
        {
            // Wikipedia: The launch should be at the angle halfway between the target and Zenith (vector opposite to Gravity)
            // See: https://en.wikipedia.org/wiki/Projectile_motion
            var path = target - source;

            if (Mathf.Approximately(path.sqrMagnitude, 0f))
            {
                startVelocity = Vector3.zero;
                angleIn2D = 0f;
                return true;
            }

            Vector3 g = gravity.HasValue ? gravity.Value : (dimensions == PhysicsDimensions.Physics3D ? Physics.gravity : Physics2D.gravity);
            var angle = Vector3.Angle(path, g) / 2f;
            bool possible = CalcStartVelocityByAngle(out startVelocity, source, target, angle);
            if (!possible)
            {
                startVelocity = Vector3.zero;
                angleIn2D = 0f;
                return false;
            }

            angleIn2D = angle;
            return true;
        }

        public static Vector3 CalcPositionByStartVelocity(
            out Vector3 velocityAtPos,
            Vector3 startVelocity, float time,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            var posA = CalcPositionByStartVelocity(startVelocity, time, gravity, dimensions);
            var posB = CalcPositionByStartVelocity(startVelocity, time + 0.02f, gravity, dimensions);
            velocityAtPos = (posB - posA) / 0.02f;
            return posA;
        }

        public static Vector3 CalcPositionByStartVelocity(
            Vector3 startVelocity, float time,
            Vector3? gravity = null, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            float speed = startVelocity.magnitude;
            Vector3 g = gravity.HasValue ? gravity.Value : (dimensions == PhysicsDimensions.Physics3D ? Physics.gravity : Physics2D.gravity);

            // Bring angle from 3D to 2D
            float angle = Mathf.Max(0f, Vector3.Angle(g, startVelocity) - 90f);

            // Calc displacement
            float x = time * speed * Mathf.Cos(angle * Mathf.Deg2Rad);
            float y = time * speed * Mathf.Sin(angle * Mathf.Deg2Rad) - g.magnitude * time * time / 2f;

            // Convert back from 2D to 3D
            Vector3 posX = Vector3.RotateTowards(g, startVelocity, 90f * Mathf.Deg2Rad, 0f).normalized * x;
            Vector3 posY = -g.normalized * y;

            return posX + posY;
        }

        public static bool CalcStartVelocity(
            out Vector3 startVelocity, out float angle2D,
            in BallisticProjectileConfig config,
            IMovementPredictor predictor = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true
            )
        {
            if (predictor != null)
            {
                return PredictStartVelocity(out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, source, target, positionsContainOffset);
            }
            else
            {
                return CalcStartVelocity(out startVelocity, out angle2D, config, sourcePos, targetPos, source, target, positionsContainOffset);
            }
        }

        public static bool CalcStartVelocity(out Vector3 startVelocity, out float angle2D, in BallisticProjectileConfig config)
        {
            var sourcePosition = config.GetSourcePosWithOffset();
            var targetPosition = config.GetTargetPosWithOffset();
            
            return CalcStartVelocity(out startVelocity, out angle2D, config, sourcePosition, targetPosition);
        }

        public static bool CalcStartVelocity(
            out Vector3 startVelocity, out float angle2D, 
            in BallisticProjectileConfig config,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true)
        {
            var gravity = config.GetGravity();

            config.ApplyToPositions(ref sourcePos, ref targetPos, source, target, positionsContainOffset, applyOffsets: true);

            switch (config.CalculationMethod)
            {
                case BallisticCalculation.Altitude:
                    return CalcStartVelocityByAltitude(out startVelocity, out angle2D, sourcePos.Value, targetPos.Value, config.Altitude, config.AltitudeBase, gravity, config.Dimensions, config.AllowLowerAltitudes);

                case BallisticCalculation.Angle:
                    return CalcStartVelocityByAngle(out startVelocity, out angle2D, sourcePos.Value, targetPos.Value, config.Angle, gravity, config.Dimensions);

                case BallisticCalculation.Speed:
                    return CalcStartVelocityBySpeed(out startVelocity, out angle2D, sourcePos.Value, targetPos.Value, config.Speed, config.SpeedUseHighSolution, gravity, config.Dimensions);

                case BallisticCalculation.LowestEnergy:
                    return CalcStartVelocityByMinEnergy(out startVelocity, out angle2D, sourcePos.Value, targetPos.Value, gravity, config.Dimensions);

                case BallisticCalculation.DirectionAndSpeed:
                    // Use one of the other methods to calc the 2D angle.
                    CalcStartVelocityByAngle(out _, out angle2D, sourcePos.Value, targetPos.Value, config.Angle, gravity, config.Dimensions);
                    // Set start velocity based on direction. For direction we use the path from source to target.
                    var direction = (targetPos.Value - sourcePos.Value).normalized;
                    startVelocity = direction * config.Speed;
                    return true;
                
                default:
                case BallisticCalculation.Time:
                    startVelocity = CalcStartVelocityByTime(out angle2D, sourcePos.Value, targetPos.Value, config.Duration, gravity, config.Dimensions);
                    return true;
            }
        }

        public static bool PredictStartVelocity(
            out Vector3 startVelocity, out float angle2D, 
            in BallisticProjectileConfig config, 
            IMovementPredictor predictor,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true
            )
        {
            bool possible;

            angle2D = 0f;
            startVelocity = Vector3.zero;

            Assert.IsNotNull(config);

            config.ApplyToPositions(ref sourcePos, ref targetPos, source, target, positionsContainOffset, applyOffsets: true);

            // Calc duration based on config.
            float predictionDeltaTime = 0f;
            if (config.CalculationMethod == BallisticCalculation.Time)
            {
                // For the Time method it is know by default since the duration is the input paramter.
                predictionDeltaTime = config.Duration;
                var predictedPosition = predictor.PredictPosition(predictionDeltaTime) + config.GetTargetOffsetInWorldSpace();
                possible = CalcStartVelocity(
                    out startVelocity, out angle2D,
                    config, sourcePos, predictedPosition, null, null, positionsContainOffset: true
                    );
            }
            else
            {
                // The input parameter needed for the predictor is delta time.
                // 
                // We have to estimate the duration for every ballistic method (except Time).
                // The "predictor.PredictPosition(predictionDeltaTime)" is a black box (interface)
                // so we do not know if the prediction made in there is based on a mathematical formula
                // or a simulation.
                //
                // We do know for the concrete implementation in MovementPredictor.cs that it could
                // be converted to a math formula (circles and lines) and thus we could, if the formula is
                // solved for t, use that here.
                //
                // However, future users may replace the predictor with other code so the safe approach
                // is to iterate over the prediction a couple of times until it has converged enough
                // to give good results (see predictionIterations below)
                //
                // This is only necessary for methods that have diverging results for the flight duration based
                // on the distance. That's also why we do not have to do this for the BallisticCalculation.Time
                // method as there the flight time is constant (it's the input).
                //
                // In (target pos) > Out (duration to target pos) > Predict pos from initial target pos(duration) > new target pos -\
                //       ^__________________________________________________________________________________________________________/
                //
                // This loop means it converges at the correct duration.
                //
                // Note to future self: There is no easy fix. I hope this was understandable ;)

                int predictionIterations = 1;
                // These need more iterations.
                if (config.CalculationMethod == BallisticCalculation.Altitude)
                    predictionIterations = 3;
                if (config.CalculationMethod == BallisticCalculation.Angle)
                    predictionIterations = 6;
                if (config.CalculationMethod == BallisticCalculation.Speed)
                    predictionIterations = 5;
                if (config.CalculationMethod == BallisticCalculation.LowestEnergy)
                    predictionIterations = 5;

                predictionIterations = Mathf.Max(1, predictionIterations * config.PredictionIterationMultiplier);

                var sourceToTarget = targetPos.Value - sourcePos.Value;
                var isPossible = true;
                var iteratedTargetPosition = targetPos.Value;
                for (int i = 0; i < predictionIterations; i++)
                {
                    // Only update the target position with the prediction IF it was possible to hit the original position.
                    // TODO: It may still happen that the new predicted position can not be hit (out of range).
                    if (isPossible)
                    {
                        isPossible = CalcStartVelocity(out startVelocity, out angle2D, config, sourcePos.Value, iteratedTargetPosition, null, null, positionsContainOffset: true);
                        // Stop if no longer possible (we can no longer advance the duration as it is not possible to hit that target position).
                        // This becomes noticable if we cross the edge from possible to impossible (the projectile may land too short as it only
                        // shoots as far as it can).
                        if (!isPossible)
                            break;
                        predictionDeltaTime = CalcDuration(angle2D, startVelocity, sourceToTarget, config.GetGravity());
                        if (predictionDeltaTime > 0f)
                        {
                            // Update target position to something closer to the final target pos and then try again.
                            iteratedTargetPosition = predictor.PredictPosition(predictionDeltaTime) + config.GetTargetOffsetInWorldSpace();
                        }
                    }
                }
                possible = isPossible;
            }

            return possible;
        }

        /// <summary>
        /// Applies the given start velocity to the rigidbody 2D or 3D.
        /// </summary>
        /// <param name="startVelocity"></param>
        /// <param name="go"></param>
        /// <param name="sourcePosition"></param>
        /// <param name="compensateSimulation"></param>
        /// <param name="dimensions">Selects whether a Rigidbody or a Rigidbody2D is used.</param>
        public static void ApplyStartVelocity(Vector3 startVelocity, GameObject go, Vector3 sourcePosition, bool compensateSimulation = true, PhysicsDimensions dimensions = PhysicsDimensions.Physics3D)
        {
            if (compensateSimulation)
                go.transform.position = sourcePosition + CalcStartPositionDelta(startVelocity);
            else
                go.transform.position = sourcePosition;

            if (dimensions == PhysicsDimensions.Physics3D)
            {
                // 3D
                go.TryGetComponent<Rigidbody>(out var rb);
                if (rb != null)
                {
                    rb.AddForce(startVelocity - rb.GetVelocity(), ForceMode.VelocityChange);
                }
            }
            else
            {
                // 2D
                go.TryGetComponent<Rigidbody2D>(out var rb);
                if (rb != null)
                {
#if UNITY_6000_0_OR_NEWER
                    rb.linearVelocity = new Vector2(startVelocity.x, startVelocity.y);
#else
                    rb.velocity = new Vector2(startVelocity.x, startVelocity.y);
#endif
                }
            }
        }
    }
}
using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    public partial class BallisticProjectile
    {
        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="possible">OUT parameter that tells you whether or not reaching the target is possible with the given parameters.</param>
        /// <param name="source">This game objects position will be used instead of config.Soure.</param>
        /// <param name="target">This game objects position will be used instead of config.Target.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">(Optional) How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            out bool possible,
            GameObject source, GameObject target,
            BallisticProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            bool usePrediction = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath(
                out possible,
                source == null ? null : source.transform,
                target == null ? null : target.transform,
                config,
                segmentsPerUnit,
                renderer,
                prefab,
                usePrediction,
                minTime, maxTime);
        }

        internal static T DrawPathGeneric<T>(
            out bool possible,
            Transform source, Transform target,
            BallisticProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            bool usePrediction = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath(out possible, source, target, config, segmentsPerUnit, renderer, prefab, usePrediction, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="possible">OUT parameter that tells you whether or not reaching the target is possible with the given parameters.</param>
        /// <param name="source">This transforms position will be used instead of config.Soure.</param>
        /// <param name="target">This transforms position will be used instead of config.Target.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">(Optional) How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            out bool possible,
            Transform source, Transform target,
            BallisticProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            bool usePrediction = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            IMovementPredictor predictor = null;
            if (usePrediction)
            {
                var resolvedTarget = BallisticProjectileConfig.ResolveTarget(config, target);
                if (resolvedTarget != null)
                    predictor = resolvedTarget.gameObject.GetComponent<IMovementPredictor>();
            }

            return DrawPath(out possible, config, segmentsPerUnit, renderer, prefab, predictor, sourcePos: null, targetPos: null, source, target, positionsContainOffset: false, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="possible">OUT parameter that tells you whether or not reaching the target is possible with the given parameters.</param>
        /// <param name="sourcePos">This position will be used instead of the config.SourcePosition.</param>
        /// <param name="targetPos">This position will be used instead of the config.TargetPosition.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">(Optional) How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="predictor">(Optional) If not null then this predictor will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            out bool possible,
            Vector3 sourcePos, Vector3 targetPos,
            BallisticProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            IMovementPredictor predictor = null,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath(out possible, config, segmentsPerUnit, renderer, prefab, predictor, sourcePos, targetPos, source: null, target: null, positionsContainOffset: true, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="possible">OUT parameter that tells you whether or not reaching the target is possible with the given parameters.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">(Optional) How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="predictor">(Optional) If not null then this predictor will be used.</param>
        /// <param name="sourcePos">(Optional) If not null then this position will be used instead of the config.SourePosition.</param>
        /// <param name="targetPos">(Optional) If not null then this position will be used instead of the config.TargetPosition.</param>
        /// <param name="source">(Optional) If not null then this transforms position will be used instead of the sourcePos and config.SourePosition.</param>
        /// <param name="target">(Optional) If not null then this transforms position will be used instead of the targetPos and config.TargetPosition.</param>
        /// <param name="positionsContainOffset">(Optional) Specifies if the sourcePos and targetPos parameters already include offsets. If false then the offsets from the config are added to sourcePos and targetPos.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            out bool possible,
            BallisticProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            IMovementPredictor predictor = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            if (config == null)
                Assert.IsNotNull(config);

            // Resolve source / target transforms
            source = BallisticProjectileConfig.ResolveSource(config, source);
            target = BallisticProjectileConfig.ResolveTarget(config, target);

            // Resolve source / target positions
            config.ApplyToPositions(ref sourcePos, ref targetPos, source, target, positionsContainOffset, applyOffsets: true);

            // Calc start velocity, abort if impossible
            Vector3 startVelocity;
            float angle2D;
            possible = BallisticUtils.CalcStartVelocity(out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, null, null, positionsContainOffset: true);
            if (!possible)
                return renderer;   

            return drawForStartVelocity(startVelocity, angle2D, config.Dimensions, config.GetGravity(), segmentsPerUnit, renderer, prefab, sourcePos.Value, targetPos.Value, minTime, maxTime);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="startVelocity"></param>
        /// <param name="dimensions"></param>
        /// <param name="gravity"></param>
        /// <param name="segmentsPerUnit">(Optional) How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="sourcePos">(Optional) If not null then this position will be used instead of the config.SourePosition.</param>
        /// <param name="targetPos">(Optional) If not null then this position will be used instead of the config.TargetPosition.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns></returns>
        public static T DrawForStartVelocity<T>(
            Vector3 startVelocity, PhysicsDimensions dimensions, Vector3 gravity, float segmentsPerUnit, T renderer,
            GameObject prefab, Vector3 sourcePos, Vector3 targetPos,
            float minTime, float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            float angle2D;
            if (Mathf.Approximately(startVelocity.x, 0f) && Mathf.Approximately(startVelocity.z, 0f))
                angle2D = 90f;
            else
                angle2D = Vector3.Angle(new Vector3(startVelocity.x, 0f, startVelocity.z), startVelocity);
            
            return drawForStartVelocity(startVelocity, angle2D, dimensions, gravity, segmentsPerUnit, renderer, prefab, sourcePos, targetPos, minTime, maxTime);
        }

        public static T drawForStartVelocity<T>(
            Vector3 startVelocity,
            float angle2D,
            PhysicsDimensions dimensions,
            Vector3 gravity,
            float segmentsPerUnit,
            T renderer,
            GameObject prefab,
            Vector3 sourcePos,
            Vector3 targetPos,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            Vector3 path = targetPos - sourcePos;
            float duration = BallisticUtils.CalcDuration(angle2D, startVelocity, path, gravity);

            // Abort if not possible.
            if (float.IsNaN(duration))
            {
                return renderer;
            }

            // Some shenanigans to make the number of line vertices adapt to the length and curvature of the path.
            var midPoint = BallisticUtils.CalcPositionByStartVelocity(startVelocity, duration * 0.5f, gravity, dimensions);
            var midDisplacement = (midPoint - path * 0.5f).magnitude;
            float distance = path.magnitude;
            float arcLength = distance + midDisplacement * 2f; // <- a very crude approximation of the parabola length.
            // If the ratio between the distance and the height gets very small then increase then double the vertices.
            var ratio = distance / midDisplacement;
            if (ratio < 0.4f)
            {
                arcLength *= 2f;
            }
            else if (ratio > 2f)
            {
                arcLength /= ratio * 0.5f;
            }

            // Round steps to int
            int steps = Mathf.RoundToInt(arcLength * segmentsPerUnit);
            steps = Mathf.Max(2, steps);

            // If maxT is not defined then assume it is meant to be equal to duration.
            if (float.IsInfinity(maxTime))
                maxTime = duration;

            float stepDuration = (maxTime - minTime) / steps;

            if (renderer == null)
            {
                GameObject obj;
                if (prefab != null)
                {
                    obj = GameObject.Instantiate(prefab);
                }
                else
                {
                    obj = new GameObject("ProjectileLineRenderer", typeof(T));
                }
                renderer = obj.GetComponent<T>();
            }

            renderer.positionCount = steps + 1;

            float t;
            Vector3 posT;
            for (int i = 0; i <= steps; i++)
            {
                t = minTime + i * stepDuration;
                posT = sourcePos + BallisticUtils.CalcPositionByStartVelocity(startVelocity, t, gravity, dimensions);
                renderer.SetPosition(i, posT);
            }

            return renderer;
        }
    }
}
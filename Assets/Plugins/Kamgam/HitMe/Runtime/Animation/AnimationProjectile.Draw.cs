using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    public partial class AnimationProjectile
    {
        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="source">This game objects position will be used instead of config.Soure.</param>
        /// <param name="target">This game objects position will be used instead of config.Target.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            GameObject source, GameObject target,
            AnimationProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            bool usePrediction = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath<T>(
                source == null ? null : source.transform,
                target == null ? null : target.transform,
                config, segmentsPerUnit, renderer, prefab, usePrediction, minTime, maxTime);
        }

        internal static T DrawPathGeneric<T>(
            Transform source, Transform target,
            AnimationProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            bool usePrediction = true,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity)
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath<T>(source, target, config, segmentsPerUnit, renderer, prefab, usePrediction, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="source">This transforms position will be used instead of config.Soure.</param>
        /// <param name="target">This transforms position will be used instead of config.Target.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            Transform source, Transform target,
            AnimationProjectileConfig config,
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
            if (usePrediction && (config == null || !config.FollowTarget))
            {
                var resolvedTarget = AnimationProjectileConfig.ResolveTarget(config, target);
                if(resolvedTarget != null)
                    predictor = target.gameObject.GetComponent<IMovementPredictor>();
            }

            return DrawPath<T>(config, segmentsPerUnit, renderer, prefab, predictor, null, null, source, target, positionsContainOffset: false, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="sourcePos">This position will be used instead of the config.SourePosition.</param>
        /// <param name="targetPos">This position will be used instead of the config.TargetPosition.</param>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
        /// <param name="renderer">(Optional) If null then a new line renderer game object will be created. Otherwise the given one will be altered.</param>
        /// <param name="prefab">(Optional) If not null then this prefab will be used to create a new line renderer.</param>
        /// <param name="predictor">(Optional) If not null then this predictor will be used.</param>
        /// <param name="minTime">(Optional) Start drawing at the time (in seconds) along the path.</param>
        /// <param name="maxTime">(Optional) End drawing at the time (in seconds) along the path.</param>
        /// <returns>An IProjectileLineRenderer instance of type T.</returns>
        public static T DrawPath<T>(
            Vector3 sourcePos, Vector3 targetPos,
            AnimationProjectileConfig config,
            float segmentsPerUnit = 1f,
            T renderer = null,
            GameObject prefab = null,
            IMovementPredictor predictor = null,
            float minTime = 0f,
            float maxTime = float.PositiveInfinity
            )
            where T : MonoBehaviour, IProjectileLineRenderer
        {
            return DrawPath<T>(
                config, segmentsPerUnit, renderer, prefab, predictor, sourcePos, targetPos,
                source: null, target: null, positionsContainOffset: true, minTime, maxTime);
        }

        /// <summary>
        /// Sets points on a line renderer based on the inputs.
        /// </summary>
        /// <typeparam name="T">The type of line renderer you want to use. Usually ProjectileLineRenderer.</typeparam>
        /// <param name="config">The config based on which the path will be calculated.</param>
        /// <param name="segmentsPerUnit">How many segments the path should have per world unit. Curvier paths may need more segments. 1 is a good default value.</param>
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
            AnimationProjectileConfig config,
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
            source = AnimationProjectileConfig.ResolveSource(config, source);
            target = AnimationProjectileConfig.ResolveTarget(config, target);

            // Resolve source / target positions
            config.ApplyToPositions(ref sourcePos, ref targetPos, source, target, positionsContainOffset, applyOffsets: true);

            // Evaluate starting conditions.
            float startAngle = config.StartAngle.Evaluate();
            // var duration = project.GetDurationBasedOnApproximatedDistanceAndSpeed(DistanceApproximationSteps);
            // TODO: Replace "config.Duration" below with a proper approximation or else the prediction will always be based on the DURATION.
            //       Which works if duration and prediction is used (without follow target) but fails is SPEED is used instead of duration.
            //       See other use of PredictPosition (projectile.Prepare()). Sadly here do not yet have a projectile so we would need to change
            //       the API to a static one that can take the config and emulate a prepared projectile. See mail report from 14-09-2025
            Vector3? predictedPos = predictor == null ? null : predictor.PredictPosition(config.Duration);

            // Some shenanegans to make the number of line vertices adapt to the length and curvature of the path.
            // Estimate the path length & create an axis aligned bonding box around the curve.
            int steps;
            {
                float length = 0f;
                if (config.UseCurves)
                {
                    // Sample curve at some points to estimate the length
                    int lengthEstimationSteps = 4;
                    float lengthEstimationStepDuration = config.Duration / (lengthEstimationSteps - 1);
                    Vector3 posA, posB;
                    posA = AnimationProjectile.Evaluate(config, 0f, startAngle, predictedPos, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing: true);

                    for (int i = 1; i < lengthEstimationSteps; i++)
                    {
                        // Sample curve
                        float t1 = i * lengthEstimationStepDuration;
                        posB = AnimationProjectile.Evaluate(config, t1, startAngle, predictedPos, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing: true);

                        length += (posB - posA).magnitude;
                        posA = posB;
                    }
                }
                else
                {
                    length = (targetPos.Value - sourcePos.Value).magnitude;
                }

                // Round steps to int
                steps = Mathf.RoundToInt(length * segmentsPerUnit);
                steps = Mathf.Max(2, steps);

                // Increase density for short distances.
                float sqrDistance = (targetPos.Value - sourcePos.Value).sqrMagnitude;
                if (config.UseCurves)
                {
                    if (sqrDistance < 36f) // 6f
                        steps *= 2;
                    if (sqrDistance > 2500f) // 50f
                        steps /= 2;
                }
            }

            // If maxT is not defined then assume it is meant to be equal to duration.
            if (float.IsInfinity(maxTime))
                maxTime = config.Duration;

            float duration = maxTime - minTime;
            float stepDuration = duration / steps;

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
                obj.TryGetComponent<T>(out renderer);
            }

            renderer.positionCount = steps + 1;

            float t = minTime;
            Vector3 posT;
            // First step
            posT = AnimationProjectile.Evaluate(config, t, startAngle, out _, out _, predictedPos, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing: true); ;
            renderer.SetPosition(0, posT);
            // In between step
            for (int i = 1; i < steps; i++)
            {
                t = minTime + i * stepDuration;
                posT = AnimationProjectile.Evaluate(config, t, startAngle, out _, out _, predictedPos, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing: true);
                renderer.SetPosition(i, posT);
            }
            // Last step
            posT = AnimationProjectile.Evaluate(config, minTime + duration, startAngle, out _, out _, predictedPos, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing: true);
            renderer.SetPosition(steps, posT);

            return renderer;
        }
    }
}
using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    public partial class AnimationProjectile
    {
        /// <summary>
        /// Spawns a new projectile based on the given prefab.<br />
        /// If there is no Projectile component on the prefab then it will add one.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="source">This game objects position will be used instead of config.Soure.</param>
        /// <param name="target">This game objects position will be used instead of config.Target.</param>
        /// <param name="config">If null then the config of the Projectile component of the prefab will be used. Use this to override the config at spawn time.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="startActive">Whether or not the projectile object should be activated automatically. NOTICE: If you want to register to the OnDisablePhysics event you will have to do so BEFORE activation.</param>
        /// <returns></returns>
        public static AnimationProjectile Spawn(GameObject prefab, GameObject source, GameObject target, AnimationProjectileConfig config = null, bool usePrediction = true, bool startActive = true)
        {
            return Spawn(prefab, source.transform, target.transform, config, usePrediction, startActive);
        }

        /// <summary>
        /// Spawns a new projectile based on the given prefab.<br />
        /// If there is no Projectile component on the prefab then it will add one.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="source">This transforms position will be used instead of config.Soure.</param>
        /// <param name="target">This transforms position will be used instead of config.Target.</param>
        /// <param name="config">If null then the config of the Projectile component of the prefab will be used. Use this to override the config at spawn time.</param>
        /// <param name="usePrediction">Whether or not to use a predictor. The predictor will be searched for on 'target'. If none is found the none will be used.</param>
        /// <param name="startActive">Whether or not the projectile object should be activated automatically. NOTICE: If you want to register to the OnDisablePhysics event you will have to do so BEFORE activation.</param>
        /// <returns></returns>
        public static AnimationProjectile Spawn(GameObject prefab, Transform source, Transform target, AnimationProjectileConfig config = null, bool usePrediction = true, bool startActive = true)
        {
            source = AnimationProjectileConfig.ResolveSource(config, source);
            target = AnimationProjectileConfig.ResolveTarget(config, target);

            var sourcePos = source != null ? source.position : (config != null ? config.SourcePosition : Vector3.zero);
            var targetPos = target != null ? target.position : (config != null ? config.TargetPosition : Vector3.one);

            IMovementPredictor predictor = null;
            if (usePrediction && (config == null || !config.FollowTarget))
            {
                var resolvedTarget = AnimationProjectileConfig.ResolveTarget(config, target);
                if (resolvedTarget != null)
                    target.gameObject.TryGetComponent<IMovementPredictor>(out predictor);
            }

            var projectile = Spawn(prefab, sourcePos, targetPos, config, source, target, predictor, startActive);
            return projectile;
        }

        /// <summary>
        /// Spawns a new projectile based on the given prefab.<br />
        /// If there is no Projectile component on the prefab then it will add one.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="sourcePos">Tthis position will be used instead of the config.SourePosition.</param>
        /// <param name="targetPos">Tthis position will be used instead of the config.TargetPosition.</param>
        /// <param name="config">If null then the config of the Projectile component of the prefab will be used. Use this to override the config at spawn time.</param>
        /// <param name="predictor">If set then this will override sourcePos and targetPos.</param>
        /// <param name="startActive">Whether or not the projectile object should be activated automatically. NOTICE: If you want to register to the OnDisablePhysics event you will have to do so BEFORE activation.</param>
        /// <returns></returns>
        public static AnimationProjectile Spawn(GameObject prefab, Vector3 sourcePos, Vector3 targetPos, AnimationProjectileConfig config = null, IMovementPredictor predictor = null, bool startActive = true)
        {
            var projectile = Spawn(prefab, sourcePos, targetPos, config, null, null, predictor, startActive);
            return projectile;
        }

        /// <summary>
        /// Spawns a new projectile based on the given prefab.<br />
        /// If there is no Projectile component on the prefab then it will add one.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="sourcePos">Tthis position will be used instead of the config.SourePosition.</param>
        /// <param name="targetPos">Tthis position will be used instead of the config.TargetPosition.</param>
        /// <param name="config">If null then the config of the Projectile component of the prefab will be used. Use this to override the config at spawn time.</param>
        /// <param name="source">Will set the Source in the config if not null. Will override sourcePos if not null.</param>
        /// <param name="target">Will set the Target in the config if not null. Will override targetPos if not null.</param>
        /// <param name="predictor">If set then this will override sourcePos and targetPos.</param>
        /// <param name="startActive">Whether or not the projectile object should be activated automatically. NOTICE: If you want to register to the OnDisablePhysics event you will have to do so BEFORE activation.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception"></exception>
        public static AnimationProjectile Spawn(
            GameObject prefab, Vector3 sourcePos, Vector3 targetPos,
            AnimationProjectileConfig config = null,
            Transform source = null, Transform target = null,
            IMovementPredictor predictor = null,
            bool startActive = true)
        {
            if (prefab == null)
                Assert.IsNotNull(prefab);

            source = AnimationProjectileConfig.ResolveSource(config, source);
            target = AnimationProjectileConfig.ResolveTarget(config, target);

            if (config != null)
            {
                config = config.Copy();
                config.UpdateSourceAndTargetPos(sourcePos, targetPos, source, target);
            }

            var spawnPos = source != null ? source.transform.position : sourcePos;
            if (config != null)
            {
                spawnPos = config.GetSourcePosWithOffset();
            }

            GameObject go = null;
            bool wasActive = prefab.activeSelf;
            try
            {
                prefab.SetActive(false);
                go = Instantiate(prefab, spawnPos, Quaternion.identity, null);
            }
            finally
            {
                prefab.SetActive(wasActive);
            }
            go.TryGetComponent<AnimationProjectile>(out var projectile);

            if (projectile == null && config == null)
                throw new System.Exception("No Projectile component found on prefab '" + prefab.name + "' and config is NULL. Please use a prefab with a Projectile component OR set a config.");

            if (projectile == null)
                projectile = go.AddComponent<AnimationProjectile>();

            // Copy config
            if (config != null)
            {
                projectile.ConfigAsset = null;
                projectile.Config = config;
            }
            else
            {
                // If the config comes from an asset then the .Config getter will automatically create a new instance (config copy).
                // If the config is part of the prefab then it already is a copy.
                config = projectile.Config;
                config.UpdateSourceAndTargetPos(sourcePos, targetPos, source, target);
            }

            // It's important to prepare the projectiles curve data to get accurate prediction durations. 
            projectile.Prepare();
            
            // Either we use the predictor and then animate towards that position OR enable the follow target.
            // Using both makes no sense as they do contradict each other.
            if (predictor != null && !config.FollowTarget)
            {
                // Calc duration based on config.
                var duration = projectile.GetDurationBasedOnApproximatedDistanceAndSpeed(DistanceApproximationSteps);
                projectile._predictedPosition = predictor.PredictPosition(duration);
            }
            
            if (startActive)
            {
                projectile.SetActive(true);
            }

            return projectile;
        }
    }
}
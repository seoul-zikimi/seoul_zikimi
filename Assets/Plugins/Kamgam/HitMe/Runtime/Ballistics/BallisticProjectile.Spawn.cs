using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace Kamgam.HitMe
{
    public partial class BallisticProjectile
    {
        protected Vector3? _startVelocity = null;
        protected bool _compensateSimulation;

        /// <summary>
        /// Returns the start velocity if the target can be reached. Returns null otherwise.
        /// </summary>
        /// <returns></returns>
        public Vector3? GetStartVelocity()
        {
            return _startVelocity;
        }


        // STATIC API
        
        public static BallisticProjectile Spawn(
            GameObject prefab, GameObject source, GameObject target, 
            BallisticProjectileConfig config = null, bool compensateSimulation = true, bool usePrediction = true, bool startActive = true,
            IBallisticProjectileSpawnConstraint spawnConstraint = null)
        {
            return Spawn(prefab, source.transform, target.transform, config, compensateSimulation, usePrediction, startActive, spawnConstraint);
        }

        public static BallisticProjectile Spawn(
            GameObject prefab, Transform source, Transform target, 
            BallisticProjectileConfig config = null, bool compensateSimulation = true, bool usePrediction = true, bool startActive = true,
            IBallisticProjectileSpawnConstraint spawnConstraint = null)
        {
            IMovementPredictor predictor = null;
            if (usePrediction)
            {
                var resolvedTarget = BallisticProjectileConfig.ResolveTarget(config, target);
                if (resolvedTarget != null)
                    resolvedTarget.gameObject.TryGetComponent<IMovementPredictor>(out predictor);
            }

            var projectile = Spawn(out _, out _, out _, prefab, config, compensateSimulation, predictor, null, null, source, target, positionsContainOffset: false, startActive, spawnConstraint);
            
            return projectile;
        }

        public static BallisticProjectile Spawn(
            GameObject prefab, Vector3 sourcePos, Vector3 targetPos,
            BallisticProjectileConfig config = null, bool compensateSimulation = true, IMovementPredictor predictor = null, bool positionsContainOffset = true, bool startActive = true,
            IBallisticProjectileSpawnConstraint spawnConstraint = null)
        {
            var projectile = Spawn(out _, out _, out _, prefab, config, compensateSimulation, predictor, sourcePos, targetPos, null, null, positionsContainOffset, startActive, spawnConstraint = null);
            return projectile;
        }

        /// <summary>
        /// Instantiates the given prefab and spawns it with the start velocity.<br />
        /// If there is no Projectile component on the prefab then it will add one.
        /// </summary>
        /// <param name="prefab">The prefab game object used for spawning the projectile. The prefab may (or may not) have a BallisticProjectile component. If it has none then one will be added.</param>
        /// <param name="config">If null then the config of the Projectile component on the prefab will be used. Use this to override the config at spawn time. It is recommended to set this if you plan on spawning multiple projectiles.</param>
        /// <param name="compensateSimulation"></param>
        /// <param name="predictor">If set then this will override sourcePos and targetPos.</param>
        /// <param name="sourcePos">Override of the source position. If null then the position from the config will be used (or the source transform if that is set).</param>
        /// <param name="targetPos">Override of the target position. If null then the position from the config will be used (or the target transform if that is set).</param>
        /// <param name="source">Override of the source transform. If not null then this transforms position will be used instead of sourcePos.</param>
        /// <param name="target">Override of the target transform.If not null then this transforms position will be used instead of targetPos.</param>
        /// <param name="positionsContainOffset">Whether the sourcePos and targetPos are final (already include the offset). If false then the offsets from the config will be applied to sourcePos and targetPos. Notice: source and target transforms are always considered to be without the offsets.</param>
        /// <param name="startActive">Override of the target transform.If not null then this transforms position will be used instead of targetPos.</param>
        /// <param name="spawnConstraint">If not null then the Allow(..) method of the constraint is checked too and if it returns false then spawning will be aborted (null will be returned).</param>
        /// <returns>Returns null if the target is impossible to hit.</returns>
        /// <exception cref="System.Exception"></exception>
        public static BallisticProjectile Spawn(
            out bool possible, out Vector3 startVelocity, out float angle2D,
            GameObject prefab,
            BallisticProjectileConfig config = null,
            bool compensateSimulation = true,
            IMovementPredictor predictor = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = false,
            bool startActive = true,
            IBallisticProjectileSpawnConstraint spawnConstraint = null
            )
        {
            if (prefab == null)
                Assert.IsNotNull(prefab);
            
            // Check constraint if defined
            if (spawnConstraint != null)
            {
                Evaluate(out possible, out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, source, target, positionsContainOffset);
                
                if (!spawnConstraint.Allow(startVelocity, angle2D, config, source, target, sourcePos, targetPos, predictor))
                    return null;
            }

            if (config != null)
            {
                config = config.Copy();
            }
            else if (prefab.TryGetComponent<BallisticProjectile>(out var prefabProjectile))
            {
                // Here we are using the config from the prefab directly. Do NOT change any values of "prefabConfig".
                // It would alter the prefab and affect all future instances of it. Use a copy instead.
                config = prefabProjectile.Config.Copy();
                // Revert changes on the config caches
                prefabProjectile.clearConfigCache();
            }
            else
            {
                throw new System.Exception("No 'BallisticProjectile' component found on prefab '" + prefab.name + "' and config is NULL. Please use a prefab with a 'BallisticProjectile' component OR set a config.");
            }

            Evaluate(out possible, out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, source, target, positionsContainOffset);

            // Abort if not possible
            if (!possible)
                return null;

            // Instantiate the prefab & add BallisticProjectile if needed
            GameObject go = null;
            bool wasActive = prefab.activeSelf;
            try
            {
                prefab.SetActive(false);
                go = Instantiate(prefab, config.GetSourcePosWithOffset(), Quaternion.identity, null);
            }
            finally
            {
                prefab.SetActive(wasActive);
            }

#if UNITY_EDITOR
            logMissingRigidbodyWarningIfNecessary(go, config.Dimensions);
#endif

            if (!go.TryGetComponent<BallisticProjectile>(out var projectile))
            {
                projectile = go.AddComponent<BallisticProjectile>();
            }

            projectile.ConfigAsset = null;
            projectile.Config = config; // Notice: we overwrite the config of the projectile here. This means the copy implicitly created via Instantiate is not used.

            projectile._compensateSimulation = compensateSimulation;
            projectile._startVelocity = startVelocity;
            projectile._duration = BallisticUtils.CalcDuration(angle2D, startVelocity, config.GetTargetPosWithOffset() - config.GetSourcePosWithOffset(), config.GetGravity());

            if (startActive)
            {
                projectile.SetActive(true);
            }

            return projectile;
        }

        public static void Evaluate(out bool possible, out Vector3 startVelocity, out float angle2D,
            BallisticProjectileConfig config, IMovementPredictor predictor, Vector3? sourcePos, Vector3? targetPos,
            Transform sourceOverride, Transform targetOverride, bool positionsContainOffset)
        {
            var resolvedSource = BallisticProjectileConfig.ResolveSource(config, sourceOverride);
            var resolvedTarget = BallisticProjectileConfig.ResolveTarget(config, targetOverride);

            config.UpdateSourceAndTargetPos(sourcePos, targetPos, resolvedSource, resolvedTarget, positionsContainOffset);

            // Calculate velocity and check if it is possible to reach the target.
            if (predictor != null)
            {
                possible = BallisticUtils.PredictStartVelocity(out startVelocity, out angle2D, config, predictor);
            }
            else
            {
                possible = BallisticUtils.CalcStartVelocity(out startVelocity, out angle2D, config);
            }
        }

        static void logMissingRigidbodyWarningIfNecessary(GameObject obj, PhysicsDimensions dimensions)
        {
            if (dimensions == PhysicsDimensions.Physics3D)
            {
                if(!obj.TryGetComponent<Rigidbody>(out _))
                {
                    Logger.LogWarning("You have used a prefab without a Rigidbody as a ballistic projectile. It probably won't move. Please use a prefab with a Rigidbody!");
                }
            }
            else
            {
                if (!obj.TryGetComponent<Rigidbody2D>(out _))
                {
                    Logger.LogWarning("You have used a prefab without a Rigidbody2D as a ballistic projectile. It probably won't move. Please use a prefab with a Rigidbody2D!");
                }
            }
        }

        /// <summary>
        /// Return whether or not the target can be reached.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="predictor"></param>
        /// <returns></returns>
        public static bool IsPossible(
            BallisticProjectileConfig config,
            IMovementPredictor predictor = null
            )
        {
            return IsPossible(out _, out _, config, predictor, null, null, null, null, false);
        }

        /// <summary>
        /// Return whether or not the target can be reached.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="predictor">If set then this will override sourcePos and targetPos.</param>
        /// <param name="sourcePos">Override of the source position. If null then the position from the config will be used (or the source transform if that is set).</param>
        /// <param name="targetPos">Override of the target position. If null then the position from the config will be used (or the target transform if that is set).</param>
        /// <param name="source">Override of the source transform. If not null then this transforms position will be used instead of sourcePos.</param>
        /// <param name="target">Override of the target transform.If not null then this transforms position will be used instead of targetPos.</param>
        /// <param name="positionsContainOffset">Whether the sourcePos and targetPos are final (already include the offset). If false then the offsets from the config will be applied to sourcePos and targetPos. Notice: source and target transforms are always considered to be without the offsets.</param>
        /// <returns></returns>
        public static bool IsPossible(
            BallisticProjectileConfig config,
            IMovementPredictor predictor = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = false
            )
        {
            return IsPossible(out _, out _, config, predictor, sourcePos, targetPos, source, target, positionsContainOffset);
        }

        /// <summary>
        /// Return whether or not the target can be reached.
        /// </summary>
        /// <param name="startVelocity"></param>
        /// <param name="angle2D"></param>
        /// <param name="config"></param>
        /// <param name="predictor">If set then this will override sourcePos and targetPos.</param>
        /// <param name="sourcePos">Override of the source position. If null then the position from the config will be used (or the source transform if that is set).</param>
        /// <param name="targetPos">Override of the target position. If null then the position from the config will be used (or the target transform if that is set).</param>
        /// <param name="source">Override of the source transform. If not null then this transforms position will be used instead of sourcePos.</param>
        /// <param name="target">Override of the target transform.If not null then this transforms position will be used instead of targetPos.</param>
        /// <param name="positionsContainOffset">Whether the sourcePos and targetPos are final (already include the offset). If false then the offsets from the config will be applied to sourcePos and targetPos. Notice: source and target transforms are always considered to be without the offsets.</param>
        /// <returns></returns>
        public static bool IsPossible(
            out Vector3 startVelocity, out float angle2D,
            BallisticProjectileConfig config,
            IMovementPredictor predictor = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = false
            )
        {
            if (predictor != null)
            {
                return BallisticUtils.PredictStartVelocity(out startVelocity, out angle2D, config, predictor, sourcePos, targetPos, source, target, positionsContainOffset);
            }
            else
            {
                return BallisticUtils.CalcStartVelocity(out startVelocity, out angle2D, config, sourcePos.Value, targetPos.Value, source, target, positionsContainOffset);
            }
        }
    }
}
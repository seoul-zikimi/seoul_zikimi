using System.Runtime.CompilerServices;
using UnityEngine;

namespace Kamgam.HitMe
{
    public partial class AnimationProjectile
    {
        protected enum TargetType
        {
            Transform = 0,
            Rigidbody = 1,
            Rigidbody2D = 2
        }

        protected bool _isInitialized = false;

        // If this is not null then it means we should use this as the new target position.
        protected Vector3? _predictedPosition = null;
        public Vector3? PredictedPosition => _predictedPosition;
        
        protected bool _animationComplete;
        protected TargetType _targetType;

        protected float? _startAngle;
        protected Vector3 _velocity = Vector3.forward;
        protected Vector3 _upAxis = Vector3.up;
        protected bool _physicsPrepared;
        protected Vector3 _lastPath;

        protected void initializeAnimation()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;

            _animationComplete = false;
            detectTargetTypeBeforeAnimation();
            preparePhysicsBeforeAnimation();

            // Memorize the path (used if FollowWithAdaptiveDuration is on)
            _lastPath = getEndPos(Config, _predictedPosition) - Config.GetSourcePosWithOffset();

            if (!Config.FollowSource && Config.Source != null)
            {
                Config.SourcePosition = Config.Source.position;
                Config.Source = null;
            }
        }

        protected void detectTargetTypeBeforeAnimation()
        {
            _targetType = TargetType.Transform;

            if (Config.SupportPhysics)
            {
                if (Rigidbody != null && !Rigidbody.isKinematic)
                {
                    _targetType = TargetType.Rigidbody;
                }
                else if (Rigidbody2D != null
#if UNITY_6000_5_OR_NEWER
                         && Rigidbody2D.bodyType != RigidbodyType2D.Kinematic)
#else
                         && !Rigidbody2D.isKinematic)
#endif
                {
                    _targetType = TargetType.Rigidbody2D;
                }
            }
        }

        public void AbortAnimation()
        {
            if (_animationComplete)
                return;

            _animationComplete = true;

            triggerEndEvent();

            if (Config.SupportPhysics)
                restorePhysicsAfterAnimation();

        }

        float _adaptiveTimeDelta = 0f;

        public void UpdateAnimation(float timeInSec)
        {
            if (_animationComplete)
                return;

            // Adaptive Duration
            if (Config.FollowTarget && Config.FollowWithAdaptiveDuration != AnimationFollowAdaptation.Disabled)
            {
                Vector3 startPos = Config.GetSourcePosWithOffset();
                Vector3 endPos = getEndPos(Config, _predictedPosition, targetPos: Config.Target != null ? Config.Target.position : null);

                var distance = (endPos - startPos).magnitude;
                var lastDistance = _lastPath.magnitude;
                // Scale up based on the distance ratios.
                if (
                        Config.FollowWithAdaptiveDuration == AnimationFollowAdaptation.Both
                    || (Config.FollowWithAdaptiveDuration == AnimationFollowAdaptation.Shrink && distance < lastDistance)
                    || (Config.FollowWithAdaptiveDuration == AnimationFollowAdaptation.Grow && distance > lastDistance)
                    )
                {
                    _adaptiveTimeDelta *= lastDistance / distance;
                    float tmpTime = timeInSec * lastDistance / distance;
                    _adaptiveTimeDelta += (tmpTime - timeInSec) * lastDistance / distance;
                }
                _lastPath = endPos - startPos;
            }
            timeInSec += _adaptiveTimeDelta;

            Transform target = Config.FollowTarget ? Config.Target : null; // set target if FollowTarget is ON
            Vector3 position = Evaluate(Config, timeInSec, getStartAngle(), out _velocity, out _upAxis, _predictedPosition, source: null, target: target);
            _velocity *= TimeScale;

            if (timeInSec >= Config.Duration) // Is the animation at the end in terms of duration?
            {
                // End animation
                _animationComplete = true;
                moveToPosition(position);
                triggerUpdateEvent();
                triggerEndEvent();
                restorePhysicsAfterAnimation();
            }
            else
            {
                // Update the position while animating
                moveToPosition(position);
                triggerUpdateEvent();
            }
        }

        /// <summary>
        /// Returns the velocity in units per second.
        /// </summary>
        /// <returns></returns>
        public Vector3 GetVelocity()
        {
            return _velocity;
        }

        /// <summary>
        /// Returns the velocity in units per second.
        /// </summary>
        public Vector2 GetVelocity2D()
        {
            return _velocity;
        }

        /// <summary>
        /// Returns the current up axis.
        /// </summary>
        /// <returns></returns>
        public Vector3 GetUpAxis()
        {
            return _upAxis;
        }

        public bool IsAnimationComplete()
        {
            return _animationComplete;
        }

        protected void moveToPosition(Vector3 position)
        {
            if (Config.SupportPhysics)
            {
                moveToPositionWithPhysics(position);
            }
            else
            {
                transform.position = position;
            }
        }
        
        
        static float easeTime(AnimationProjectileConfig config, float t)
        {
            if (config.Easing == Easing.CustomCurve)
            {
                return config.EasingCurve.Evaluate(t);
            }
            else if (config.Easing != Easing.Linear)
            {
                return EasingUtils.Ease(config.Easing, t);
            }

            return t;
        }

        public Vector3 Evaluate(float timeInSec)
        {
            float startAngle = getStartAngle();
            return Evaluate(Config, timeInSec, startAngle, out _, out _, _predictedPosition);
        }

        public Vector3 Evaluate(float timeInSec, out Vector3 velocity)
        {
            float startAngle = getStartAngle();
            return Evaluate(Config, timeInSec, startAngle, out velocity, out _, _predictedPosition);
        }

        /// <summary>
        /// We have to make sure the start angle is only evaluated once at the start.
        /// Why? Because it can be set to random and reevaluating would return a differen number every time.
        /// </summary>
        /// <returns></returns>
        protected float getStartAngle()
        {
            if (Config.UseCurves && !_startAngle.HasValue)
            {
                _startAngle = Config.StartAngle.Evaluate();
            }
            else if(!_startAngle.HasValue)
            {
                _startAngle = 0f;
            }

            return _startAngle.Value;
        }

        public static Vector3 Evaluate(
            AnimationProjectileConfig config,
            float timeInSec, float startAngle, Vector3? predictedPosition = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true,
            bool ignoreEasing = false)
        {
            return evaluateInternal(config, timeInSec, startAngle, out _, out _, predictedPosition, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing);
        }

        public static Vector3 Evaluate(
            AnimationProjectileConfig config,
            float timeInSec, float startAngle, out Vector3 velocity, out Vector3 upAxis, Vector3? predictedPosition = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true,
            bool ignoreEasing = false)
        {
            var currentPos = evaluateInternal(config, timeInSec, startAngle, out var uneasedBaseVelocity, out upAxis, predictedPosition, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing);

            // Used to calculate a position back in time for velocity calculation
            float velocityDeltaTime = 0.02f;
            float futureTimeInSec = timeInSec + velocityDeltaTime;
            if (futureTimeInSec < config.Duration - velocityDeltaTime)
            {
                var futurePos = evaluateInternal(config, futureTimeInSec, startAngle, out _, out _, predictedPosition, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing);
                velocity = (futurePos - currentPos) / velocityDeltaTime;
            }
            else
            {
                // Are near the end? Look back instead of ahead to get the velocity.
                var pastTimeInSec = timeInSec - velocityDeltaTime;
                var pastPos = evaluateInternal(config, pastTimeInSec, startAngle, out _, out _, predictedPosition, sourcePos, targetPos, source, target, positionsContainOffset, ignoreEasing);
                velocity = (currentPos - pastPos) / velocityDeltaTime;
            }

            if (config.UseCurves)
            {
                upAxis = Quaternion.FromToRotation(uneasedBaseVelocity, velocity) * upAxis;
            }

            return currentPos;
        }

        static Vector3 evaluateInternal(
            AnimationProjectileConfig config,
            float timeInSec, float startAngle, out Vector3 uneasedBaseVelocity, out Vector3 upAxis, Vector3? predictedPosition = null,
            Vector3? sourcePos = null, Vector3? targetPos = null,
            Transform source = null, Transform target = null,
            bool positionsContainOffset = true,
            bool ignoreEasing = false)
        {
            config.ApplyToPositions(ref sourcePos, ref targetPos, source, target, positionsContainOffset, applyOffsets: true);
            var startPos = sourcePos.Value;
            var endPos = getEndPos(config, predictedPosition, targetPos, target, positionContainsOffset: true);
            
            // With a duration of 0 we skip to the end.
            float t = config.Duration <= 0f ? 1f : Mathf.Clamp01(timeInSec / config.Duration);
            // Easing
            float eT;
            if (ignoreEasing)
            {
                eT = t;
            }
            else
            {
                eT = easeTime(config, t);
            }

            // Calc pos along the base "curve" (usually the _distanceVector)
            Vector3 currentPos = Vector3.LerpUnclamped(startPos, endPos, eT);

            // The direction from source to target
            if (config.Duration > 0f)
            {
                uneasedBaseVelocity = (endPos - startPos) / config.Duration;
            }
            else
            {
                if (config.Duration > 0f)
                    uneasedBaseVelocity = (endPos - startPos) / config.Duration;
                else
                    uneasedBaseVelocity = (endPos - startPos) * 1000f; // In theory it is infinite, but this should suffice.
            }

            Vector3 curvePos = Vector3.zero;
            if (!config.UseCurves)
            {
                upAxis = Quaternion.FromToRotation(Vector3.forward, uneasedBaseVelocity) * Vector3.up;
            }
            else
            {
                // Curves anyone?
                // Rotate curvePos along current velocity axis.
                curvePos = new Vector3(
                    config.CurveX.Evaluate(eT * config.CurveTileX) * config.CurveScaleX,
                    config.CurveY.Evaluate(eT * config.CurveTileY) * config.CurveScaleY,
                    config.CurveZ.Evaluate(eT * config.CurveTileZ) * config.CurveScaleZ
                    );

                // Add angle over time
                float angleOverTime;
                switch (config.AngleOverDuration.Mode)
                {
                    case AnimationValueWithCurve.ValueMode.Constant:
                        angleOverTime = eT * config.AngleOverDuration.Evaluate();
                        break;
                    case AnimationValueWithCurve.ValueMode.RandomBetweenTwoConstants:
                        angleOverTime = eT * config.AngleOverDuration.Evaluate();
                        break;
                    case AnimationValueWithCurve.ValueMode.Curve:
                        angleOverTime = config.AngleOverDuration.Evaluate(eT) * 360f;
                        break;
                    default:
                        angleOverTime = 0f;
                        break;
                }
                angleOverTime *= config.CurveScaleRatio;

                // Calc rotation around the (source to target) axis.
                var angle = startAngle + angleOverTime;
                Quaternion angleRotation = Quaternion.identity;
                Quaternion uneasedBaseVelocityRotation = Quaternion.identity;
                if (uneasedBaseVelocity.sqrMagnitude > 0.001f)
                {
                    angleRotation = Quaternion.AngleAxis(angle, uneasedBaseVelocity);

                    // Calc rotation to match the look direction of the (source to target) axis.
                    uneasedBaseVelocityRotation = Quaternion.LookRotation(uneasedBaseVelocity, config.StartUpAxis);

                    // Apply rotations to get final curve points.
                    curvePos = angleRotation * uneasedBaseVelocityRotation * curvePos;

                    // Debug
                    // Debug.DrawLine(currentPos + curvePos, currentPosInPast + curvePosInPast, Color.blue);
                }

                upAxis = angleRotation * uneasedBaseVelocityRotation * config.StartUpAxis;

                // Add curve pos to pos on base curve
                currentPos += curvePos;
            }

            if (timeInSec >= config.Duration) // Is the animation at the end in terms of duratino?
            {
                return endPos + curvePos;
            }
            else
            {
                return currentPos;
            }
        }

        /// <summary>
        /// Returns the end position with offsets already applied.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="predictedPosition"></param>
        /// <param name="targetPos"></param>
        /// <param name="targetOverride"></param>
        /// <param name="positionContainsOffset"></param>
        /// <returns></returns>
        static Vector3 getEndPos(AnimationProjectileConfig config, Vector3? predictedPosition = null, Vector3? targetPos = null, Transform targetOverride = null, bool positionContainsOffset = true)
        {
            if (predictedPosition.HasValue)
            {
                return predictedPosition.Value + config.GetTargetOffsetInWorldSpace(targetOverride);
            }
            else
            {
                if (targetPos.HasValue)
                {
                    return targetPos.Value + (positionContainsOffset ? Vector3.zero : config.GetTargetOffsetInWorldSpace(targetOverride));
                }
                else
                {
                    return config.GetTargetPosWithOffset(targetOverride);
                }
            }
        }
    }
}
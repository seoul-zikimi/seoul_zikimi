using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace Kamgam.HitMe
{
    public enum AnimationBaseCurveType
    {
        Linear = 0,
        Ballistic = 1
    }

    public enum AnimationFollowAdaptation
    {
        Disabled = 0,
        Shrink = 1,
        Grow = 2,
        Both = 3
    }

    public enum CurveScaleMode
    {
        Stretch = 0,
        Scale = 1,
        Repeat = 2
    }

#if KAMGAM_VISUAL_SCRIPTING
    // Why? See: https://forum.unity.com/threads/unable-to-provide-a-default-for-getvalue-on-object-valueinput.1140022/#post-9138727
    [Unity.VisualScripting.Inspectable]
#endif
    [System.Serializable]
    public class AnimationProjectileConfig : ISerializationCallbackReceiver
    {
        /// <summary>
        /// Source position in world space.
        /// </summary>
        [Header("Projectile")]
        [Tooltip("Used only if no 'Source' transform was set at runtime.")]
        public Vector3 SourcePosition;

        [Tooltip("If specified then this will override the 'SourcePosition'.")]
        public Transform Source;
        public Vector3 SourceOffset;
        public Space SourceOffsetSpace = Space.Self;

        [Tooltip("Used only if no 'Target' transform was set at runtime.")]
        public Vector3 TargetPosition;

        [Tooltip("If specified then this will override the 'TargetPosition'.")]
        public Transform Target;
        public Vector3 TargetOffset;
        public Space TargetOffsetSpace = Space.Self;


        [Header("Animation")]

        [ShowIf("UseSpeed", false, ShowIfAttribute.DisablingType.DontDraw)]
        [Tooltip("The duration of the projectile flight in seconds (unless speed is used).\n\n" +
            "The duration my be altered at runtime if 'UseSpeed' is enabled.")]
        public float Duration = 2f;

        [ShowIf("UseSpeed", true, ShowIfAttribute.DisablingType.DontDraw)]
        [Tooltip("Speed in Units per second based on which the flight duration will be calculated (see 'UseSpeed' tooltip).\n\n" +
            "Notice that the animation is always duration based (meaning easing will still work even if speed is used to calculate the duration).\n\n" +
            "Notice that if UseSpeed is false then this will have no effect on the animation and will NOT return the projectile speed but only the configured " +
            "speed that would be used if UseSpeed was enabled. If you want the actual average speed then use EvaluateSpeed(distance).")]
        public float Speed = 1f;

        /// <summary>
        /// Returns the speed based on the distance and UseSpeed + Speed or Duration if UseSpeed is turned off.
        /// </summary>
        /// <param name="distance"></param>
        /// <returns></returns>
        public float EvaluateSpeed(float distance)
        {
            return UseSpeed ? Speed : distance / Duration;
        }

        [Tooltip("In enabled then the duration is based on the distance between the source and the target and the 'Speed' parameter.\n\n" +
            "Formula: duration = linear distance between source and target / Speed\n\n" +
            "NOTICE: the animation is always duration based (meaning easing will still work even if speed is used to calculate the duration).\n\n" +
            "HINT: You may want to disable 'Follow With Adaptive Duration' if this is enabled.")]
        public bool UseSpeed = false;

        [Tooltip("If disabled then the animation curve will act as if the source was a fixed position in world space (i.e. it will not follow the source transform).")]
        /// <summary>If enabled then the animation curve will act as if the source was a fixed position in world space (i.e. it will not follow the source transform).</summary>
        public bool FollowSource = true;

        [Tooltip("If enabled then the animation curve will be stretched each frame to match the distance between the source and the target. This means it will hit the target exactly, always.\n\n" +
            "If enabled then the prediction can not be used (predictions will be ignored).")]
        public bool FollowTarget = false;

        [ShowIf("FollowTarget", true, ShowIfAttribute.DisablingType.ReadOnly)]
        [Tooltip("If FollowTarget is enabled then the distance between source and target can change. " +
            "This means that the speed and position of the projectile will change too as it keeps its position on the curve.\n\n" +
            "Enable this if you want the keep the distance to the source constant. This will allow the projectile to change the flight duration in proportion to the original distance.")]
        public AnimationFollowAdaptation FollowWithAdaptiveDuration = AnimationFollowAdaptation.Disabled;

        [Tooltip("Wrap behaviour of the custom easing curve AND all the animation curves.")]
        public WrapMode CurveWrap = WrapMode.Loop;

        [Header("Easing")]

        [Tooltip("This is what is known to most people as 'Easing' curves. 0/0 means [time 0]/[progress 0%]. 1/1 means [time at the end of the animation]/[progress 100%]. The curve describes how far the object will have progressed at each point in time from left to right.")]
        public Easing Easing = Easing.Linear;

        [ShowIf("Easing", Easing.CustomCurve, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationCurve EasingCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);


        [Header("Animation Curves")]
        [Tooltip("Turned off by default to save some performance on calculating all these curves.")]
        public bool UseCurves = false;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        [Tooltip("Defines how the curve will scale if the distance from source to target changes.")]
        public CurveScaleMode CurveScaleMode = CurveScaleMode.Stretch;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        [ShowIf("CurveScaleMode", CurveScaleMode.Stretch, ShowIfAttribute.DisablingType.DontDraw, true)]
        [Tooltip("The reference distance that is used to calculate the scale factor of the curve if CurveScaleMode is scale or repeat.")]
        public float CurveScaleReferenceDistance = 10f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationCurve CurveX = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveTileX = 1f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveScaleX = 10f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationCurve CurveY = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveTileY = 1f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveScaleY = 10f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationCurve CurveZ = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveTileZ = 1f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public float CurveScaleZ = 10f;

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public AnimationValue StartAngle = new AnimationValue(0f);

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        [Tooltip("Values are in degrees from start to finish.")]
        public AnimationValueWithCurve AngleOverDuration = new AnimationValueWithCurve(0f);

        [ShowIf("UseCurves", true, ShowIfAttribute.DisablingType.DontDraw)]
        public Vector3 StartUpAxis = Vector3.up;

        [Header("Collisions")]
        [Tooltip("Should the animation stop if another object is hit?\n\n" +
            "To use this you have to add a „CollisionTrigger“ component to your projectile prefab.\n\n"+
            "If physics support is on then before stopping the current velocity will be applied to the rigidbody to ensure consistent movement after the animation.")]
        public bool StopOnCollision = true;

        [Tooltip("Sometimes it is useful to ignore collision for the first N seconds after spawn.\n" +
            "For example to avoid self colliding with the character or nearby objects.\n\n" +
            "NOTICE: This is only for EVENTS. It does not prohibit collisions. It just does not report them as an event.")]
        [ShowIf("StopOnCollision", true, ShowIfAttribute.DisablingType.ReadOnly)]
        [Min(0f)]
        public float CollisionMinAge = 0.5f;

        [Header("Physics")]

        [Tooltip("If enabled then the rigibody of the projectile is set to isKinematic = true and useGravity = false while animating.\n\n" +
            "If disabled then the projectile logic will ignore all physics components(rigidbodies, colliders) on the projectile and just animate the transform. If your objects do have rigidbodies then this may lead to undefined behaviour.\n\n" +
            "The object will use infinite force to move other objects. You probably do not want that. Enable 'StopOnCollision' to avoid that.")]
        public bool SupportPhysics = true;

        [ShowIf("SupportPhysics", true, ShowIfAttribute.DisablingType.ReadOnly)]
        [Tooltip("If enabled then the object will be moved by the animation in every FixedUpdate step via Rigidbody.MovePosition(). \n\n" +
            "FixedUpdate is not necessary in all cases. The recommended approach is to disable this and enable StopOnCollision.\n\n" +
            "Disabling FixedUpdate saves a bit in performance as not FixedUpdate method will be used.")]
        public bool UseFixedUpdate = false;

        [Header("Events")]

        /// <summary>
        /// Trigger collision events only for the first N collisions. Default: 1 means it only trigger for the very first collision.
        /// </summary>
        [Tooltip("Trigger collision events only for the first N collisions. Default: 1 means it only trigger for the very first collision.")]
        public int TriggerFirstNCollisions = 1;

        [Tooltip("Should collisions be triggered after the animation has ended?")]
        public bool TriggerCollisionsAfterEnd = false;

        /// <summary>
        /// A dynamic value (i.e. not serialized). It is updated by Update() of the projectile. Default 1f.<br />
        /// Used in evaluation methods to apply the curve scaling mode to complex parameters (like angle over time).
        /// </summary>
        [System.NonSerialized]
        public float CurveScaleRatio = 1f;

        public AnimationProjectileConfig Copy()
        {
            var copy = new AnimationProjectileConfig();
            CopyValuesTo(copy);
            return copy;
        }

        public void CopyValuesTo(AnimationProjectileConfig copy)
        {
            copy.SourcePosition = SourcePosition;
            copy.Source = Source;
            copy.SourceOffset = SourceOffset;
            copy.SourceOffsetSpace = SourceOffsetSpace;
            copy.TargetPosition = TargetPosition;
            copy.Target = Target;
            copy.TargetOffset = TargetOffset;
            copy.TargetOffsetSpace = TargetOffsetSpace;

            copy.Duration = Duration;
            copy.UseSpeed = UseSpeed;
            copy.Speed = Speed;
            copy.FollowSource = FollowSource;
            copy.FollowTarget = FollowTarget;
            copy.FollowWithAdaptiveDuration = FollowWithAdaptiveDuration;

            copy.Easing = Easing;
            copy.EasingCurve = AnimationUtils.CopyCurve(EasingCurve);

            copy.UseCurves = UseCurves;
            copy.CurveScaleMode = CurveScaleMode;
            copy.CurveScaleReferenceDistance = CurveScaleReferenceDistance;
            copy.CurveX = AnimationUtils.CopyCurve(CurveX);
            copy.CurveTileX = CurveTileX;
            copy.CurveScaleX = CurveScaleX;
            copy.CurveY = AnimationUtils.CopyCurve(CurveY);
            copy.CurveTileY = CurveTileY;
            copy.CurveScaleY = CurveScaleY;
            copy.CurveZ = AnimationUtils.CopyCurve(CurveZ);
            copy.CurveTileZ = CurveTileZ;
            copy.CurveScaleZ = CurveScaleZ;

            if (copy.StartAngle == null)
                copy.StartAngle = new AnimationValue(0f);
            StartAngle.CopyValuesTo(copy.StartAngle);

            if (copy.AngleOverDuration == null)
                copy.AngleOverDuration = new AnimationValueWithCurve(0f);
            AngleOverDuration.CopyValuesTo(copy.AngleOverDuration);

            copy.StartUpAxis = StartUpAxis;
            copy.StopOnCollision = StopOnCollision;
            copy.CollisionMinAge = CollisionMinAge;
            copy.SupportPhysics = SupportPhysics;
            copy.UseFixedUpdate = UseFixedUpdate;

            copy.TriggerFirstNCollisions = TriggerFirstNCollisions;
            copy.TriggerCollisionsAfterEnd = TriggerCollisionsAfterEnd;

            copy.updateWrapMode();

            copy.CurveScaleRatio = CurveScaleRatio;
        }

        protected void updateWrapMode()
        {
            EasingCurve.preWrapMode = CurveWrap;
            EasingCurve.postWrapMode = CurveWrap;

            CurveX.preWrapMode = CurveWrap;
            CurveX.postWrapMode = CurveWrap;

            CurveY.preWrapMode = CurveWrap;
            CurveY.postWrapMode = CurveWrap;

            CurveZ.preWrapMode = CurveWrap;
            CurveZ.postWrapMode = CurveWrap;

            if (AngleOverDuration.Curve != null)
            {
                AngleOverDuration.Curve.preWrapMode = CurveWrap;
                AngleOverDuration.Curve.postWrapMode = CurveWrap;
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            updateWrapMode();
        }

        /// <summary>
        /// Resolves the source transform (combines config and override).<br />
        /// Override can be NULL.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="sourceOverride">Use NULL if no override should be used.</param>
        /// <returns></returns>
        public static Transform ResolveSource(AnimationProjectileConfig config, Transform sourceOverride)
        {
            if (sourceOverride != null)
                return sourceOverride;
            else if (config != null)
                return config.Source;
            else
                return null;
        }

        /// <summary>
        /// Resolves the source transform (combines config and override).<br />
        /// Override can be NULL.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="targetOverride">Use NULL if no override should be used.</param>
        /// <returns></returns>
        public static Transform ResolveTarget(AnimationProjectileConfig config, Transform targetOverride)
        {
            if (targetOverride != null)
                return targetOverride;
            else if (config != null)
                return config.Target;
            else
                return null;
        }

        public void UpdateSourceAndTargetPos(Vector3 sourcePos, Vector3 targetPos, Transform source = null, Transform target = null)
        {
            UpdateSourcePos(sourcePos, source);
            UpdateTargetPos(targetPos, target);
        }

        public void UpdateTargetPos(Vector3 targetPos, Transform target)
        {
            TargetPosition = targetPos;
            if (target != null)
            {
                Target = target;
            }
        }

        public void UpdateSourcePos(Vector3 sourcePos, Transform source)
        {
            SourcePosition = sourcePos;
            if (source != null)
            {
                Source = source;
            }
        }

        /// <summary>
        /// Takes two position values and merges them with the config data. Returns them with or without applied offsets depending on 'addOffsets'.
        /// </summary>
        /// <param name="sourcePos"></param>
        /// <param name="targetPos"></param>
        /// <param name="sourceOverride"></param>
        /// <param name="targetOverride"></param>
        /// <param name="positionContainsOffset">Are the input positions (sourcePos, targetPos) already with offsets?</param>
        /// <param name="applyOffsets">Should the output positions (sourcePos, targetPos) include offsets or not?</param>
        public void ApplyToPositions(ref Vector3? sourcePos, ref Vector3? targetPos, Transform sourceOverride, Transform targetOverride, bool positionContainsOffset, bool applyOffsets)
        {
            ApplyToSourcePosition(this, ref sourcePos, sourceOverride, positionContainsOffset, applyOffsets);
            ApplyToTargetPosition(this, ref targetPos, targetOverride, positionContainsOffset, applyOffsets);
        }

        public void ApplyToSourcePosition(ref Vector3? sourcePos, Transform sourceOverride, bool positionContainsOffset, bool applyOffsets)
        {
            ApplyToSourcePosition(this, ref sourcePos, sourceOverride, positionContainsOffset, applyOffsets);
        }

        public void ApplyToTargetPosition(ref Vector3? targetPos, Transform targetOverride, bool positionContainsOffset, bool applyOffsets)
        {
            ApplyToTargetPosition(this, ref targetPos, targetOverride, positionContainsOffset, applyOffsets);
        }

        public static void ApplyToSourcePosition(in AnimationProjectileConfig config, ref Vector3? sourcePos, Transform sourceOverride, bool positionContainsOffset, bool applyOffsets)
        {
            // Source
            if (sourceOverride != null)
            {
                sourcePos = sourceOverride.position;
                if (applyOffsets)
                    sourcePos += config.GetSourceOffsetInWorldSpace(sourceOverride);
            }
            else if (!sourcePos.HasValue)
            {
                if (config.Source != null)
                {
                    sourcePos = config.Source.position;
                }
                else
                {
                    sourcePos = config.SourcePosition;
                }

                if (applyOffsets)
                    sourcePos += config.GetSourceOffsetInWorldSpace(sourceOverride);
            }
            else
            {
                if (applyOffsets && !positionContainsOffset)
                    sourcePos += config.GetSourceOffsetInWorldSpace(sourceOverride);
            }
        }

        public static void ApplyToTargetPosition(in AnimationProjectileConfig config, ref Vector3? targetPos, Transform targetOverride, bool positionContainsOffset, bool applyOffsets)
        {
            if (targetOverride != null)
            {
                targetPos = targetOverride.position;
                if (applyOffsets)
                    targetPos += config.GetTargetOffsetInWorldSpace(targetOverride);
            }
            else if (!targetPos.HasValue)
            {
                if (config.Target != null && config.FollowTarget)
                {
                    targetPos = config.Target.position;
                }
                else
                {
                    targetPos = config.TargetPosition;
                }

                if (applyOffsets)
                    targetPos += config.GetTargetOffsetInWorldSpace(targetOverride);
            }
            else
            {
                if (applyOffsets && !positionContainsOffset)
                    targetPos += config.GetTargetOffsetInWorldSpace(targetOverride);
            }
        }

        /// <summary>
        /// Source position in world space.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public Vector3 GetSourcePosWithOffset(Transform source = null)
        {
            if (source == null)
                source = Source;

            if (source != null)
            {
                return source.transform.position + GetSourceOffsetInWorldSpace(source);
            }
            else
            {
                return SourcePosition + GetSourceOffsetInWorldSpace(source);
            }
        }

        public Vector3 GetSourceOffsetInWorldSpace(Transform source = null)
        {
            if (source == null)
                source = Source;

            if (SourceOffsetSpace == Space.Self && source != null)
            {
                return source.TransformVector(SourceOffset);
            }
            else
            {
                return SourceOffset;
            }
        }

        public Vector3 GetTargetPosWithOffset(Transform target = null)
        {
            if (target == null)
                target = Target;
            
            if (target != null)
            {
                return target.transform.position + GetTargetOffsetInWorldSpace(target);
            }
            else
            {
                return TargetPosition + GetTargetOffsetInWorldSpace(target);
            }
        }

        public Vector3 GetTargetOffsetInWorldSpace(Transform target = null)
        {
            if (target == null)
                target = Target;

            if (TargetOffsetSpace == Space.Self && target != null)
            {
                return target.TransformVector(TargetOffset);
            }
            else
            {
                return TargetOffset;
            }
        }

        public Vector3 GetTargetVector()
        {
            return GetTargetVector(null, null);
        }

        public Vector3 GetTargetVector(AnimationProjectileSource projectileSource)
        {
            if (projectileSource != null)
                return GetTargetVector(projectileSource.SourceOverride, projectileSource.TargetOverride);
            else
                return GetTargetVector();
        }

        public Vector3 GetTargetVector(Transform sourceOverride, Transform targetOverride)
        {
            var source = AnimationProjectileConfig.ResolveSource(this, sourceOverride);
            var target = AnimationProjectileConfig.ResolveTarget(this, targetOverride);

            return GetTargetPosWithOffset(target) - GetSourcePosWithOffset(source);
        }

        public float GetLinearDistance()
        {
            return GetLinearDistance(null);
        }

        public float GetLinearDistance(AnimationProjectileSource projectileSource)
        {
            return GetTargetVector(projectileSource).magnitude;
        }
    }
}
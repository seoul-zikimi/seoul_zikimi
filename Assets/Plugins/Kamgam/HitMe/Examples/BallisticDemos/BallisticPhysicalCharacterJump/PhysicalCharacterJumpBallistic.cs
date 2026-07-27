using System.Collections.Generic;
using UnityEngine;

namespace Kamgam.HitMe
{
    [RequireComponent(typeof(BallisticProjectileConfigProvider))]
    public class PhysicalCharacterJumpBallistic : MonoBehaviour
    {
        public GameObject JumpPreviewRendererPrefab;
        public Transform JumpTargetIndicator;
        public float MaxJumpTargetHeightDelta = 10f;

        protected BallisticProjectileConfigProvider _configProvider;
        public BallisticProjectileConfigProvider ConfigProvider
        {
            get
            {
                if (_configProvider == null)
                {
                    _configProvider = this.GetComponent<BallisticProjectileConfigProvider>();
                }
                return _configProvider;
            }
        }

        // Cache the renderer for re-use.
        protected ProjectileLineRenderer _jumpPreviewRenderer;
        protected Vector3? _jumpTarget;

        public BallisticProjectileConfig Config
        {
            get => ConfigProvider.Config;
        }

        protected RaycastHit[] _raycastHits = new RaycastHit[10];

        protected Queue<Vector3> _jumpTargetPositions = new Queue<Vector3>();

        public virtual Vector3? FindTarget(Vector3 velocity)
        {
            if (velocity.sqrMagnitude < 0.01f)
                return null;

            // Very simple jump target calculation. We simply assume a 45 degree angle and calculate how far that would take us.
            var distanceOnGround = BallisticUtils.CalcMaxDistance(45f, velocity.magnitude, Physics.gravity.magnitude);
            var directionXZ = new Vector3(velocity.x, 0f, velocity.z).normalized;
            var jumpTargetXZ = transform.position + directionXZ * distanceOnGround;

            // The we do a raycast downwards to check where it intersets a collider and that is our target position.
            var ray = new Ray(new Vector3(jumpTargetXZ.x, jumpTargetXZ.y + MaxJumpTargetHeightDelta, jumpTargetXZ.z), Vector3.down);
            int hits = Physics.RaycastNonAlloc(ray, _raycastHits, MaxJumpTargetHeightDelta * 20f);
            // Physics.RaycastNonAlloc are not sorted by distance so we have to do it on our own.
            if (hits > 0)
            {
                _jumpTarget = null;
                float minDistance = float.MaxValue;
                for (int i = 0; i < hits; i++)
                {
                    if (_raycastHits[i].distance < minDistance)
                    {
                        minDistance = _raycastHits[i].distance;
                        _jumpTarget = _raycastHits[i].point;
                    }
                }
            }

            // Averaging the target pos
            if (_jumpTarget.HasValue)
            {
                _jumpTargetPositions.Enqueue(_jumpTarget.Value);
                if (_jumpTargetPositions.Count > 30)
                {
                    _jumpTargetPositions.Dequeue();
                    _jumpTarget = avgJumpTarget();
                }
            }

            // Update the target indicator
            if (JumpTargetIndicator != null && _jumpTarget.HasValue)
            {
                JumpTargetIndicator.transform.position = _jumpTarget.Value;
            }

            return _jumpTarget;
        }

        protected Vector3 avgJumpTarget()
        {
            var target = Vector3.zero;
            if(_jumpTargetPositions.Count == 30)
            {
                foreach (var t in _jumpTargetPositions)
                {
                    target += t;
                }
            }

            return target / 30f;
        }

        public Vector3? GetTargetPos()
        {
            return _jumpTarget;
        }

        public bool DrawPreview(Vector3? targetPos)
        {
            if (!targetPos.HasValue)
                return false;

            // Ensure the offsets from the config are applied.
            Config.ApplyToTargetPosition(ref targetPos, null, positionContainsOffset: false, applyOffsets: true);

            if (JumpPreviewRendererPrefab != null)
            {
                _jumpPreviewRenderer = BallisticProjectile.DrawPath(
                    out bool possible,
                    sourcePos: transform.position,
                    targetPos: targetPos.Value,
                    config: Config,
                    segmentsPerUnit: 1.2f,
                    _jumpPreviewRenderer,
                    JumpPreviewRendererPrefab
                    );
                if (_jumpPreviewRenderer != null) // Is only null if not cached yet and no ballistic path is possible
                {
                    _jumpPreviewRenderer.Renderer.alignment = LineAlignment.View;
                }

                return possible;
            }

            return false;
        }

        public bool Jump(Vector3 targetPos)
        {
            Vector3? sourcePosition = transform.position;
            Vector3? targetPosition = targetPos;

            // Ensure the offsets from the config are applied.
            Config.ApplyToPositions(ref sourcePosition, ref targetPosition, null, null, positionContainsOffset: false, applyOffsets: true);

            bool possible = BallisticUtils.CalcStartVelocity(out var startVelocity, out _, Config, sourcePosition, targetPosition);
            if(possible)
                BallisticUtils.ApplyStartVelocity(startVelocity, gameObject, sourcePosition.Value, compensateSimulation: true);
            return possible;
        }

        public bool CalculateStartVelocity(out Vector3 startVelocity)
        {
            return BallisticUtils.CalcStartVelocity(out startVelocity, out _, Config);
        }
    }
}
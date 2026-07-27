using UnityEngine;

namespace Kamgam.HitMe
{
    using UnityEngine;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
#endif

    [RequireComponent(typeof(PhysicalCharacterJumpBallistic))]
    public class PhysicalCharacterController : MonoBehaviour
    {
        public float MaxSpeed = 10f;
        public float AccelerationForce = 20f;
        public float RotationSpeed = 90f;

        public PhysicalCharacterJumpBallistic Jump;

        private Rigidbody rb;
        private bool isJumping = false;
        private float _rotation = 0f;
        private float _rotationSpeed = 0f;
        private float _speedLimit = 0f;
        private bool _running = false;

        protected void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        protected void Update()
        {
#if ENABLE_INPUT_SYSTEM
            // Using the new input system
            _speedLimit = 0f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                _speedLimit = 1f * MaxSpeed;
            else if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                _speedLimit = -1f * MaxSpeed;

            _rotationSpeed = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                _rotationSpeed = -1f * RotationSpeed;
            else if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                _rotationSpeed = 1f * RotationSpeed;

            _running = false;
            if (Keyboard.current.leftShiftKey.isPressed && !isJumping)
            {
                _running = true;
            }

            if (Keyboard.current.spaceKey.wasPressedThisFrame && !isJumping)
            {
                jump();
            }
#else
            // Using the old input system
            _speedLimit = Input.GetAxis("Vertical") * MaxSpeed;
            _rotationSpeed = Input.GetAxis("Horizontal") * RotationSpeed;

            _running = false;
            if (Input.GetKey(KeyCode.LeftShift) && !isJumping)
            {
                _running = true;
            }

            if (Input.GetKeyDown(KeyCode.Space) && !isJumping)
            {
                jump();
            }
#endif

            var targetPos = Jump.GetTargetPos();
            if (!isJumping)
            {
                targetPos = Jump.FindTarget(rb.GetVelocity() * 1.3f);
                Jump.DrawPreview(targetPos);
            }
        }

        protected void FixedUpdate()
        {
            // Velocity
            float speedSign = 0f;
            if (!isJumping)
            {
                var speed = _speedLimit * (_running ? 1.3f : 1f);

                speedSign = Mathf.Sign(_speedLimit);
                if (rb.GetVelocity().magnitude < speed)
                {
                    rb.AddRelativeForce(Vector3.forward * speedSign * AccelerationForce * (_running ? 1.3f : 1f), ForceMode.Acceleration);
                }
            }

            // Rotation
            rb.angularVelocity = Vector3.zero;
            float rotationDelta = _rotationSpeed * Time.fixedDeltaTime;
            _rotation += rotationDelta;
            _rotation %= 360f;
            rb.MoveRotation(Quaternion.AngleAxis(_rotation, Vector3.up));

            if (!isJumping)
            {
                // Align velocity with rotation
                var velocity = Quaternion.Euler(0f, rotationDelta * speedSign, 0f) * rb.GetVelocity();

                // Fast slow down
                if (Mathf.Abs(_speedLimit) > 0.001f)
                {
                    rb.SetVelocity(velocity);
                }
                else
                {
                    rb.SetVelocity(new Vector3(velocity.x * 0.96f, velocity.y, velocity.z * 0.96f));
                }

            }
        }

        protected void jump()
        {
            var target = Jump.GetTargetPos();
            if (target.HasValue)
            {
                isJumping = Jump.Jump(target.Value);
            }
        }

        protected void OnCollisionEnter(Collision collision)
        {
            isJumping = false;
        }
    }
}
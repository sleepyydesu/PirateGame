using UnityEngine;
namespace PirateGame.PlayerV2
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private Transform movementCamera;
        [Tooltip("Solid world only. Exclude all player colliders and trigger hurtboxes.")]
        [SerializeField] private LayerMask world;
        public float runSpeed = 5f, sprintSpeed = 8f, crouchSpeed = 2f;
        public float acceleration = 35f, deceleration = 45f, airAcceleration = 10f;
        public float turnRate = 720f, gravity = -24f, jumpHeight = 1.1f;
        public float coyoteSeconds = 0.10f, probeDistance = 0.12f, groundStickSpeed = 2f;
        public float crouchHeight = 1.35f;
        [Header("Sound / presentation hooks")]
        public UnityEngine.Events.UnityEvent onJump = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent<float> onLand = new UnityEngine.Events.UnityEvent<float>();
        public bool Grounded { get; private set; }
        public bool Crouched { get; private set; }
        public float VerticalSpeed { get; private set; }
        public Vector3 HorizontalVelocity { get; private set; }
        public Vector3 WishDirection { get; private set; }
        public float LastImpactSpeed { get; private set; }
        public bool JustLanded { get; private set; }
        public bool CanJump => VerticalSpeed <= 0f && Time.time - lastGroundTime <= coyoteSeconds;
        public bool CanUseGroundAction => Grounded && !Crouched;
        private CharacterController capsule;
        private float originalHeight, originalStep, lastGroundTime = float.NegativeInfinity;
        private Vector3 originalCenter;
        private Vector3 normal = Vector3.up;
        private readonly Collider[] clearance = new Collider[32];
        private bool wasGrounded;
        private Vector3 knockbackDirection;
        private float knockbackDistance;
        private float knockbackDuration;
        private float knockbackElapsed;

        public bool HasKnockback =>
            knockbackElapsed < knockbackDuration;
        private void Awake()
        {
            capsule = GetComponent<CharacterController>(); originalHeight = capsule.height;
            originalCenter = capsule.center; originalStep = capsule.stepOffset;
            if (movementCamera == null && Camera.main != null) movementCamera = Camera.main.transform;
            if (world.value == 0) Debug.LogWarning("Motor world mask is empty; assign solid scenery.", this);
            if (transform.lossyScale != Vector3.one) Debug.LogWarning("Use scale 1 on PlayerV2 root.", this);
        }
        public bool Validate() => world.value != 0 && gravity < 0f && jumpHeight > 0f && runSpeed > 0f &&
            transform.lossyScale == Vector3.one;
        public Vector3 CameraDirection(Vector2 input)
        {
            Vector3 f = movementCamera != null ? Vector3.ProjectOnPlane(movementCamera.forward, Vector3.up) : transform.forward;
            if (f.sqrMagnitude < 0.001f) f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            f.Normalize();
            return Vector3.ClampMagnitude(f * input.y + Vector3.Cross(Vector3.up, f) * input.x, 1f);
        }
        // Probe only at/below the capsule foot. Ascending jumps cannot be snapped back down.
        public void Probe(Vector2 input)
        {
            JustLanded = false; WishDirection = CameraDirection(input);
            if (!capsule.enabled) { Grounded = false; return; }
            Vector3 center = transform.TransformPoint(capsule.center);
            float r = Mathf.Max(0.01f, capsule.radius - capsule.skinWidth * 0.5f);
            Vector3 bottom = center - Vector3.up * (capsule.height * 0.5f - capsule.radius);
            bool hit = Physics.SphereCast(bottom + Vector3.up * 0.04f, r, Vector3.down,
                out RaycastHit ground, probeDistance + capsule.skinWidth + 0.04f, world, QueryTriggerInteraction.Ignore);
            Grounded = VerticalSpeed <= 0f && hit && Vector3.Angle(ground.normal, Vector3.up) <= capsule.slopeLimit;
            normal = Grounded ? ground.normal : Vector3.up;
            if (Grounded)
            {
                lastGroundTime = Time.time;
                if (!wasGrounded)
                {
                    JustLanded = true;
                    LastImpactSpeed = Mathf.Max(0f, -VerticalSpeed);
                    onLand.Invoke(LastImpactSpeed);
                }
            }
            wasGrounded = Grounded;
        }
        public bool Jump()
        {
            if (!CanJump || !TryCrouch(false)) return false;
            VerticalSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);
            Grounded = wasGrounded = false; lastGroundTime = float.NegativeInfinity;
            onJump.Invoke();
            return true;
        }
        public bool TryCrouch(bool crouch)
        {
            if (crouch == Crouched) return true;
            float h = crouch ? Mathf.Max(crouchHeight, capsule.radius * 2f) : originalHeight;
            Vector3 center = originalCenter + Vector3.up * ((h - originalHeight) * 0.5f);
            if (!crouch)
            {
                Vector3 c = transform.TransformPoint(center);
                float r = Mathf.Max(0.01f, capsule.radius - capsule.skinWidth);
                float half = Mathf.Max(0f, h * 0.5f - capsule.radius);
                int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.up * half, c - Vector3.up * half,
                    r, clearance, world, QueryTriggerInteraction.Ignore);
                if (n == clearance.Length) return false; // Conservatively refuse an ambiguous full buffer.
                for (int i = 0; i < n; i++)
                    if (clearance[i] != capsule && !clearance[i].transform.IsChildOf(transform)) return false;
            }
            capsule.height = h; capsule.center = center; Crouched = crouch; return true;
        }
        public void StopHorizontalMotion()
        {
            HorizontalVelocity = Vector3.zero;
        }

        public void ApplyKnockback(
            Vector3 direction,
            float distance,
            float seconds)
        {
            direction.y = 0f;

            if (!capsule.enabled ||
                distance <= 0f ||
                direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            knockbackDirection = direction.normalized;
            knockbackDistance = distance;
            knockbackDuration = Mathf.Max(0.05f, seconds);
            knockbackElapsed = 0f;

            StopHorizontalMotion();
        }

        private Vector3 ConsumeKnockback(float dt)
        {
            if (!HasKnockback)
                return Vector3.zero;

            float before = knockbackElapsed / knockbackDuration;

            knockbackElapsed = Mathf.Min(
                knockbackDuration,
                knockbackElapsed + dt);

            float after = knockbackElapsed / knockbackDuration;

            // Fast at first, then slows to a stop.
            float progressBefore =
                1f - (1f - before) * (1f - before);

            float progressAfter =
                1f - (1f - after) * (1f - after);

            return knockbackDirection *
                   knockbackDistance *
                   (progressAfter - progressBefore);
        }
        // One persistent velocity. Action states choose policy, never reset gravity.
        public void Step(float dt, Vector2 input, bool sprint, float movementScale, bool rotate,
            bool scriptedMotion, Vector3 actionVelocity, bool rootTranslation,
            float moveSpeedOverride = -1f)
        {
            if (!capsule.enabled || dt <= 0f) return;
            WishDirection = CameraDirection(input);
            float speed = Crouched ? crouchSpeed : sprint ? sprintSpeed :
                moveSpeedOverride >= 0f ? moveSpeedOverride : runSpeed;
            Vector3 desired = WishDirection * speed * movementScale;
            bool pushed = HasKnockback;

            if (pushed)
                HorizontalVelocity = Vector3.zero;
            else if (scriptedMotion)
                HorizontalVelocity = actionVelocity;
            else if (rootTranslation) HorizontalVelocity = Vector3.zero;
            else HorizontalVelocity = Vector3.MoveTowards(HorizontalVelocity, desired,
                (Grounded ? (desired.sqrMagnitude > 0.001f ? acceleration : deceleration) : airAcceleration) * dt);
            Vector3 horizontal = HorizontalVelocity;
            if (Grounded && !scriptedMotion && horizontal.sqrMagnitude > 0.001f)
                horizontal = Vector3.ProjectOnPlane(horizontal, normal).normalized * horizontal.magnitude;
            if (Grounded && VerticalSpeed < 0f) VerticalSpeed = -groundStickSpeed;
            else VerticalSpeed = Mathf.Max(VerticalSpeed + gravity * dt, -55f);
            capsule.stepOffset = Grounded ? originalStep : 0f;
            Vector3 push = ConsumeKnockback(dt);

            if (Grounded && push.sqrMagnitude > 0.0001f)
            {
                push = Vector3.ProjectOnPlane(push, normal).normalized *
                       push.magnitude;
            }

            CollisionFlags flags = capsule.Move(
                (horizontal + Vector3.up * VerticalSpeed) * dt + push);

            if (pushed && (flags & CollisionFlags.Sides) != 0)
                knockbackElapsed = knockbackDuration;
            if ((flags & CollisionFlags.Above) != 0 && VerticalSpeed > 0f) VerticalSpeed = 0f;
            if (rotate && !pushed && WishDirection.sqrMagnitude > 0.001f)
                Face(WishDirection, turnRate, dt);
        }
        public void Face(Vector3 direction, float degrees, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degrees * dt);
        }
        public void ApplyRootTranslation(Vector3 delta)
        {
            if (!capsule.enabled) return;
            delta.y = 0f; capsule.Move(delta); // Gravity already ran in Step; only authored horizontal delta is added.
        }
        public void ResetMotion()
        {
            knockbackElapsed = knockbackDuration = 0f;
            HorizontalVelocity = Vector3.zero;
            VerticalSpeed = 0f;
            lastGroundTime = float.NegativeInfinity;
        }
    }
}

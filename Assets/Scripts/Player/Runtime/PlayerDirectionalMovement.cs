using UnityEngine;

namespace PirateGame.PlayerV2
{
    // Called once by PlayerControllerV2 instead of its direct Motor.Step call.
    // There is deliberately no Update or second CharacterController.Move here.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(PlayerCombat))]
    public sealed class PlayerDirectionalMovement : MonoBehaviour
    {
        [Header("Walking")]
        [Min(0.1f)] public float walkSpeed = 2f;
        public bool cameraFacingWalk = true;
        public bool faceCameraWhileIdle = false;
        [Min(1f)] public float cameraTurnRate = 540f;

        [Header("Animator; moveX / moveZ are local metres per second")]
        [Min(0f)] public float directionDamping = 0.06f;

        private static readonly int MoveX = Animator.StringToHash("moveX");
        private static readonly int MoveZ = Animator.StringToHash("moveZ");
        private PlayerMotor motor;
        private PlayerInputReader input;
        private PlayerCombat combat;
        private Animator animator;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            combat = GetComponent<PlayerCombat>();
            animator = GetComponent<Animator>();
            if (animator == null || !HasFloat(MoveX) || !HasFloat(MoveZ))
            {
                Debug.LogError(
                    "Directional walking: add Float parameters moveX and moveZ " +
                    "to the player's Animator Controller. Component disabled; " +
                    "PlayerControllerV2 will use its previous movement.", this);
                enabled = false;
            }
        }

        private bool HasFloat(int hash)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == hash &&
                    parameter.type == AnimatorControllerParameterType.Float)
                    return true;
            return false;
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;

            Vector2 move = input.Move;
            bool sprint = combat.IsFree && input.Sprint;
            bool pushed = motor.HasKnockback;

            // Attack aim, dodge facing and hurt states retain their existing owners.
            // RotateWithMovement also allows normal facing again in attack recovery.
            bool facingAllowed = combat.RotateWithMovement ||
                                 combat.Kind == PlayerActionKind.Block;
            bool cameraFacing = cameraFacingWalk && motor.Grounded &&
                                !motor.Crouched && !sprint && facingAllowed;

            if (cameraFacing && !pushed &&
                (move.sqrMagnitude > 0.001f || faceCameraWhileIdle ||
                 combat.Kind == PlayerActionKind.Block))
            {
                // CameraDirection removes camera pitch and returns a horizontal direction.
                motor.Face(motor.CameraDirection(Vector2.up), cameraTurnRate, dt);
            }

            Vector3 before = transform.position;
            motor.Step(
                dt, move, sprint, combat.MovementScale,
                combat.RotateWithMovement && !cameraFacing,
                combat.ScriptedMotion, combat.ConsumeActionVelocity(dt),
                combat.RootTranslation, Mathf.Max(0.1f, walkSpeed));

            // Use displacement after collision, not just input: no walking into a wall.
            Vector3 velocity = (transform.position - before) / dt;
            velocity.y = 0f;
            Vector3 local = transform.InverseTransformDirection(velocity);
            local.y = 0f;

            // Forced movement must not animate ordinary walking underneath a reaction.
            if (pushed || combat.ScriptedMotion || combat.RootTranslation ||
                combat.Kind == PlayerActionKind.Stagger ||
                combat.Kind == PlayerActionKind.Dead)
                local = Vector3.zero;

            if (local.sqrMagnitude < 0.0004f)
            {
                animator.SetFloat(MoveX, 0f);
                animator.SetFloat(MoveZ, 0f);
            }
            else
            {
                animator.SetFloat(MoveX, local.x, directionDamping, dt);
                animator.SetFloat(MoveZ, local.z, directionDamping, dt);
            }
        }
    }
}

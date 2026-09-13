using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Controls")]
    public float playerSpeed = 5.0f;
    public float crouchSpeed = 2.0f;
    public float sprintSpeed = 8.0f;
    public float jumpHeight = 1.0f;
    public float gravityMultiplier = 2;
    public float rotationSpeed = 5f;
    public float crouchColliderHeight = 1.35f;

    [Header("Animation Smoothing")]
    [Range(0, 1)]
    public float speedDampTime = 0.1f;
    [Range(0, 1)]
    public float velocityDampTime = 0.9f;
    [Range(0, 1)]
    public float rotationDampTime = 0.2f;
    [Range(0, 1)]
    public float airControl = 0.5f;

    public StateMachine movementSM;
    public StandingState standing;
    public CrouchingState crouching;
    public JumpingState jumping;
    public LandingState landing;
    public SprintingState sprinting;
    public SprintJumpState sprintJumping;
    public CombatState combatting;
    public AttackState attacking;
    public RollState rolling;
    public TimedAnimState specialAttacking;
    public TimedAnimState thrustAttacking;
    public TimedAnimState specialAttacking2;

    [HideInInspector]
    public float gravityValue = -9.81f;
    [HideInInspector]
    public float normalColliderHeight;
    [HideInInspector]
    public CharacterController controller;
    [HideInInspector]
    public PlayerInput playerInput;
    [HideInInspector]
    public Transform cameraTransform;
    [HideInInspector]
    public Animator animator;
    [HideInInspector]
    public Vector3 playerVelocity;

    const int BaseLayerIndex = 0;
    const int CombatLayerIndex = 1;
    const int ArmsLayerIndex = 2;

    public bool isInCombat = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        playerInput = GetComponent<PlayerInput>();
        cameraTransform = Camera.main.transform;

        movementSM = new StateMachine();
        standing = new StandingState(this, movementSM);
        jumping = new JumpingState(this, movementSM);
        crouching = new CrouchingState(this, movementSM);
        landing = new LandingState(this, movementSM);
        sprinting = new SprintingState(this, movementSM);
        sprintJumping = new SprintJumpState(this, movementSM);
        combatting = new CombatState(this, movementSM);
        attacking = new AttackState(this, movementSM);
        rolling = new RollState(this, movementSM);
        specialAttacking = new TimedAnimState(this, movementSM, "specialAttack", "specialAttackFinished");
        thrustAttacking = new TimedAnimState(this, movementSM, "thrustAttack", "thrustAttackFinished", false);
        specialAttacking2 = new TimedAnimState(this, movementSM, "specialAttack2", "specialAttack2Finished");

        movementSM.Initialize(standing);

        normalColliderHeight = controller.height;
        gravityValue *= gravityMultiplier;

        // Start with combat layers disabled (player in base state)
        SetCombatAnimationLayers(false);
    }

    public void SetCombatAnimationLayers(bool combatActive)
    {
        animator.SetLayerWeight(BaseLayerIndex, combatActive ? 0f : 1f);
        animator.SetLayerWeight(CombatLayerIndex, combatActive ? 1f : 0f);
        animator.SetLayerWeight(ArmsLayerIndex, combatActive ? 1f : 0f);
    }

    // Update is called once per frame
    void Update()
    {
        movementSM.currentState.HandleInput();

        movementSM.currentState.LogicUpdate();
    }

    private void FixedUpdate()
    {
        movementSM.currentState.PhysicsUpdate();
    }
}


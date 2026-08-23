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

#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public StateMachine movementSM;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public StandingState standing;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public CrouchingState crouching;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public JumpingState jumping;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public LandingState landing;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public SprintingState sprinting;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]
#pragma warning disable UAC1001 // Public field skipped by serialization due to missing [Serializable]
    public SprintJumpState sprintJumping;
#pragma warning restore UAC1001 // Public field skipped by serialization due to missing [Serializable]

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

        movementSM.Initialize(standing);

        normalColliderHeight = controller.height;
        gravityValue *= gravityMultiplier;
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

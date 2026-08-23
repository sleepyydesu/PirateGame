using UnityEngine;

public class CrouchingState : State
{
    float playerSpeed;
    bool belowCeiling;
    bool crouchHeld;
    bool jump;
    bool sprint;

    bool grounded;
    float gravityValue;
    Vector3 currentVelocity;

    public CrouchingState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public override void Enter()
    {
        base.Enter();

        character.animator.SetTrigger("crouch");
        belowCeiling = false;
        crouchHeld = false;
        jump = false;
        sprint = false;
        gravityVelocity.y = 0;

        playerSpeed = character.crouchSpeed;
        character.controller.height = character.crouchColliderHeight;
        character.controller.center = new Vector3(0, character.crouchColliderHeight / 2, 0);
        grounded = character.controller.isGrounded;
        gravityValue = character.gravityValue;
    }

    public override void Exit()
    {
        base.Exit();

        character.animator.SetTrigger("move");
        belowCeiling = true;
        crouchHeld = true;

        character.controller.height = character.normalColliderHeight;
        character.controller.center = new Vector3(0, character.normalColliderHeight / 2, 0);
        gravityVelocity.y = 0;
        character.playerVelocity = new Vector3(input.x, 0, input.y);
    }

    public override void HandleInput()
    {
        base.HandleInput();

        if (crouchAction.triggered && !belowCeiling)
        {
            crouchHeld = true;
        }
        if (jumpAction.triggered && !belowCeiling)
        {
            jump = true;
        }
        if (sprintAction.triggered && !belowCeiling)
        {
            sprint = true;
        }

        input = moveAction.ReadValue<Vector2>();
        velocity = new Vector3(input.x, 0, input.y);
        velocity = velocity.x * character.cameraTransform.right.normalized + velocity.z * character.cameraTransform.forward.normalized;
        velocity.y = 0f;
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        UpdateSpeedParam(input.magnitude, character.speedDampTime);

        if (jump)
        {
            stateMachine.ChangeState(character.jumping);
        }
        else if (sprint)
        {
            stateMachine.ChangeState(character.sprinting);
        }
        else if (crouchHeld)
        {
            stateMachine.ChangeState(character.standing);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();

        belowCeiling = CheckCollisionOverlap(character.normalColliderHeight);

        gravityVelocity.y += gravityValue * Time.deltaTime;
        grounded = character.controller.isGrounded;

        if (grounded && gravityVelocity.y < 0)
        {
            gravityVelocity.y = 0f;
        }

        currentVelocity = Vector3.Lerp(currentVelocity, velocity, character.velocityDampTime);
        
        character.controller.Move(currentVelocity * Time.deltaTime * playerSpeed + gravityVelocity * Time.deltaTime);

        if (velocity.magnitude > 0)
        {
            character.transform.rotation = Quaternion.Slerp(character.transform.rotation, Quaternion.LookRotation(velocity), character.rotationDampTime); 
        }
    }

    public bool CheckCollisionOverlap(float checkHeight)
    {
        int layerMask = ~(1 << character.gameObject.layer);
        Vector3 origin = character.transform.position + Vector3.up * character.crouchColliderHeight;
        float distance = checkHeight - character.crouchColliderHeight;

        if (Physics.Raycast(origin, Vector3.up, out RaycastHit hit, distance, layerMask))
        {
            Debug.DrawRay(origin, Vector3.up * distance, Color.red);
            return true;
        }
        Debug.DrawRay(origin, Vector3.up * distance, Color.green);
        return false;
    }
}

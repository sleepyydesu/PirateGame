using UnityEngine;

public class SprintingState : State
{
    float gravityValue;
    Vector3 currentVelocity;

    bool grounded;
    bool sprint;
    float playerSpeed;
    bool sprintJump;
    Vector3 cVelocity;

    public SprintingState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public override void Enter()
    {
        base.Enter();

        sprint = false;
        sprintJump = false;
        input = Vector2.zero;
        velocity = Vector3.zero;
        gravityVelocity = Vector3.zero;
        gravityVelocity.y = 0;

        playerSpeed = character.sprintSpeed;
        grounded = character.controller.isGrounded;
        gravityValue = character.gravityValue;

        character.SetCombatAnimationLayers(character.isInCombat);
    }

    public override void HandleInput()
    {
        base.HandleInput();
        input = moveAction.ReadValue<Vector2>();
        velocity = new Vector3(input.x, 0, input.y);

        velocity = velocity.x * character.cameraTransform.right.normalized + velocity.z * character.cameraTransform.forward.normalized;
        velocity.y = 0f;

        sprint = sprintAction.IsPressed() && input.sqrMagnitude > 0f;

        if (jumpAction.triggered)
        {
            sprintJump = true;
        }
    }

    public override void LogicUpdate()
    {
        if (sprint)
        {
            UpdateSpeedParam(input.magnitude + 0.5f, character.speedDampTime);
        }
        else
        {
            stateMachine.ChangeState(character.isInCombat ? character.combatting : character.standing);
        }

        if (sprintJump)
        {
            stateMachine.ChangeState(character.sprintJumping);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();

        gravityVelocity.y = gravityValue * Time.deltaTime;
        grounded = character.controller.isGrounded;

        if (grounded && gravityVelocity.y < 0)
        {
            gravityVelocity.y = 0f;
        }

        currentVelocity = Vector3.SmoothDamp(currentVelocity, velocity, ref cVelocity, character.velocityDampTime);

        character.controller.Move(currentVelocity * Time.deltaTime * playerSpeed + gravityVelocity * Time.deltaTime);

        if (velocity.sqrMagnitude > 0)
        {
            character.transform.rotation = Quaternion.Slerp(character.transform.rotation, Quaternion.LookRotation(velocity), character.rotationDampTime);
        }
    }

}

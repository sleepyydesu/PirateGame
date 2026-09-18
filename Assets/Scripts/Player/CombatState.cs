using Unity.VisualScripting;
using UnityEngine;

public class CombatState : State
{
    float gravityValue;
    Vector3 currentVelociy;
    bool grounded;
    bool sheathWeapon;
    float playerSpeed;
    bool attack;
    bool roll;
    bool specialAttack;
    bool thrustAttack;
    bool specialAttack2;
    bool sprint;
    bool sheathing;
    bool movingSheath;
    bool sheathAnimationStarted;
    bool defend;

    Vector3 cVelocity;
    Vector3 currentVelocity;

    public CombatState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public override void Enter()
    {
        base.Enter();

        character.isInCombat = true;
        character.SetCombatAnimationLayers(true);

        sheathWeapon = false;
        sheathing = false;
        movingSheath = false;
        sheathAnimationStarted = false;
        input = Vector2.zero;
        currentVelociy = Vector3.zero;
        gravityVelocity.y = 0;

        attack = false;
        roll = false;
        specialAttack = false;
        specialAttack2 = false;
        thrustAttack = false;
        sprint = false;
        defend = false;

        velocity = character.playerVelocity;
        playerSpeed = character.playerSpeed;
        grounded = character.controller.isGrounded;
        gravityValue = character.gravityValue; 
    }

    public override void HandleInput()
    {
        base.HandleInput();

        if (drawWeaponAction.triggered)
        {
            sheathWeapon = true;
        }

        if (attackAction.triggered)
        {
            attack = true;
        }

        if (rollAction.triggered)
        {
            roll = true;
        }

        if (specialAttackAction.triggered)
        {
            specialAttack = true;
        }

        if (specialAttack2Action.triggered)
        {
            specialAttack2 = true;
        }

        if (thrustAction.triggered)
        {
            thrustAttack = true;
        }

        if (sprintAction.triggered)
        {
            sprint = true;
        }

        if (defendAction.triggered)
        {
            defend = true;
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

        if (sheathWeapon)
        {
            movingSheath = input.sqrMagnitude > 0.0001f;
            character.animator.SetTrigger(movingSheath ? "sheathWeaponMoving" : "sheathWeapon");
            sheathing = true;
            sheathAnimationStarted = false;
            sheathWeapon = false;
            return;
        }

        if (sheathing)
        {
            // Moving uses the arms-only layer so the combat locomotion blend tree
            // remains active. Idle sheathing continues to use the full-body layer.
            int sheathLayer = movingSheath ? 2 : 1;
            AnimatorStateInfo sheathLayerState = character.animator.GetCurrentAnimatorStateInfo(sheathLayer);
            bool isSheathAnim = sheathLayerState.IsName("PlayerSheath1") ||
                                sheathLayerState.IsName("PlayerSheath2");

            if (isSheathAnim)
                sheathAnimationStarted = true;
            else if (sheathAnimationStarted && !character.animator.IsInTransition(sheathLayer))
                stateMachine.ChangeState(character.standing);

            return;
        }

        if (defend)
        {
            stateMachine.ChangeState(character.defending);
            return;
        }

        if (specialAttack)
        {
            stateMachine.ChangeState(character.specialAttacking);
            return;
        }

        if (specialAttack2)
        {
            stateMachine.ChangeState(character.specialAttacking2);
            return;
        }

        if (thrustAttack)
        {
            stateMachine.ChangeState(character.thrustAttacking);
            return;
        }

        if (attack)
        {
            character.animator.SetTrigger("attack");
            stateMachine.ChangeState(character.attacking);
            return;
        }

        if (roll)
        {
            stateMachine.ChangeState(character.rolling);
            return;
        }

        if (sprint)
        {
            character.rolling.SetReturnState(character.combatting);
            stateMachine.ChangeState(character.sprinting);
            return;
        }

    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();

        gravityVelocity.y += gravityValue * Time.deltaTime;
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

    public override void Exit()
    {
        base.Exit();

        gravityVelocity.y = 0f;
        character.playerVelocity = new Vector3(input.x, 0, input.y);

        if (velocity.sqrMagnitude > 0)
        {
            character.transform.rotation = Quaternion.LookRotation(velocity);
        }
    }
}

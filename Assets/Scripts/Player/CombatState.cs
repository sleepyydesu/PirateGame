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
            character.animator.SetTrigger("sheathWeapon");
            sheathing = true;
            sheathWeapon = false;
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
            stateMachine.ChangeState(character.sprinting);
            return;
        }

        if (sheathing)
        {
            AnimatorStateInfo combatLayerState = character.animator.GetCurrentAnimatorStateInfo(1);
            bool isSheathAnim = combatLayerState.IsName("PlayerSheath1") || combatLayerState.IsName("PlayerSheath2");
            bool finished = !character.animator.IsInTransition(1) && !isSheathAnim;

            if (finished)
            {
                stateMachine.ChangeState(character.standing);
            }
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

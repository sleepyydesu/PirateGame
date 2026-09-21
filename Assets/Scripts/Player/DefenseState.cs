using System.Data.Common;
using PirateGame.Combat;
using Unity.VisualScripting;
using UnityEngine;

public class DefenseState : State
{
    Health playerHealth;
    float defenseMultiplier = 0.4f;
    State returnState;

    public DefenseState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public override void Enter()
    {
        base.Enter();

        if (playerHealth == null) playerHealth = character.GetComponent<Health>();
        if (playerHealth != null) playerHealth.damageMultiplier = defenseMultiplier;

        character.animator.SetBool("defense", true);
    }

    public override void HandleInput()
    {
        base.HandleInput();
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (!defendAction.IsPressed())
        {
            stateMachine.ChangeState(character.combatting);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
    }

    public override void Exit()
    {
        base.Exit();

        if (playerHealth != null)
        {
            playerHealth.damageMultiplier = 1f;
            character.animator.SetBool("defense", false);
        }
    }
}

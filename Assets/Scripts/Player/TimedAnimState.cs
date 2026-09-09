using UnityEngine;

public class TimedAnimState : State
{
    string enterTrigger;
    string exitTrigger;
    bool useRootMotion;

    float timePassed;
    float clipLength;
    float clipSpeed;

    public TimedAnimState(PlayerController _character, StateMachine _stateMachine,
        string _enterTrigger, string _exitTrigger, bool _useRootMotion = true)
        : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
        enterTrigger = _enterTrigger;
        exitTrigger = _exitTrigger;
        useRootMotion = _useRootMotion;
    }

    public override void Enter()
    {
        base.Enter();

        timePassed = 0f;
        if (useRootMotion) character.animator.applyRootMotion = true;
        character.animator.ResetTrigger(exitTrigger);
        character.animator.SetTrigger(enterTrigger);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();

        if (!useRootMotion)
        {
            character.controller.Move(character.transform.forward * character.playerSpeed * Time.deltaTime);
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        timePassed += Time.deltaTime;
        clipLength = character.animator.GetCurrentAnimatorClipInfo(1)[0].clip.length;
        clipSpeed = character.animator.GetCurrentAnimatorStateInfo(1).speed;

        if (timePassed >= clipLength / clipSpeed)
        {
            stateMachine.ChangeState(character.combatting);
            character.animator.SetTrigger(exitTrigger);
        }
    }

    public override void Exit()
    {
        base.Exit();
        if (useRootMotion) character.animator.applyRootMotion = false;
    }
}
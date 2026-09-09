using UnityEngine;

public class RollState : State
{
    float timePassed;
    float clipLength;
    float clipSpeed;

    public RollState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public override void Enter()
    {
        base.Enter();

        timePassed = 0f;
        character.animator.applyRootMotion = true;
        character.animator.ResetTrigger("rollFinished0");
        character.animator.SetTrigger("roll");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        timePassed += Time.deltaTime;
        if (character.animator.IsInTransition(1))
        {
            return;
        }

        AnimatorClipInfo[] clipInfo = character.animator.GetCurrentAnimatorClipInfo(1);
        if (clipInfo.Length == 0)
        {
            return;
        }

        clipLength = clipInfo[0].clip.length;
        clipSpeed = character.animator.GetCurrentAnimatorStateInfo(1).speed;

        if (clipSpeed > 0f && timePassed >= clipLength / clipSpeed)
        {
            stateMachine.ChangeState(character.combatting);
            character.animator.SetTrigger("rollFinished");
        }
    }

    public override void Exit()
    {
        base.Exit();
        character.animator.applyRootMotion = false;
    }
}
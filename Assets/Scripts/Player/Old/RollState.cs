using UnityEngine;

public class RollState : State
{
    float timePassed;
    float clipLength;
    float clipSpeed;
    State returnState;

    public RollState(PlayerController _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;
    }

    public void SetReturnState(State state)
    {
        returnState = state;
    }

    public override void Enter()
    {
        base.Enter();

        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        character.FaceRollDirection(moveInput);

        timePassed = 0f;
        character.animator.applyRootMotion = true;
        character.animator.ResetTrigger("rollFinished");
        character.animator.SetTrigger("roll");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        timePassed += Time.deltaTime;
        if (character.animator.IsInTransition(0)) return; // check Base Layer transition too now

        AnimatorClipInfo[] clipInfo = character.animator.GetCurrentAnimatorClipInfo(0); // read from Base Layer by default
        if (clipInfo.Length == 0) return;

        clipLength = clipInfo[0].clip.length;
        clipSpeed = character.animator.GetCurrentAnimatorStateInfo(0).speed;

        if (clipSpeed > 0f && timePassed >= clipLength / clipSpeed)
        {
            stateMachine.ChangeState(returnState ?? character.standing);
            character.animator.SetTrigger("rollFinished");
        }
    }

    public override void Exit()
    {
        base.Exit();
        character.animator.applyRootMotion = false;
    }
}
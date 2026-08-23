using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class State
{
    public PlayerController character;
    public StateMachine stateMachine;

    protected Vector3 gravityVelocity;
    protected Vector3 velocity;
    protected Vector2 input;

    public InputAction moveAction;
    public InputAction lookAction;
    public InputAction jumpAction;
    public InputAction crouchAction;
    public InputAction sprintAction;

    protected float currentSpeed;
    protected float speedVelocityRef;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public State(PlayerController _character, StateMachine _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;

        moveAction = character.playerInput.actions["Move"];
        lookAction = character.playerInput.actions["Look"];
        jumpAction = character.playerInput.actions["Jump"];
        crouchAction = character.playerInput.actions["Crouch"];
        sprintAction = character.playerInput.actions["Sprint"];
    }

    public virtual void Enter()
    {
        Debug.Log("Enter State: " + this.ToString());
    }

    public virtual void HandleInput()
    {

    }

    public virtual void LogicUpdate()
    {
       
    }

    public virtual void PhysicsUpdate()
    {
    }

    public virtual void Exit()
    {

    }

    protected void UpdateSpeedParam(float targetSpeed, float dampTime)
    {
        currentSpeed = Mathf.SmoothDamp(currentSpeed, targetSpeed, ref speedVelocityRef, dampTime);
        currentSpeed = Mathf.Max(0f, currentSpeed);
        character.animator.SetFloat("speed", currentSpeed);
    }
}

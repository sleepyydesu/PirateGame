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
    public InputAction drawWeaponAction;
    public InputAction attackAction;
    public InputAction rollAction;
    public InputAction specialAttackAction;
    public InputAction thrustAction;
    public InputAction specialAttack2Action;
    public InputAction defendAction;

    protected float currentSpeed;
    protected float speedVelocityRef;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public State(PlayerController _character, StateMachine _stateMachine)
    {
        character = _character;
        stateMachine = _stateMachine;

        InputActionMap playerMap = character.playerInput.actions.FindActionMap("Player");
        moveAction = SafeGetAction(playerMap, "Move");
        lookAction = SafeGetAction(playerMap, "Look");
        jumpAction = SafeGetAction(playerMap, "Jump");
        crouchAction = SafeGetAction(playerMap, "Crouch");
        sprintAction = SafeGetAction(playerMap, "Sprint");
        drawWeaponAction = SafeGetAction(playerMap, "DrawWeapon");
        attackAction = SafeGetAction(playerMap, "Attack");
        rollAction = SafeGetAction(playerMap, "Roll");
        specialAttackAction = SafeGetAction(playerMap, "SpecialAttack");
        thrustAction = SafeGetAction(playerMap, "ThrustAttack");
        specialAttack2Action = SafeGetAction(playerMap, "SpecialAttack2");
        defendAction = SafeGetAction(playerMap, "Defense");
    }

    private InputAction SafeGetAction(InputActionMap actionsMap, string name)
    {
        if (actionsMap == null) { Debug.LogWarning($"Player action map not found on player input asset."); return null; }
        InputAction foundAction = null;
        foreach (var action in actionsMap.actions)
        {
            if (action.name == name)
            {
                foundAction = action;
                break;
            }
        }
        if (foundAction == null)
        {
            Debug.LogWarning($"Input action '{name}' not found on player input asset.");
        }
        return foundAction;
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

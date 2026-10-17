using UnityEngine;
using UnityEngine.Events;
namespace PirateGame.PlayerV2
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(PlayerCombat))]
    [RequireComponent(typeof(PlayerAnimationDriver), typeof(PlayerWeaponBridge), typeof(PlayerAnimationEvents))]
    public sealed class PlayerControllerV2 : MonoBehaviour
    {
        public PlayerMotor Motor { get; private set; }
        public PlayerInputReader Input { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public UnityEvent<float> onLanded = new UnityEvent<float>();
        private PlayerAnimationDriver animationDriver;
        public PlayerStateMachine Locomotion { get; private set; }
        public PlayerGroundedState Grounded { get; private set; }
        public PlayerAirborneState Airborne { get; private set; }
        private bool ready, started, sprintJump;
        private void Start()
        {
            started = true; Motor = GetComponent<PlayerMotor>(); Input = GetComponent<PlayerInputReader>();
            Combat = GetComponent<PlayerCombat>(); animationDriver = GetComponent<PlayerAnimationDriver>();
            var weapon = GetComponent<PlayerWeaponBridge>(); weapon.Initialize();
            ready = Motor.Validate() && weapon.Ready && animationDriver.Validate() && Combat.Initialize();
            if (ready)
            {
                Locomotion = new PlayerStateMachine(); Grounded = new PlayerGroundedState(this); Airborne = new PlayerAirborneState(this);
                Locomotion.Initialize(Airborne);
            }
            if (!ready) { Debug.LogError("PlayerV2 setup incomplete. Follow the migration guide.", this); enabled = false; }
        }
        private void Update()
        {
            if (!ready) return;
            float dt = Time.deltaTime;
            Motor.Probe(Input.Move);
            if (Motor.JustLanded) { animationDriver.Landing(Motor.LastImpactSpeed); onLanded.Invoke(Motor.LastImpactSpeed); }
            Combat.Tick(dt);
            if (Combat.Kind == PlayerActionKind.Dead) return;
            Locomotion.Tick(dt);
            // Dodge velocity integrates only its remaining travel budget, even on a slow frame.
            Motor.Step(dt, Input.Move, Combat.IsFree && Input.Sprint, Combat.MovementScale,
                Combat.RotateWithMovement, Combat.ScriptedMotion, Combat.ConsumeActionVelocity(dt), Combat.RootTranslation);
            if (Motor.Grounded) sprintJump = false;
            float speed = Motor.HorizontalVelocity.magnitude / Mathf.Max(0.1f, Motor.runSpeed);
            animationDriver.Visuals(Combat.Weapon.Drawn, !Motor.Grounded, Motor.Crouched, sprintJump,
                Combat.Kind == PlayerActionKind.Dodge, Combat.VisualLayer, speed, dt);
        }
        public void ProcessMovementCommands()
        {
            if (Combat.IsFree)
            {
                if (Motor.Grounded && Input.Has(PlayerCommand.Crouch))
                { Input.Consume(PlayerCommand.Crouch); Motor.TryCrouch(!Motor.Crouched); }
                if (Input.Has(PlayerCommand.Jump) && Motor.CanJump)
                {
                    sprintJump = Input.Sprint;
                    if (Motor.Jump()) Input.Consume(PlayerCommand.Jump);
                }
            }
        }
        private void OnDisable()
        {
            if (!started || !ready) return;
            Combat.Shutdown(); ready = false;
        }
        // This foundation's component-disable path is terminal. Use a fresh scene/prefab after disabling.
        // Health.Revive is supported while components stay enabled; external respawn restores presentation/position.
    }
}

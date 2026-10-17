namespace PirateGame.PlayerV2
{
    // Orthogonal to the action FSM: airborne movement and gravity never disappear during an action.
    public sealed class PlayerGroundedState : PlayerState
    {
        private readonly PlayerControllerV2 p;
        public PlayerGroundedState(PlayerControllerV2 player) { p = player; }
        public override void Tick(float dt)
        {
            p.ProcessMovementCommands();
            if (!p.Motor.Grounded) p.Locomotion.Request(p.Airborne, 10);
        }
    }
    public sealed class PlayerAirborneState : PlayerState
    {
        private readonly PlayerControllerV2 p;
        public PlayerAirborneState(PlayerControllerV2 player) { p = player; }
        public override void Tick(float dt)
        {
            p.ProcessMovementCommands(); // Coyote jump and landing buffer still use the same motor.
            if (p.Motor.Grounded) p.Locomotion.Request(p.Grounded, 10);
        }
    }
}

namespace PirateGame.PlayerV2
{
    public abstract class PlayerActionState : PlayerState
    {
        protected readonly PlayerCombat c;
        private readonly PlayerActionKind kind;
        protected PlayerActionState(PlayerCombat combat, PlayerActionKind action) { c = combat; kind = action; }
        public override void Enter() { c.Enter(kind); }
        public override void Exit() { c.Cleanup(); }
    }
    public sealed class PlayerFreeState : PlayerActionState
    {
        public PlayerFreeState(PlayerCombat c) : base(c, PlayerActionKind.Free) { }
        public override void Tick(float dt) { c.SelectFreeAction(); }
    }
    public sealed class PlayerAttackState : PlayerActionState
    {
        public PlayerAttackDefinition Definition { get; }
        public PlayerAttackState(PlayerCombat c, PlayerAttackDefinition definition) : base(c, PlayerActionKind.Attack) { Definition = definition; }
        public override void Enter() { base.Enter(); c.BeginAttack(this); }
        public override void Tick(float dt) { c.TickAttack(dt); }
    }
    public sealed class PlayerDodgeState : PlayerActionState
    {
        public PlayerDodgeState(PlayerCombat c) : base(c, PlayerActionKind.Dodge) { }
        public override void Enter() { base.Enter(); c.BeginDodge(); }
        public override void Tick(float dt) { c.TickDodge(); }
    }
    public sealed class PlayerBlockState : PlayerActionState
    {
        public PlayerBlockState(PlayerCombat c) : base(c, PlayerActionKind.Block) { }
        public override void Enter() { base.Enter(); c.BeginBlock(); }
        public override void Tick(float dt) { c.TickBlock(); }
    }
    public sealed class PlayerEquipState : PlayerActionState
    {
        public PlayerEquipState(PlayerCombat c) : base(c, PlayerActionKind.Equip) { }
        public override void Enter() { base.Enter(); c.BeginEquip(); }
        public override void Tick(float dt) { c.TickEquip(); }
    }
    public sealed class PlayerStaggerState : PlayerActionState
    {
        public PlayerStaggerState(PlayerCombat c) : base(c, PlayerActionKind.Stagger) { }
        public override void Enter() { base.Enter(); c.BeginStagger(); }
        public override void Tick(float dt) { c.TickStagger(); }
    }
    public sealed class PlayerDeadState : PlayerActionState
    {
        public PlayerDeadState(PlayerCombat c) : base(c, PlayerActionKind.Dead) { }
        public override void Enter() { base.Enter(); c.BeginDead(); }
        public override void Tick(float dt) { }
    }
}

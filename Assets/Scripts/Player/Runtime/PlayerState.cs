namespace PirateGame.PlayerV2
{
    // Pure lifecycle API. Movement and animation live in services, not in this base class.
    public abstract class PlayerState
    {
        public virtual void Enter() { }
        public abstract void Tick(float deltaTime);
        public virtual void Exit() { }
    }
}

using System;
namespace PirateGame.PlayerV2
{
    // Requests are committed after Tick. One winner, highest priority, first request wins ties.
    // Enter/Exit may queue future transitions but cannot recursively perform one.
    public sealed class PlayerStateMachine
    {
        public PlayerState Current { get; private set; }
        private PlayerState pending;
        private int priority = int.MinValue;
        private bool changing;
        public void Initialize(PlayerState state)
        {
            if (Current != null) throw new InvalidOperationException("Already initialized.");
            Change(state);
        }
        public void Request(PlayerState state, int requestPriority)
        {
            if (state == null || state == Current || requestPriority <= priority) return;
            pending = state; priority = requestPriority;
        }
        public void Tick(float dt)
        {
            Current?.Tick(dt);
            var next = pending; pending = null; priority = int.MinValue;
            if (next != null) Change(next);
        }
        // Only terminal lifecycle operations use an immediate transition.
        public void Force(PlayerState state)
        {
            pending = null; priority = int.MinValue;
            if (Current != state) Change(state);
        }
        private void Change(PlayerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (changing) throw new InvalidOperationException("Recursive transition.");
            changing = true;
            try { Current?.Exit(); Current = state; Current.Enter(); }
            finally { changing = false; }
        }
    }
}

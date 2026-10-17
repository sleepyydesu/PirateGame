using UnityEngine;
namespace PirateGame.PlayerV2
{
    // Add to each action state. The editor migration utility installs this automatically.
    public sealed class PlayerActionAnimationState : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo state, int layer)
        { var d = animator.GetComponent<PlayerAnimationDriver>(); if (d != null) d.StateEntered(layer, state.fullPathHash); }
        public override void OnStateExit(Animator animator, AnimatorStateInfo state, int layer)
        { var d = animator.GetComponent<PlayerAnimationDriver>(); if (d != null) d.StateExited(layer, state.fullPathHash); }
    }
}

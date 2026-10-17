using UnityEngine;

namespace PirateGame.Enemies
{
    // Attach this behaviour to every attack, roar, taunt and stagger Animator state.
    // Do not put it on locomotion or a blend tree; action states must be unique.
    public sealed class BossActionState : StateMachineBehaviour
    {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layerIndex)
        {
            if (layerIndex != 0) return;
            var relay = animator.GetComponent<BossAnimationRelay>();
            if (relay != null) relay.StateEntered(info.fullPathHash);
        }
        public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layerIndex)
        {
            if (layerIndex != 0) return;
            var relay = animator.GetComponent<BossAnimationRelay>();
            if (relay != null) relay.StateExited(info.fullPathHash);
        }
    }
}

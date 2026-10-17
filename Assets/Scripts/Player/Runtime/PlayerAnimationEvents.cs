using UnityEngine;
namespace PirateGame.PlayerV2
{
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimationEvents : MonoBehaviour
    {
        private PlayerAnimationDriver driver;
        private PlayerCombat combat;
        private void Awake() { driver = GetComponent<PlayerAnimationDriver>(); combat = GetComponent<PlayerCombat>(); }
        private bool Accept(AnimationEvent e) => combat != null && combat.Ready && combat.isActiveAndEnabled && driver.Accept(e);
        public void AE_Commit(AnimationEvent e) { if (Accept(e)) combat.EventCommit(); }
        public void AE_HitboxOn(AnimationEvent e) { if (Accept(e)) combat.EventHitOn(); }
        public void AE_HitboxOff(AnimationEvent e) { if (Accept(e)) combat.EventHitOff(); }
        public void AE_ComboOpen(AnimationEvent e) { if (Accept(e)) combat.EventCombo(true); }
        public void AE_ComboClose(AnimationEvent e) { if (Accept(e)) combat.EventCombo(false); }
        public void AE_Chain(AnimationEvent e) { if (Accept(e)) combat.EventBranch(); }
        public void AE_CancelOpen(AnimationEvent e) { if (Accept(e)) combat.EventCancel(true); }
        public void AE_CancelClose(AnimationEvent e) { if (Accept(e)) combat.EventCancel(false); }
        public void AE_ActionComplete(AnimationEvent e) { if (Accept(e)) combat.EventComplete(); }
        public void AE_Equip(AnimationEvent e) { if (Accept(e)) combat.EventEquip(); }
        public void AE_InvulnerableOn(AnimationEvent e) { if (Accept(e)) combat.EventInvulnerable(true); }
        public void AE_InvulnerableOff(AnimationEvent e) { if (Accept(e)) combat.EventInvulnerable(false); }
        private void OnAnimatorMove()
        {
            if (combat == null || !combat.Ready || !combat.RootTranslation || !combat.isActiveAndEnabled) return;
            combat.Motor.ApplyRootTranslation(GetComponent<Animator>().deltaPosition);
        }
    }
}

using UnityEngine;

namespace PirateGame.Enemies
{
    [DefaultExecutionOrder(-100)]
    // Animation Events are received on the Animator GameObject, often a child of the AI.
    [RequireComponent(typeof(Animator))]
    public sealed class BossAnimationRelay : MonoBehaviour
    {
        private BossAI boss;
        private Animator animator;
        [Header("Landing pose correction")]
        [SerializeField] private Transform landingHips;
        [SerializeField] private Transform landingLeftFoot;
        [SerializeField] private Transform landingRightFoot;

        [Tooltip("Only layers containing solid ground.")]
        [SerializeField] private LayerMask landingGround;

        [Range(0.01f, 0.3f)]
        [SerializeField] private float landingBlendWindow = 0.12f;

        [Min(0.01f)]
        [SerializeField] private float landingFadeOutSeconds = 0.2f;

        [Min(0.01f)]
        [SerializeField] private float maximumLandingCorrection = 1.5f;

        private bool landingHeightCalibrated;
        private float standingFootHeight;
        private float landingWeight;
        private Vector3 appliedHipsOffsetLocal;
        private void Awake() { boss = GetComponentInParent<BossAI>(); animator = GetComponent<Animator>(); }
        public void StateEntered(int hash) { if (boss != null) boss.AnimationStateEntered(hash); }
        public void StateExited(int hash) { if (boss != null) boss.AnimationStateExited(hash); }
        // Accept only events from the expected, entered state. Outgoing blended clips
        // cannot open hitboxes or complete the next action after an interruption.
        private bool Accept(AnimationEvent e)
        {
            return boss != null && e.isFiredByAnimator &&
                boss.AcceptAnimationEvent(e.animatorStateInfo.fullPathHash);
        }
        public void AE_PrepareStrike(AnimationEvent e)
        {
            if (Accept(e))
                boss.PrepareStrike();
        }

        public void AE_AttackStepOn(AnimationEvent e)
        {
            if (Accept(e))
                boss.SetAttackStep(true);
        }

        public void AE_AttackStepOff(AnimationEvent e)
        {
            if (Accept(e))
                boss.SetAttackStep(false);
        }
        public void AE_JumpTakeoff(AnimationEvent e)
        {
            bool accepted = Accept(e);

            Debug.Log(
                $"Jump takeoff event received. Accepted: {accepted}",
                this);

            if (accepted)
                boss.BeginJump(e.animatorStateInfo.normalizedTime);
        }

        public void AE_JumpLand(AnimationEvent e)
        {
            if (Accept(e))
                boss.LandJump();
        }
        public void AE_Commit(AnimationEvent e) { if (Accept(e)) boss.CommitAttack(); }
        public void AE_HitboxOn(AnimationEvent e) { if (Accept(e)) boss.OpenHitbox(); }
        public void AE_HitboxOff(AnimationEvent e) { if (Accept(e)) boss.CloseHitbox(); }
        public void AE_ParryOn(AnimationEvent e) { if (Accept(e)) boss.SetParryWindow(true); }
        public void AE_ParryOff(AnimationEvent e) { if (Accept(e)) boss.SetParryWindow(false); }
        public void AE_ChainDecision(AnimationEvent e) { if (Accept(e)) boss.DecideChain(); }
        public void AE_ActionComplete(AnimationEvent e) { if (Accept(e)) boss.CompleteAction(); }
        private void Update()
        {
            RemoveLandingOffset();
        }

        private void RemoveLandingOffset()
        {
            if (landingHips != null)
                landingHips.localPosition -= appliedHipsOffsetLocal;

            appliedHipsOffsetLocal = Vector3.zero;
        }

        private bool TryGetLandingGround(out RaycastHit hit)
        {
            Vector3 origin =
                boss.transform.position + Vector3.up;

            return Physics.Raycast(
                origin,
                Vector3.down,
                out hit,
                12f,
                landingGround,
                QueryTriggerInteraction.Ignore);
        }

        private float LowestFootY()
        {
            return Mathf.Min(
                landingLeftFoot.position.y,
                landingRightFoot.position.y);
        }

        [ContextMenu("Calibrate Standing Height")]
        public void CalibrateStandingHeight()
        {
            if (!Application.isPlaying ||
                boss == null ||
                landingHips == null ||
                landingLeftFoot == null ||
                landingRightFoot == null)
            {
                Debug.LogWarning(
                    "Enter Play mode and assign the landing bones first.",
                    this);
                return;
            }

            if (boss.CurrentState != BossAI.State.Idle ||
                animator.IsInTransition(0))
            {
                Debug.LogWarning(
                    "Calibrate while the boss is standing idle.",
                    this);
                return;
            }

            RemoveLandingOffset();

            if (!TryGetLandingGround(out RaycastHit ground))
            {
                Debug.LogWarning(
                    "Landing correction could not find ground. " +
                    "Check Landing Ground layers.",
                    this);
                return;
            }

            // Foot bones are usually near the ankles, not at the shoe soles.
            // Remember their normal height above the ground.
            standingFootHeight = LowestFootY() - ground.point.y;

            landingHeightCalibrated = true;

            Debug.Log(
                "Boss landing height calibrated.",
                this);
        }

        private void LateUpdate()
        {
            if (boss == null ||
                animator == null ||
                !animator.enabled ||
                landingHips == null ||
                landingLeftFoot == null ||
                landingRightFoot == null)
            {
                return;
            }

            if (!landingHeightCalibrated)
            {
                // Capture the normal standing pose before the fight starts.
                if (boss.CurrentState == BossAI.State.Idle &&
                    !animator.IsInTransition(0))
                {
                    CalibrateStandingHeight();
                }

                return;
            }

            if (boss.CurrentState == BossAI.State.Dead)
            {
                landingWeight = 0f;
                return;
            }

            float requestedWeight =
                boss.GetJumpLandingCorrectionWeight(landingBlendWindow);

            if (requestedWeight > 0f)
            {
                landingWeight = requestedWeight;
            }
            else
            {
                landingWeight = Mathf.MoveTowards(
                    landingWeight,
                    0f,
                    Time.deltaTime /
                    Mathf.Max(0.01f, landingFadeOutSeconds));
            }

            if (landingWeight <= 0f ||
                !TryGetLandingGround(out RaycastHit ground))
            {
                return;
            }

            float desiredFootY =
                ground.point.y + standingFootHeight;

            float correction = Mathf.Clamp(
                desiredFootY - LowestFootY(),
                -maximumLandingCorrection,
                maximumLandingCorrection);

            Vector3 previousLocalPosition =
                landingHips.localPosition;

            landingHips.position +=
                Vector3.up * correction * landingWeight;

            appliedHipsOffsetLocal =
                landingHips.localPosition - previousLocalPosition;
        }

        private void OnDisable()
        {
            RemoveLandingOffset();
            landingWeight = 0f;
        }
        private void OnAnimatorMove()
        {
            if (boss != null) boss.ApplyRootMotion(animator.deltaPosition);
            // Root rotation is baked into pose. Code owns bounded facing before commitment.
        }
        public void AE_FireProjectile(AnimationEvent e)
        {
            if (Accept(e))
                boss.QueueAirSlash();
        }
    }
}

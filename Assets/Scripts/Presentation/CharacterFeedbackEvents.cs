using System.Collections.Generic;
using UnityEngine;
using PirateGame.Enemies;
using PirateGame.PlayerV2;

namespace PirateGame.Presentation
{
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class CharacterFeedbackEvents : MonoBehaviour
    {
        public CharacterFeedback feedback;
        public Transform leftFoot, rightFoot;
        [Tooltip("Ground only. Exclude characters, weapons, and triggers.")]
        public LayerMask ground;
        public string defaultFootstep = "Footstep_Dirt";
        [Tooltip("Player: 0 and 1. Boss with one layer: 0 only. Exclude upper-body Arms/Damage layers.")]
        public int[] footstepLayers = { 0, 1 };
        public bool requireMovement = true;
        [Min(0f)] public float minimumSpeed = .1f;
        [Min(0f)] public float rayStartHeight = .2f;
        [Min(.05f)] public float rayDistance = .65f;
        [Min(0f)] public float footstepCooldown = .08f;
        private Animator animator;
        private PlayerCombat player;
        private BossAI boss;
        private Vector3 previousPosition;
        private float speed, lastStep = float.NegativeInfinity;
        private readonly List<AnimatorClipInfo> clipInfos = new List<AnimatorClipInfo>(8);

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (feedback == null) feedback = GetComponentInParent<CharacterFeedback>();
            player = GetComponentInParent<PlayerCombat>();
            boss = GetComponentInParent<BossAI>();
            if (feedback == null) Debug.LogWarning("Assign CharacterFeedback to CharacterFeedbackEvents.", this);
        }
        private void OnEnable()
        {
            previousPosition = feedback != null ? feedback.transform.position : transform.position;
            speed = 0f; lastStep = float.NegativeInfinity;
        }
        private void LateUpdate()
        {
            Vector3 position = feedback != null ? feedback.transform.position : transform.position;
            Vector3 delta = position - previousPosition; delta.y = 0f;
            speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            previousPosition = position;
        }
        public void AE_SFX(AnimationEvent e)
        {
            if (!CanReceive(e) || string.IsNullOrEmpty(e.stringParameter)) return;
            bool accepted;
            if (boss != null)
            {
                accepted = boss.AcceptAnimationEvent(e.animatorStateInfo.fullPathHash) ||
                    ((boss.CurrentState == BossAI.State.Idle || boss.CurrentState == BossAI.State.Chase ||
                      boss.CurrentState == BossAI.State.Circle) && DominantLocomotionEvent(e));
            }
            else if (player != null)
            {
                accepted = player.Ready && player.Kind != PlayerActionKind.Dead &&
                    ((player.Animation != null && player.Animation.Accept(e)) ||
                     (player.IsFree && DominantLocomotionEvent(e)));
            }
            else accepted = DominantLocomotionEvent(e);
            if (accepted) feedback.Play(e.stringParameter);
        }
        public void AE_Footstep(AnimationEvent e)
        {
            if (!CanReceive(e) || !DominantLocomotionEvent(e)) return;
            if (player != null && (!player.Ready || player.Kind == PlayerActionKind.Dead)) return;
            if (boss != null && boss.CurrentState == BossAI.State.Dead) return;
            if (requireMovement && speed < minimumSpeed) return;
            if (Time.unscaledTime - lastStep < footstepCooldown) return;
            Transform foot = e.intParameter == 1 ? rightFoot : leftFoot;
            Vector3 point = foot != null ? foot.position : feedback.transform.position;
            if (!Physics.Raycast(point + Vector3.up * rayStartHeight, Vector3.down,
                out RaycastHit hit, rayDistance, ground, QueryTriggerInteraction.Ignore)) return;
            lastStep = Time.unscaledTime;
            FootstepSurface surface = hit.collider.GetComponentInParent<FootstepSurface>();
            string cue = surface != null ? surface.CueAt(hit.point) : defaultFootstep;
            feedback.PlayAt(cue, hit.point, hit.normal);
        }
        private bool CanReceive(AnimationEvent e)
        {
            return isActiveAndEnabled && feedback != null && feedback.isActiveAndEnabled &&
                animator != null && e != null && e.isFiredByAnimator;
        }
        private bool DominantLocomotionEvent(AnimationEvent e)
        {
            int chosen = -1;
            foreach (int layer in footstepLayers ?? new int[0])
                if (layer >= 0 && layer < animator.layerCount &&
                    (layer == 0 || animator.GetLayerWeight(layer) > .5f) && layer > chosen)
                    chosen = layer;
            if (chosen < 0) return false;
            bool blending = animator.IsInTransition(chosen);
            AnimatorStateInfo state = blending ? animator.GetNextAnimatorStateInfo(chosen) :
                animator.GetCurrentAnimatorStateInfo(chosen);
            if (e.animatorStateInfo.fullPathHash != state.fullPathHash) return false;
            clipInfos.Clear();
            if (blending) animator.GetNextAnimatorClipInfo(chosen, clipInfos);
            else animator.GetCurrentAnimatorClipInfo(chosen, clipInfos);
            AnimationClip dominant = null; float weight = -1f;
            foreach (AnimatorClipInfo info in clipInfos)
                if (info.clip != null && info.weight > weight) { weight = info.weight; dominant = info.clip; }
            return dominant != null && e.animatorClipInfo.clip == dominant;
        }
    }
}

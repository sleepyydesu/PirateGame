using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Thin wrapper around an NPC's Animator (params: Talk, Thank, Freed triggers; Restrained bool)
    /// plus smooth turning towards the player during dialogue. Missing params are ignored so
    /// the same component works with any controller.
    /// </summary>
    public class NpcAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private bool restrained;
        [SerializeField] private float turnSpeed = 6f;
        [SerializeField] private bool canTurn = true;

        private static readonly int TalkId = Animator.StringToHash("Talk");
        private static readonly int ThankId = Animator.StringToHash("Thank");
        private static readonly int FreedId = Animator.StringToHash("Freed");
        private static readonly int RestrainedId = Animator.StringToHash("Restrained");

        private Quaternion? lookTarget;

        public bool Restrained => restrained;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnEnable() => ApplyRestrained();

        public void SetRestrained(bool value)
        {
            restrained = value;
            canTurn = !value;
            ApplyRestrained();
        }

        private void ApplyRestrained()
        {
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetBool(RestrainedId, restrained);
        }

        public void Play(NpcGesture gesture)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            switch (gesture)
            {
                case NpcGesture.Talk: animator.SetTrigger(TalkId); break;
                case NpcGesture.Thank: animator.SetTrigger(ThankId); break;
                case NpcGesture.Freed: animator.SetTrigger(FreedId); break;
            }
        }

        public void FaceTowards(Vector3 worldPos)
        {
            if (!canTurn) return;
            Vector3 d = worldPos - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) lookTarget = Quaternion.LookRotation(d);
        }

        private void Update()
        {
            if (lookTarget.HasValue)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, lookTarget.Value, Time.deltaTime * turnSpeed);
                if (Quaternion.Angle(transform.rotation, lookTarget.Value) < 1f) lookTarget = null;
            }
        }
    }
}

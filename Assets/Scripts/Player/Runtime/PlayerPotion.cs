using UnityEngine;
using UnityEngine.Events;
using PirateGame.Combat;

namespace PirateGame.PlayerV2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class PlayerPotion : MonoBehaviour
    {
        [Header("Charges and healing")]
        [SerializeField, Range(1, 10)] private int maximumCharges = 3;
        [SerializeField, Min(1f)] private float healAmount = 35f;
        [SerializeField] private int remainingCharges;

        [Header("Animation")]
        public string fullStatePath = "Arms Layer.Player_DrinkPotion";
        [Min(0.2f)] public float watchdogSeconds = 4f;
        [Range(0f, 1f)] public float movementScale = 1f;

        [Header("Bottle already attached to the drinking hand")]
        [SerializeField] private GameObject heldBottle;
        [SerializeField] private GameObject beltBottle;

        [Header("Healing VFX")]
        [SerializeField] private GameObject healingVfx;
        [SerializeField] private Transform vfxAnchor;
        [SerializeField, Min(0.1f)] private float vfxLifetime = 3f;

        [Header("Optional sounds, UI, and boss notification")]
        public UnityEvent onDrinkStarted = new UnityEvent();
        public UnityEvent onSip = new UnityEvent();
        public UnityEvent onChargesChanged = new UnityEvent();

        private Health health;
        private bool drinking, sipCommitted, startNotified;

        public int RemainingCharges => remainingCharges;
        public int MaximumCharges => maximumCharges;
        public bool SipCommitted => sipCommitted;
        public bool IsDrinking => drinking;
        public bool CanUse => isActiveAndEnabled && health != null &&
            !health.IsDead && remainingCharges > 0 &&
            health.CurrentHealth < health.MaxHealth &&
            healAmount > 0f && !float.IsNaN(healAmount) &&
            !float.IsInfinity(healAmount);

        private void Awake()
        {
            health = GetComponent<Health>();
            maximumCharges = Mathf.Max(1, maximumCharges);
            remainingCharges = maximumCharges;
            EndDrink();
        }
        public void GrabBottle()
        {
            if (!drinking) return;

            // The hand reaches the hip: hide the hip copy.
            if (beltBottle != null)
                beltBottle.SetActive(false);

            // Show the copy attached to the hand.
            if (heldBottle != null)
                heldBottle.SetActive(true);
        }

        public void PutAwayBottle()
        {
            if (heldBottle != null)
                heldBottle.SetActive(false);

            if (beltBottle != null)
                beltBottle.SetActive(true);
        }

        public void EndDrink()
        {
            drinking = false;

            // Also restore the hip bottle if drinking gets interrupted.
            PutAwayBottle();
        }

        public bool BeginDrink()
        {
            if (drinking || !CanUse) return false;
            drinking = true;
            sipCommitted = startNotified = false;
            return true;
        }

        public void ShowWhenAnimationEntered()
        {
            if (!drinking || startNotified) return;
            startNotified = true;
            onDrinkStarted.Invoke();
        }

        public bool TrySip()
        {
            if (!drinking || sipCommitted || !CanUse) return false;
            ShowWhenAnimationEntered();
            // A start-event listener may have interrupted drinking.
            if (!drinking || !CanUse) return false;

            sipCommitted = true;
            remainingCharges--;
            health.Heal(healAmount);

            if (healingVfx != null)
            {
                Transform anchor = vfxAnchor != null ? vfxAnchor : transform;
                GameObject effect = Instantiate(
                    healingVfx, anchor.position, anchor.rotation, anchor);

                foreach (ParticleSystem particles in
                    effect.GetComponentsInChildren<ParticleSystem>())
                {
                    Transform parent = particles.transform.parent;
                    if (parent == null ||
                        parent.GetComponentInParent<ParticleSystem>() == null)
                        particles.Play(true);
                }

                Destroy(effect, Mathf.Max(0.1f, vfxLifetime));
            }

            onChargesChanged.Invoke();
            onSip.Invoke();
            return true;
        }

        public void Refill()
        {
            remainingCharges = maximumCharges;
            onChargesChanged.Invoke();
        }

        private void OnDisable() { EndDrink(); }
    }

    public sealed class PlayerHealState : PlayerActionState
    {
        public PlayerHealState(PlayerCombat combat)
            : base(combat, PlayerActionKind.Heal) { }

        public override void Enter()
        {
            base.Enter();
            c.BeginHeal();
        }

        public override void Tick(float dt) { c.TickHeal(); }
    }
}

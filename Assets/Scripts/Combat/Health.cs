using System;
using UnityEngine;
using PirateGame.Progression;

namespace PirateGame.Combat
{
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsDead { get; }
    }

    public struct DamageInfo
    {
        public float Amount;
        public GameObject Attacker;
        public Vector3 HitPoint;
        public bool WasBlocked;
        public bool HasImpactOrigin;
        public Vector3 ImpactOrigin;

        public Vector3 KnockbackDirection;
        public float KnockbackDistance;
        public float KnockbackSeconds;

        // A sword hit can push less when blocked.
        // False keeps old callers compatible.
        public bool HasBlockedKnockback;
        public float BlockedKnockbackDistance;
        public float BlockedKnockbackSeconds;

        public HitReactionType Reaction;
        public HitReactionMode ReactionMode;

        public DamageInfo(
            float amount,
            GameObject attacker,
            Vector3 hitPoint = default,
            HitReactionType reaction = HitReactionType.Default,
            HitReactionMode reactionMode = HitReactionMode.Stagger)
        {
            Amount = amount;
            Attacker = attacker;
            HitPoint = hitPoint;
            WasBlocked = false;
            HasImpactOrigin = false;
            ImpactOrigin = Vector3.zero;

            KnockbackDirection = Vector3.zero;
            KnockbackDistance = 0f;
            KnockbackSeconds = 0f;

            HasBlockedKnockback = false;
            BlockedKnockbackDistance = 0f;
            BlockedKnockbackSeconds = 0f;

            Reaction = reaction;
            ReactionMode = reactionMode;
        }
    }

    // Resolves incoming defence before Health changes HP.
    public interface IIncomingDamageFilter
    {
        bool FilterIncoming(ref DamageInfo info);
    }

    /// <summary>
    /// The player's/enemy's health receiver.
    /// Supports damage, healing, revival, and progression health bonuses.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable, ILevelUpHealth
    {
        [SerializeField, Min(1f)]
        private float maxHealth = 100f;

        [SerializeField]
        private float currentHealth;

        public float damageMultiplier = 1f;

        public float MaxHealth => maxHealth;

        public float CurrentHealth { get; private set; }

        public bool IsDead => CurrentHealth <= 0f;

        // Existing events retained.
        public event Action<DamageInfo> OnDamaged;
        public event Action OnDeath;
        public event Action OnRevived;

        // New event: arguments are current health and maximum health.
        public event Action<float, float> OnHealthChanged;

        private IIncomingDamageFilter filter;
        private bool resolving;

        private void Awake()
        {
            maxHealth = Mathf.Max(1f, maxHealth);

            CurrentHealth = maxHealth;

            Sync();
        }

        public void InstallDamageFilter(IIncomingDamageFilter next)
        {
            if (filter != null && !ReferenceEquals(filter, next))
            {
                throw new InvalidOperationException(
                    "Health already has an incoming damage filter.");
            }

            filter = next;
        }

        public void RemoveDamageFilter(IIncomingDamageFilter previous)
        {
            if (ReferenceEquals(filter, previous))
            {
                filter = null;
            }
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || resolving || !FinitePositive(info.Amount))
            {
                return;
            }

            resolving = true;

            try
            {
                // Allow blocking or other defence logic to modify damage.
                if (filter != null && !filter.FilterIncoming(ref info))
                {
                    return;
                }

                float amount =
                    info.Amount * Mathf.Max(0f, damageMultiplier);

                if (!FinitePositive(amount))
                {
                    return;
                }

                float before = CurrentHealth;

                CurrentHealth = Mathf.Max(
                    0f,
                    CurrentHealth - amount
                );

                Sync();

                // Report the actual health lost, clamped to remaining HP.
                info.Amount = before - CurrentHealth;

                OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

                bool killed = IsDead;

                OnDamaged?.Invoke(info);

                if (killed)
                {
                    OnDeath?.Invoke();
                }
            }
            finally
            {
                resolving = false;
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || !FinitePositive(amount))
            {
                return;
            }

            float before = CurrentHealth;

            CurrentHealth = Mathf.Min(
                maxHealth,
                CurrentHealth + amount
            );

            Sync();

            if (!Mathf.Approximately(before, CurrentHealth))
            {
                OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
            }
        }

        public void Revive(float amount = -1f)
        {
            if (resolving)
            {
                throw new InvalidOperationException(
                    "Revive after the damage notification finishes.");
            }

            CurrentHealth =
                amount <= 0f || !FinitePositive(amount)
                    ? maxHealth
                    : Mathf.Min(maxHealth, amount);

            Sync();

            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

            OnRevived?.Invoke();
        }

        /// <summary>
        /// Called by PlayerProgression when the player levels up.
        /// Increases maximum health and fully restores current health.
        /// </summary>
        public void IncreaseMaxHealthAndRestore(float amount)
        {
            // Prevent invalid or negative values from corrupting health.
            if (float.IsNaN(amount)
                || float.IsInfinity(amount)
                || amount < 0f)
            {
                Debug.LogWarning(
                    $"Health received an invalid max-health increase: {amount}.",
                    this
                );

                return;
            }

            float newMaxHealth = maxHealth + amount;

            if (!FinitePositive(newMaxHealth))
            {
                Debug.LogWarning(
                    "Health could not apply the max-health increase because the result was invalid.",
                    this
                );

                return;
            }

            // Increase maximum health.
            maxHealth = newMaxHealth;

            // Restore the player to full health.
            CurrentHealth = maxHealth;

            Sync();

            // Notify health-bar listeners immediately.
            OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        private static bool FinitePositive(float value)
        {
            return value > 0f
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }

        // Keeps the serialized backing field synchronized.
        private void Sync()
        {
            currentHealth = CurrentHealth;
        }
    }
}
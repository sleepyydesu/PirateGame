using System;
using UnityEngine;
namespace PirateGame.Combat
{
    public interface IDamageable { void TakeDamage(DamageInfo info); bool IsDead { get; } }
    public struct DamageInfo
    {
        public float Amount;
        public GameObject Attacker;
        public Vector3 HitPoint;
        public bool WasBlocked;

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

            Reaction = reaction;
            ReactionMode = reactionMode;
        }
    }
    // Health remains the ONE receiver. A filter resolves defence before HP mutation.
    public interface IIncomingDamageFilter { bool FilterIncoming(ref DamageInfo info); }
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;
        public float damageMultiplier = 1f;
        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead => CurrentHealth <= 0f;
        // Existing signature retained. Amount now reports actual HP lost, including reduction/overkill clamp.
        public event Action<DamageInfo> OnDamaged;
        public event Action OnDeath;
        public event Action OnRevived;
        private IIncomingDamageFilter filter;
        private bool resolving;
        private void Awake() { maxHealth = Mathf.Max(1f, maxHealth); CurrentHealth = maxHealth; Sync(); }
        public void InstallDamageFilter(IIncomingDamageFilter next)
        {
            if (filter != null && !ReferenceEquals(filter, next))
                throw new InvalidOperationException("Health already has an incoming damage filter.");
            filter = next;
        }
        public void RemoveDamageFilter(IIncomingDamageFilter previous) { if (ReferenceEquals(filter, previous)) filter = null; }
        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || resolving || float.IsNaN(info.Amount) || float.IsInfinity(info.Amount) || info.Amount <= 0f) return;
            resolving = true;
            try
            {
                if (filter != null && !filter.FilterIncoming(ref info)) return;
                float amount = info.Amount * Mathf.Max(0f, damageMultiplier);
                if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return;
                float before = CurrentHealth;
                CurrentHealth = Mathf.Max(0f, CurrentHealth - amount); Sync();
                info.Amount = before - CurrentHealth;
                bool killed = IsDead;
                OnDamaged?.Invoke(info);
                if (killed) OnDeath?.Invoke();
            }
            finally { resolving = false; }
        }
        public void Heal(float amount)
        {
            if (IsDead || !FinitePositive(amount)) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount); Sync();
        }
        public void Revive(float amount = -1f)
        {
            if (resolving) throw new InvalidOperationException("Revive after the damage notification finishes.");
            CurrentHealth = amount <= 0f || !FinitePositive(amount) ? maxHealth : Mathf.Min(maxHealth, amount);
            Sync(); OnRevived?.Invoke();
        }
        private static bool FinitePositive(float x) => x > 0f && !float.IsNaN(x) && !float.IsInfinity(x);
        private void Sync() { currentHealth = CurrentHealth; }
    }
}

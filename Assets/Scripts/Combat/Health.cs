using System;
using UnityEngine;

namespace PirateGame.Combat
{
    /// <summary>
    /// Anything that can receive damage (player, enemies, destructibles, ships later).
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsDead { get; }
    }

    /// <summary>
    /// Info describing one hit. Extend later (damage type, knockback, etc.).
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public GameObject Attacker;
        public Vector3 HitPoint;

        public DamageInfo(float amount, GameObject attacker, Vector3 hitPoint = default)
        {
            Amount = amount;
            Attacker = attacker;
            HitPoint = hitPoint;
        }
    }

    /// <summary>
    /// Simple shared health component used by both the player and enemies.
    /// Other scripts subscribe to the events instead of polling.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead => CurrentHealth <= 0f;

        /// <summary>Fired every time damage is applied (after health is reduced).</summary>
        public event Action<DamageInfo> OnDamaged;

        /// <summary>Fired once when health reaches zero.</summary>
        public event Action OnDeath;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        private void FixedUpdate()
        {

            currentHealth = CurrentHealth;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - info.Amount);
            OnDamaged?.Invoke(info);

            if (IsDead)
            {
                OnDeath?.Invoke();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        }

        /// <summary>
        /// Bring something back from zero — used for respawning the player and for
        /// resetting pooled enemies. Deliberately separate from <see cref="Heal"/>,
        /// which refuses to resurrect the dead.
        /// </summary>
        public void Revive(float amount = -1f)
        {
            CurrentHealth = amount <= 0f ? maxHealth : Mathf.Min(maxHealth, amount);
            OnRevived?.Invoke();
        }

        /// <summary>Fired when something is brought back from zero health.</summary>
        public event Action OnRevived;
    }
}

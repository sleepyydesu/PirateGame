using System;
using UnityEngine;

namespace PirateGame.Progression
{
    /// <summary>
    /// Implement this interface on the player's existing health component.
    /// The implementation should increase max health and then restore current health to max health.
    /// </summary>
    public interface ILevelUpHealth
    {
        void IncreaseMaxHealthAndRestore(float amount);
    }

    /// <summary>
    /// Owns player doubloons, cumulative XP, and level progression.
    /// Other systems communicate with it through public methods and events.
    /// XP thresholds are cumulative totals required to attain levels 2, 3, 4, and 5.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProgression : MonoBehaviour
    {
        public const int MaximumLevel = 5;

        private static readonly int[] DefaultXpThresholds = { 100, 250, 500, 1000 };

        [Header("Starting Values")]
        [SerializeField, Min(0)] private int startingDoubloons = 0;

        [Header("Level Progression")]
        [Tooltip("Cumulative total XP needed to reach levels 2, 3, 4, and 5, respectively.")]
        [SerializeField, Min(1)] private int[] xpThresholds = { 100, 250, 500, 1000 };

        [Tooltip("Amount added to the player's maximum health for every level gained.")]
        [SerializeField, Min(0f)] private float maxHealthIncreasePerLevel = 20f;

        [Tooltip("Drag the player's existing Health component here. It must implement ILevelUpHealth.")]
        [SerializeField] private MonoBehaviour healthTarget;

        private int doubloons;
        private int totalExperience;
        private int currentLevel = 1;
        private bool hasWarnedAboutHealthTarget;

        public int Doubloons => doubloons;
        public int TotalExperience => totalExperience;
        public int CurrentLevel => currentLevel;
        /// <summary>Cumulative XP at which the current rank began.</summary>
        public int CurrentLevelXpStart => currentLevel <= 1 ? 0 : xpThresholds[currentLevel - 2];
        public int NextLevelXpThreshold => currentLevel >= MaximumLevel ? 0 : xpThresholds[currentLevel - 1];

        /// <summary>Invoked after the player's doubloon total changes.</summary>
        public event Action<int> MoneyChanged;

        /// <summary>Invoked after XP changes. Arguments: total XP, next-level cumulative threshold (0 at max level).</summary>
        public event Action<int, int> ExperienceChanged;

        /// <summary>Invoked whenever the player's level changes.</summary>
        public event Action<int> LevelChanged;

        /// <summary>Invoked after level-up health handling. UI and VFX listeners can subscribe to this event.</summary>
        public event Action<int> LevelUp;

        private void Awake()
        {
            doubloons = Mathf.Max(0, startingDoubloons);
            FindHealthTargetOnThisObjectIfNeeded();
        }

        /// <summary>Adds doubloons. Non-positive amounts are ignored.</summary>
        public void AddDoubloons(int amount)
        {
            if (amount <= 0)
                return;

            doubloons += amount;
            MoneyChanged?.Invoke(doubloons);
        }

        /// <summary>Adds cumulative XP and processes any levels earned.</summary>
        public void AddExperience(int amount)
        {
            if (amount <= 0)
                return;

            totalExperience += amount;

            // A large XP reward can cross more than one threshold, so use a loop.
            while (currentLevel < MaximumLevel && totalExperience >= xpThresholds[currentLevel - 1])
            {
                currentLevel++;
                ApplyLevelUpHealthBonus();
                LevelChanged?.Invoke(currentLevel);
                LevelUp?.Invoke(currentLevel);
            }

            ExperienceChanged?.Invoke(totalExperience, NextLevelXpThreshold);
        }

        /// <summary>Convenience method used by EnemyLoot to award both types of reward.</summary>
        public void AwardLoot(int moneyAmount, int experienceAmount)
        {
            AddDoubloons(moneyAmount);
            AddExperience(experienceAmount);
        }

        private void ApplyLevelUpHealthBonus()
        {
            if (healthTarget is ILevelUpHealth levelUpHealth)
            {
                levelUpHealth.IncreaseMaxHealthAndRestore(maxHealthIncreasePerLevel);
                return;
            }

            if (!hasWarnedAboutHealthTarget)
            {
                Debug.LogWarning(
                    $"{nameof(PlayerProgression)} on '{name}' leveled up, but no assigned component implements " +
                    $"{nameof(ILevelUpHealth)}. Assign your existing Health component and implement " +
                    $"IncreaseMaxHealthAndRestore(float amount).", this);
                hasWarnedAboutHealthTarget = true;
            }
        }

        private void FindHealthTargetOnThisObjectIfNeeded()
        {
            if (healthTarget != null)
                return;

            // Convenience fallback when the existing Health component is on the same GameObject.
            foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
            {
                if (component is ILevelUpHealth)
                {
                    healthTarget = component;
                    return;
                }
            }
        }

        private void OnValidate()
        {
            startingDoubloons = Mathf.Max(0, startingDoubloons);
            maxHealthIncreasePerLevel = Mathf.Max(0f, maxHealthIncreasePerLevel);

            if (xpThresholds == null)
                xpThresholds = new int[MaximumLevel - 1];

            int oldLength = xpThresholds.Length;
            if (oldLength != MaximumLevel - 1)
                Array.Resize(ref xpThresholds, MaximumLevel - 1);

            for (int i = 0; i < xpThresholds.Length; i++)
            {
                if (i >= oldLength || xpThresholds[i] < 1)
                    xpThresholds[i] = DefaultXpThresholds[i];
            }

            // Keep thresholds increasing, otherwise a higher level could require less total XP.
            for (int i = 1; i < xpThresholds.Length; i++)
                xpThresholds[i] = Mathf.Max(xpThresholds[i], xpThresholds[i - 1] + 1);
        }
    }
}

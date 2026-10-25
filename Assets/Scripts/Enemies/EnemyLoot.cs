using UnityEngine;

namespace PirateGame.Progression
{
    /// <summary>
    /// Attach to an enemy and call DropLoot() exactly when the enemy dies.
    /// The method is safe to call more than once; a given enemy only awards loot once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLoot : MonoBehaviour
    {
        [Header("Loot Drop")]
        [SerializeField, Min(0)] private int doubloonsDrop = 10;
        [SerializeField, Min(0)] private int experienceDrop = 25;

        [Tooltip("Assign the player's PlayerProgression component.")]
        [SerializeField] private PlayerProgression playerProgression;

        private bool lootDropped;

        /// <summary>Call from the enemy's death handler or a death-animation event.</summary>
        public void DropLoot()
        {
            if (lootDropped)
                return;

            if (playerProgression == null)
            {
                Debug.LogError(
                    $"EnemyLoot on '{name}' has no PlayerProgression assigned. Loot was not awarded.", this);
                return;
            }

            // Set this before invoking gameplay events to guard against re-entrant/double death calls.
            lootDropped = true;
            playerProgression.AwardLoot(doubloonsDrop, experienceDrop);
        }

        private void OnValidate()
        {
            doubloonsDrop = Mathf.Max(0, doubloonsDrop);
            experienceDrop = Mathf.Max(0, experienceDrop);
        }
    }
}

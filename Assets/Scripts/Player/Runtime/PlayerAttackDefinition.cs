using UnityEngine;
using PirateGame.Combat;

namespace PirateGame.PlayerV2
{
    [CreateAssetMenu(menuName = "Pirate Game/Player V2/Attack")]
    public sealed class PlayerAttackDefinition : ScriptableObject
    {
        public string fullStatePath = "Arms Layer.combat_attack";
        [Range(1, 2)] public int layer = 2;
        [Min(0f)] public float damage = 20f;
        [Header("Reaction requested on the target")]
        public HitReactionType hitReaction = HitReactionType.Default;
        public HitReactionMode hitReactionMode = HitReactionMode.Stagger;
        [Tooltip("0.46 * 5m/s = walking speed while cutting.")]
        [Range(0f, 1f)] public float movementScale = 0.46f;
        [Min(0f)] public float anticipationTurnRate = 540f;
        [Min(0.1f)] public float watchdogSeconds = 4f;
        [Min(0f)] public float recoverySeconds = 0.12f;
        [Tooltip("Only supported on full-body Combat Layer attacks. Motor handles collisions.")]
        public bool useRootTranslation;
        [Tooltip("Make all horizontal animation movement travel forward instead of following the clip's original direction.")]
        public bool forceForwardRootMotion = false;
        public PlayerAttackDefinition followup;
        [System.Serializable]
        public sealed class WeightedFollowup
        {
            public PlayerAttackDefinition attack;

            [Min(0f)]
            public float weight = 1f;

            public bool CanChoose =>
                attack != null &&
                weight > 0f &&
                !float.IsNaN(weight) &&
                !float.IsInfinity(weight);
        }

        [Header("Random follow-ups")]
        public WeightedFollowup[] weightedFollowups = new WeightedFollowup[0];

        public bool avoidSameFollowupTwice = true;

        public bool UsesWeightedFollowups =>
            weightedFollowups != null &&
            weightedFollowups.Length > 0;

        public bool HasFollowup
        {
            get
            {
                if (!UsesWeightedFollowups)
                    return followup != null;

                foreach (var option in weightedFollowups)
                {
                    if (option != null && option.CanChoose)
                        return true;
                }

                return false;
            }
        }

        public PlayerAttackDefinition PickFollowup(
            PlayerAttackDefinition previous)
        {
            if (!UsesWeightedFollowups)
                return followup;

            bool skipPrevious = false;

            if (avoidSameFollowupTwice && previous != null)
            {
                foreach (var option in weightedFollowups)
                {
                    if (option != null &&
                        option.CanChoose &&
                        option.attack != previous)
                    {
                        skipPrevious = true;
                        break;
                    }
                }
            }

            double total = 0;

            foreach (var option in weightedFollowups)
            {
                if (option != null &&
                    option.CanChoose &&
                    (!skipPrevious || option.attack != previous))
                {
                    total += option.weight;
                }
            }

            if (total <= 0)
                return null;

            double roll = UnityEngine.Random.value * total;
            PlayerAttackDefinition lastValid = null;

            foreach (var option in weightedFollowups)
            {
                if (option == null ||
                    !option.CanChoose ||
                    (skipPrevious && option.attack == previous))
                {
                    continue;
                }

                lastValid = option.attack;
                roll -= option.weight;

                if (roll < 0)
                    return option.attack;
            }

            // Random.value can include 1.
            return lastValid;
        }
    }
}

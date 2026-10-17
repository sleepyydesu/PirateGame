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
        public PlayerAttackDefinition followup;
    }
}

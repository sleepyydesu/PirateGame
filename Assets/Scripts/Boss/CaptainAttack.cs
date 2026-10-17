using UnityEngine;
using PirateGame.Combat;

namespace PirateGame.Enemies
{
    [CreateAssetMenu(menuName = "Pirate Game/Captain Attack")]
    public sealed class CaptainAttack : ScriptableObject
    {
        [Header("Animator contract: layer 0, unique non-looping state per move")]
        public string trigger = "Attack";
        public string fullStateName = "Base Layer.attack";
        [Min(0.2f)] public float watchdogSeconds = 4f;
        [Header("Selection envelope, NOT a damage radius")]
        [Min(0f)] public float minRange;
        [Min(0.1f)] public float maxRange = 2.5f;
        [Range(1f, 180f)] public float maxFacingAngle = 65f;
        [Min(0f)] public float phase1Weight = 1f, phase2Weight = 1f;
        [Min(0f)] public float cooldown = 3f;
        [Header("Commitment and consequence")]
        [Min(0f)] public float damage = 20f;
        [Min(0f)] public float fatigueCost = 25f;
        [Min(0.1f)] public float guaranteedRecovery = 0.7f;
        [Min(0f)] public float trackingDegreesPerSecond = 100f;
        [Header("Reaction requested on the target")]
        public HitReactionType hitReaction = HitReactionType.Default;
        public HitReactionMode hitReactionMode = HitReactionMode.Stagger;
        public bool useRootMotion;
        [Header("Jump slam")]
        public bool jumpSlam;
        [Tooltip("Adjusts the direction of the jump attack's weapon strike.")]
        [Range(-45f, 45f)]
        public float jumpFacingOffsetDegrees = 0f;

        [Min(0.1f)]
        public float jumpHeight = 1.8f;

        [Min(0.1f)]
        public float jumpMaxDistance = 5f;

        [Range(0.05f, 0.95f)]
        public float jumpLandNormalized = 0.65f;
        [Header("Forward step for in-place attack clips")]
        [Min(0f)] public float attackStepDistance = 0.7f;
        [Min(0f)] public float attackStepSpeed = 2.5f;


        [Tooltip("Minimum horizontal distance between boss and player roots.")]
        [Min(0.1f)] public float attackStopDistance = 1.3f;

        [Header("Air slash projectile")]
        public bool isProjectileAttack;

        public SwordAirSlashProjectile projectilePrefab;

        [Tooltip("Minimum and maximum speed in metres per second.")]
        public Vector2 projectileSpeedRange = new Vector2(12f, 18f);
        [Tooltip("Extra rotation around the projectile's travel direction.")]
        [Range(-180f, 180f)]
        public float projectileRollOffsetDegrees = 0f;

        [Min(0f)]
        public float projectileAimHeight = 1f;

        public bool parryable = true;
        public bool punishHealing;
        [Header("Optional extension (separate clip/state)")]
        public CaptainAttack followup;
        [Range(0f, 1f)] public float phase1ChainChance = 0.15f;
        [Range(0f, 1f)] public float phase2ChainChance = 0.45f;
        // No runtime counters live in this asset: multiple bosses may share it safely.
    }
}

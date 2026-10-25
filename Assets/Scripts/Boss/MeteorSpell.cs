using UnityEngine;

namespace PirateGame.Enemies
{
    // Settings for ONE meteor spell. No code runs here.
    // Create it: Right-click in Project > Create > Pirate Game > Meteor Spell.
    [CreateAssetMenu(menuName = "Pirate Game/Meteor Spell")]
    public sealed class MeteorSpell : ScriptableObject
    {
        [Header("What falls (prefabs)")]
        [Tooltip("The meteor model. A sphere with a fire material is enough to start.")]
        public GameObject meteorPrefab;
        [Tooltip("Optional. Spawned where a meteor lands (explosion, sound).")]
        public GameObject impactEffectPrefab;
        [Tooltip("Optional. A flat red circle on the ground. It warns the player where a meteor will land. Make it 1 unit wide.")]
        public GameObject groundMarkerPrefab;
        [Header("Self-playing prefab (all-in-one meteor)")]
        [Tooltip("Tick for a self-playing VFX prefab. The prefab root (or MeteorParts.body) is moved down to impact.")]
        public bool prefabPlaysItself = false;
        [Tooltip("Earliest seconds from spawning until impact. A slower fall can take longer.")]
        [Min(0f)] public float impactDelay = 1.5f;
        [Tooltip("Seconds until the whole prefab is removed. Must be longer than the full effect.")]
        [Min(0.5f)] public float prefabLifetime = 5f;
        [Header("Sound")]
        public AudioClip formingSound;
        [Range(0f, 1f)] public float formingSoundVolume = 1f;
        public AudioClip impactSound;
        [Range(0f, 1f)] public float impactSoundVolume = 1f;

        [Header("How many, how long, how fast")]
        [Tooltip("Meteors spawned together in each wave.")]
        [Min(1)] public int meteorCount = 10;
        [Tooltip("Seconds the boss keeps spawning meteors.")]
        [Min(0.5f)] public float castDuration = 5f;
        [Tooltip("Seconds between waves. A wave is spawned immediately when casting starts.")]
        [Min(0.05f)] public float waveInterval = 1f;
        [Tooltip("Meters per second while falling.")]
        [Min(1f)] public float fallSpeed = 14f;
        [Tooltip("How high above the ground a meteor forms.")]
        [Min(2f)] public float spawnHeight = 18f;
        [Tooltip("Seconds a meteor takes to grow before it drops. Warning time = Form Time + Spawn Height / Fall Speed.")]
        [Min(0f)] public float formTime = 0.6f;

        [Header("Where they land")]
        [Tooltip("Chance each wave includes one meteor that lands right on the player. The rest land around the player.")]
        [Range(0f, 1f)] public float directHitChance = 0.35f;
        [Tooltip("Keep this bigger than Blast Radius, or the meteors around you will also hit you.")]
        [Min(0f)] public float scatterMinRadius = 3f;
        [Min(0.5f)] public float scatterMaxRadius = 8f;
        [Tooltip("Meteors try not to land closer than this to each other.")]
        [Min(0f)] public float minSpacing = 2f;

        [Header("Damage")]
        [Min(0f)] public float damage = 25f;
        [Min(0.1f)] public float blastRadius = 2f;
        [Tooltip("Layers that can be hurt. Pick the Player layer only.")]
        public LayerMask victims;
        [Tooltip("Your floor layers. Leave on Nothing for a flat arena. Never include Player or Enemy.")]
        public LayerMask ground;

        [Header("Spell pose")]
        [Tooltip("Pause the animation while casting, so the boss holds the spell pose.")]
        public bool holdPose = true;
        [Tooltip("0 = frozen. Increase slightly only if you want subtle movement during the spell.")]
        [Range(0f, 1f)] public float holdPoseSpeed = 0f;

        [Header("Later phases (phase 2 and above)")]
        [Min(0.1f)] public float laterPhaseCountMultiplier = 1.5f;
        [Min(0.1f)] public float laterPhaseSpeedMultiplier = 1.15f;
    }
}
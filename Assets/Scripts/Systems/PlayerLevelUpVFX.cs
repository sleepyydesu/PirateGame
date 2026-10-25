using UnityEngine;

namespace PirateGame.Progression
{
    /// <summary>
    /// Optional event listener that spawns a particle-effect prefab whenever the player levels up.
    /// Attach it to the player GameObject, or assign the PlayerProgression reference manually.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLevelUpVFX : MonoBehaviour
    {
        [SerializeField] private PlayerProgression playerProgression;
        [SerializeField] private ParticleSystem levelUpEffectPrefab;
        [SerializeField] private Vector3 localSpawnOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.1f)] private float destroyAfterSeconds = 5f;

        private void OnEnable()
        {
            if (playerProgression == null)
                playerProgression = GetComponent<PlayerProgression>();

            if (playerProgression == null)
            {
                Debug.LogError($"Assign a {nameof(PlayerProgression)} to {nameof(PlayerLevelUpVFX)}.", this);
                return;
            }

            playerProgression.LevelUp += HandleLevelUp;
        }

        private void OnDisable()
        {
            if (playerProgression != null)
                playerProgression.LevelUp -= HandleLevelUp;
        }

        private void HandleLevelUp(int newLevel)
        {
            if (levelUpEffectPrefab == null)
            {
                Debug.LogWarning($"No level-up ParticleSystem prefab is assigned on '{name}'.", this);
                return;
            }

            Vector3 spawnPosition = transform.position + transform.TransformVector(localSpawnOffset);
            ParticleSystem effect = Instantiate(levelUpEffectPrefab, spawnPosition, transform.rotation);
            effect.Play(true);
            Destroy(effect.gameObject, destroyAfterSeconds);
        }
    }
}

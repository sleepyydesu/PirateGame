using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateGame.Combat;

namespace PirateGame.UI
{
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        [Header("Enemy")]
        [Tooltip("Enemy root or transform that the health bar follows.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("Height above the enemy's pivot, in world units.")]
        [Min(0f)]
        [SerializeField] private float heightOffset = 2.2f;

        [Header("Camera")]
        [Tooltip("Leave empty to use the Main Camera.")]
        [SerializeField] private Camera targetCamera;

        [Header("UI References")]
        [Tooltip("The World Space Canvas on this GameObject.")]
        [SerializeField] private Canvas worldCanvas;

        [Tooltip("The Image that displays the current health.")]
        [SerializeField] private Image healthFill;
        [Tooltip("The delayed damage trail behind the health fill.")]
        [SerializeField] private Image damageTrailFill;

        [Tooltip("How long the damage trail waits before moving.")]
        [SerializeField, Min(0f)] private float damageTrailDelay = 0.2f;

        [Tooltip("How quickly the damage trail catches up, in fill fraction per second.")]
        [SerializeField, Min(0.01f)] private float damageTrailSpeed = 1.2f;

        [Tooltip("Optional text showing current and maximum health.")]
        [SerializeField] private TMP_Text healthValueText;

        [Header("Behaviour")]
        [Tooltip("Hide the bar when the enemy reaches zero health.")]
        [SerializeField] private bool hideWhenDead = true;

        private Health targetHealth;
        private bool hasHealthSample;
        private float previousHealthFraction;
        private float damageTrailTargetFraction;
        private float damageTrailDelayRemaining;

        private void Awake()
        {
            targetHealth = GetComponentInParent<Health>();

            if (followTarget == null && targetHealth != null)
                followTarget = targetHealth.transform;

            if (worldCanvas == null)
                worldCanvas = GetComponent<Canvas>();

            if (targetCamera == null)
                targetCamera = Camera.main;
        }

        private void OnEnable()
        {
            if (targetHealth == null)
                targetHealth = GetComponentInParent<Health>();

            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged += HandleHealthChanged;

                // Start both fills at the enemy's current health.
                // The first sample should not animate.
                hasHealthSample = false;

                HandleHealthChanged(
                    targetHealth.CurrentHealth,
                    targetHealth.MaxHealth
                );
            }
            else
            {
                Debug.LogWarning(
                    $"EnemyHealthBar on '{name}' could not find a Health " +
                    "component on this object or its parents.",
                    this
                );
            }
        }

        private void OnDisable()
        {
            if (targetHealth != null)
                targetHealth.OnHealthChanged -= HandleHealthChanged;
        }

        private void LateUpdate()
        {
            UpdateDamageTrail();
            // Follow the enemy's position.
            if (followTarget != null)
            {
                transform.position =
                    followTarget.position + Vector3.up * heightOffset;
            }

            // Find the camera if it wasn't available during Awake.
            if (targetCamera == null)
                targetCamera = Camera.main;

            // Keep the front of the health bar facing the camera.
            if (targetCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(
                    targetCamera.transform.position - transform.position,
                    Vector3.up
                );
            }

            // Give the World Space Canvas the appropriate camera reference.
            if (worldCanvas != null && targetCamera != null)
                worldCanvas.worldCamera = targetCamera;

            if (targetHealth != null)
            {
                HandleHealthChanged(
                    targetHealth.CurrentHealth,
                    targetHealth.MaxHealth
                );
            }
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            float fraction = maximum > 0.001f
                ? Mathf.Clamp01(current / maximum)
                : 0f;

            // The actual health fill changes immediately.
            if (healthFill != null)
                healthFill.fillAmount = fraction;

            if (!hasHealthSample)
            {
                // Initialise both fills without playing an animation.
                if (damageTrailFill != null)
                    damageTrailFill.fillAmount = fraction;

                damageTrailTargetFraction = fraction;
                damageTrailDelayRemaining = 0f;
                hasHealthSample = true;
            }
            else if (fraction < previousHealthFraction - 0.0001f)
            {
                // DAMAGE: keep the trail where it is for a short time,
                // then animate it down to the new health fraction.
                damageTrailTargetFraction = fraction;
                damageTrailDelayRemaining = damageTrailDelay;
            }
            else if (fraction > previousHealthFraction + 0.0001f)
            {
                // HEALING: bring the trail up immediately so it doesn't
                // incorrectly appear to represent missing health.
                if (damageTrailFill != null)
                    damageTrailFill.fillAmount = fraction;

                damageTrailTargetFraction = fraction;
                damageTrailDelayRemaining = 0f;
            }

            previousHealthFraction = fraction;

            if (healthValueText != null)
            {
                healthValueText.SetText(
                    "{0:0} / {1:0}",
                    Mathf.Ceil(current),
                    Mathf.Ceil(maximum)
                );
            }

            // Preserve the existing option to hide the bar on death.
            if (worldCanvas != null && hideWhenDead)
                worldCanvas.enabled = current > 0f;
        }

        private void UpdateDamageTrail()
        {
            if (damageTrailFill == null || !hasHealthSample)
                return;

            // Wait before starting the trailing animation.
            if (damageTrailDelayRemaining > 0f)
            {
                damageTrailDelayRemaining -= Time.unscaledDeltaTime;
                return;
            }

            // Gradually catch up with the real health.
            damageTrailFill.fillAmount = Mathf.MoveTowards(
                damageTrailFill.fillAmount,
                damageTrailTargetFraction,
                damageTrailSpeed * Time.unscaledDeltaTime
            );
        }
    }
}
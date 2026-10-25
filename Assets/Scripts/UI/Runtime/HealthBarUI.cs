using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PirateGame.Combat;

namespace PirateGame.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class HealthBarUI : MonoBehaviour
    {
        [Header("Whose health this bar shows")]
        public Health source;
        public string displayName = "PLAYER";

        [Header("UI references; assigned by the creation menu")]
        public Image healthFill;
        [Tooltip("The delayed damage trail behind the health fill.")]
        public Image damageTrail;
        public TMP_Text nameText;
        public TMP_Text valueText;
        public bool showNumbers = true;

        [Header("Damage trail timing")]
        [Tooltip("Seconds the trail waits before it starts draining.")]
        [Min(0f)] public float damageDelay = 0.35f;
        [Tooltip("How fast the trail drains, in fill fraction per second.")]
        [Min(0.01f)] public float trailSpeed = 1.2f;

        private Health boundSource;
        private bool initialized;
        private float previousHealth, previousMaximum, target, trail, hold;
        private string previousName;
        private bool previousShowNumbers;

        private void OnEnable() { initialized = false; }

        private void LateUpdate()
        {
            Refresh(Time.unscaledDeltaTime);
        }

        // Public for a future encounter manager or a newly spawned player.
        public void SetSource(Health next)
        {
            source = next;
            initialized = false;
        }

        public void Refresh(float dt)
        {
            if (nameText != null && previousName != displayName)
            {
                nameText.text = displayName;
                previousName = displayName;
            }

            if (source == null)
            {
                SetFill(healthFill, 0f);
                SetFill(damageTrail, 0f);
                if (valueText != null) valueText.text = showNumbers ? "-- / --" : "";
                initialized = false;
                boundSource = null;
                return;
            }

            // Health.Heal has no event in the supplied Health script. Reading these
            // values covers damage, healing and revival without editing Health.
            float maximum = Mathf.Max(0.0001f, source.MaxHealth);
            float current = Mathf.Clamp(source.CurrentHealth, 0f, maximum);
            float fraction = Mathf.Clamp01(current / maximum);
            bool snap = !initialized || source != boundSource ||
                        !Mathf.Approximately(maximum, previousMaximum);
            bool changed = snap || !Mathf.Approximately(current, previousHealth);

            if (snap)
            {
                target = trail = fraction;
                hold = 0f;
            }
            else if (changed)
            {
                if (fraction < target)
                {
                    // Main health changes immediately; the trail is visual only.
                    trail = Mathf.Max(trail, target);
                    hold = Mathf.Max(0f, damageDelay);
                }
                else
                {
                    // Healing never leaves a false damage trail behind.
                    trail = fraction;
                    hold = 0f;
                }
                target = fraction;
            }

            if (changed || previousShowNumbers != showNumbers)
            {
                if (valueText != null)
                {
                    if (showNumbers)
                        valueText.SetText("{0:0} / {1:0}", Mathf.Ceil(current), Mathf.Ceil(maximum));
                    else valueText.text = "";
                }
            }

            // A newly observed hit gets its full delay, even on a slow frame.
            if (!changed)
            {
                float remaining = Mathf.Max(0f, dt);
                float used = Mathf.Min(hold, remaining);
                hold -= used;
                remaining -= used;
                trail = Mathf.MoveTowards(trail, target,
                    Mathf.Max(0.01f, trailSpeed) * remaining);
            }

            trail = Mathf.Clamp(trail, target, 1f);
            SetFill(healthFill, target);
            SetFill(damageTrail, trail);
            previousHealth = current;
            previousMaximum = maximum;
            previousShowNumbers = showNumbers;
            boundSource = source;
            initialized = true;
        }

        private static void SetFill(Image image, float value)
        {
            if (image != null && !Mathf.Approximately(image.fillAmount, value))
                image.fillAmount = value;
        }
    }
}
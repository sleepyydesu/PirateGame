using System.Collections;
using TMPro;
using UnityEngine;
using PirateGame.Progression;

namespace PirateGame.UI
{
    /// <summary>
    /// Event-driven HUD. It subscribes to PlayerProgression events and does not poll in Update().
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PirateUIManager : MonoBehaviour
    {
        [Header("Data Source")]
        [SerializeField] private PlayerProgression playerProgression;

        [Header("HUD Text")]
        [SerializeField] private TMP_Text treasureMapPlaceholderText;
        [SerializeField] private TMP_Text doubloonsText;
        [SerializeField] private TMP_Text levelText;

        [TextArea]
        [SerializeField] private string treasureMapPlaceholderMessage = "TREASURE MAP\n\nNo chart discovered yet...";

        [Header("Level-Up Popup")]
        [Tooltip("CanvasGroup on the popup panel (or a parent of the popup text).")]
        [SerializeField] private CanvasGroup levelUpPopupGroup;
        [SerializeField] private TMP_Text levelUpPopupText;
        [SerializeField] private string levelUpMessageFormat = "Arrr! Ye Be a Higher Rank Now!\nLevel {0}!";
        [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;
        [SerializeField, Min(0f)] private float popupHoldDuration = 2f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.8f;

        private Coroutine popupCoroutine;

        private void Awake()
        {
            SetPopupAlpha(0f);
            if (treasureMapPlaceholderText != null)
                treasureMapPlaceholderText.text = treasureMapPlaceholderMessage;
        }

        private void OnEnable()
        {
            if (playerProgression == null)
            {
                Debug.LogError($"Assign a {nameof(PlayerProgression)} to {nameof(PirateUIManager)}.", this);
                return;
            }

            playerProgression.MoneyChanged += HandleMoneyChanged;
            playerProgression.LevelChanged += HandleLevelChanged;
            playerProgression.LevelUp += HandleLevelUp;

            // Populate the HUD immediately in case progression changed before this UI was enabled.
            RefreshHud();
        }

        private void OnDisable()
        {
            if (playerProgression != null)
            {
                playerProgression.MoneyChanged -= HandleMoneyChanged;
                playerProgression.LevelChanged -= HandleLevelChanged;
                playerProgression.LevelUp -= HandleLevelUp;
            }

            if (popupCoroutine != null)
            {
                StopCoroutine(popupCoroutine);
                popupCoroutine = null;
            }

            SetPopupAlpha(0f);
        }

        private void RefreshHud()
        {
            HandleMoneyChanged(playerProgression.Doubloons);
            HandleLevelChanged(playerProgression.CurrentLevel);

            if (treasureMapPlaceholderText != null)
                treasureMapPlaceholderText.text = treasureMapPlaceholderMessage;
        }

        private void HandleMoneyChanged(int totalDoubloons)
        {
            if (doubloonsText != null)
                doubloonsText.text = $"DOUBLOONS  {totalDoubloons:N0}";
        }

        private void HandleLevelChanged(int level)
        {
            if (levelText != null)
                levelText.text = $"LEVEL  {level}";
        }

        private void HandleLevelUp(int newLevel)
        {
            if (levelUpPopupText == null || levelUpPopupGroup == null)
                return;

            if (popupCoroutine != null)
                StopCoroutine(popupCoroutine);

            popupCoroutine = StartCoroutine(PlayLevelUpPopup(newLevel));
        }

        private IEnumerator PlayLevelUpPopup(int newLevel)
        {
            levelUpPopupText.text = string.Format(levelUpMessageFormat, newLevel);
            levelUpPopupGroup.gameObject.SetActive(true);
            SetPopupAlpha(0f);

            yield return FadePopup(0f, 1f, fadeInDuration);
            yield return new WaitForSecondsRealtime(popupHoldDuration);
            yield return FadePopup(1f, 0f, fadeOutDuration);

            SetPopupAlpha(0f);
            popupCoroutine = null;
        }

        private IEnumerator FadePopup(float from, float to, float duration)
        {
            if (levelUpPopupGroup == null)
                yield break;

            if (duration <= 0f)
            {
                levelUpPopupGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                levelUpPopupGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            levelUpPopupGroup.alpha = to;
        }

        private void SetPopupAlpha(float alpha)
        {
            if (levelUpPopupGroup == null)
                return;

            levelUpPopupGroup.alpha = alpha;
            levelUpPopupGroup.interactable = false;
            levelUpPopupGroup.blocksRaycasts = false;
        }
    }
}

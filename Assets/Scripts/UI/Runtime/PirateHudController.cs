using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PirateGame.Combat;
using PirateGame.Progression;
using PirateGame.Quests;

namespace PirateGame.UI
{
    /// <summary>
    /// Drives the player HUD made by Tools/Pirate Game/Create Pirate HUD.
    /// Sources are found automatically when they live on the player; they may also
    /// be assigned explicitly for scenes with more than one character.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PirateHudController : MonoBehaviour
    {
        public enum CoinSource { InventoryGold, ProgressionDoubloons }

        [Header("Player sources")]
        [SerializeField] private Transform player;
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private PlayerProgression progression;
        [SerializeField] private CoinSource coinSource = CoinSource.ProgressionDoubloons;

        [Header("Health display")]
        [Tooltip("Existing HealthBarUI that draws the fill, damage trail and numbers. This controller gives it the player's Health.")]
        [SerializeField] private HealthBarUI healthBar;

        [Header("Treasure display")]
        [SerializeField] private TMP_Text coinsText;
        [SerializeField] private string coinsLabel = "DOUBLOONS";

        [Header("Rank and experience display")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text experienceText;
        [SerializeField] private Image experienceFill;
        [Tooltip("Optional. Adds the delayed gain trail on the XP ring.")]
        [SerializeField] private HudFillTrail experienceTrail;

        [Header("Rank emblem")]
        [Tooltip("Transparent pirate-skull sprite shown inside the XP ring.")]
        [SerializeField] private Sprite pirateSkullSprite;
        [SerializeField] private Image skullImage;
        [SerializeField] private GameObject skullFallback;

        [Header("Map display")]
        [Tooltip("Assign the RenderTexture used by your overhead minimap camera. Leave empty to keep the parchment placeholder.")]
        [SerializeField] private Texture mapTexture;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;

        private Health boundHealth;
        private PlayerInventory subscribedInventory;
        private PlayerProgression subscribedProgression;
        private float nextResolveAttempt;

        // XP trail bookkeeping: snap (no animation) the first time a source is bound.
        private bool xpNeedsSnap = true;
        private int lastXpStart = -1;

        private void OnEnable()
        {
            ResolveSources();
            RefreshAll();
        }

        private void OnValidate()
        {
            RefreshSkull();
        }

        private void Start()
        {
            // Some player components initialise after the HUD canvas.
            ResolveSources();
            RefreshAll();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            UpdateMarker();

            if (Time.unscaledTime >= nextResolveAttempt && NeedsSource())
            {
                nextResolveAttempt = Time.unscaledTime + 0.5f;
                ResolveSources();
                RefreshAll();
            }
        }

        /// <summary>Assign a minimap RenderTexture at runtime if the map camera is spawned later.</summary>
        public void SetMapTexture(Texture texture)
        {
            mapTexture = texture;
            RefreshMap();
        }

        /// <summary>Replaces the built-in emblem with a transparent custom skull sprite.</summary>
        public void SetPirateSkull(Sprite sprite)
        {
            pirateSkullSprite = sprite;
            RefreshSkull();
        }

        private bool NeedsSource()
        {
            return playerHealth == null ||
                progression == null ||
                (coinSource == CoinSource.InventoryGold && inventory == null) ||
                (coinSource == CoinSource.ProgressionDoubloons && progression == null);
        }

        private void ResolveSources()
        {
            Transform root = player;
            if (root == null)
            {
                GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
                if (taggedPlayer != null) root = taggedPlayer.transform;
            }

            if (root != null)
            {
                player = root;
                if (playerHealth == null) playerHealth = root.GetComponentInChildren<Health>();
                if (inventory == null) inventory = root.GetComponentInChildren<PlayerInventory>();
                if (progression == null) progression = root.GetComponentInChildren<PlayerProgression>();
            }

            // PlayerInventory exposes a safe singleton and is useful when the Player tag is not set.
            if (inventory == null) inventory = PlayerInventory.Instance;
            if (player == null && inventory != null) player = inventory.transform;

            if (playerHealth == null) playerHealth = FindAnyObjectByType<Health>();
            if (progression == null) progression = FindAnyObjectByType<PlayerProgression>();

            RebindEvents();
        }

        private void RebindEvents()
        {
            if (boundHealth != playerHealth)
            {
                boundHealth = playerHealth;
                if (healthBar != null) healthBar.SetSource(playerHealth);
            }

            if (subscribedInventory != inventory)
            {
                if (subscribedInventory != null) subscribedInventory.GoldChanged -= HandleGoldChanged;
                subscribedInventory = inventory;
                if (subscribedInventory != null) subscribedInventory.GoldChanged += HandleGoldChanged;
            }

            if (subscribedProgression != progression)
            {
                if (subscribedProgression != null)
                {
                    subscribedProgression.MoneyChanged -= HandleDoubloonsChanged;
                    subscribedProgression.ExperienceChanged -= HandleExperienceChanged;
                    subscribedProgression.LevelChanged -= HandleLevelChanged;
                }
                subscribedProgression = progression;
                xpNeedsSnap = true;
                lastXpStart = -1;
                if (subscribedProgression != null)
                {
                    subscribedProgression.MoneyChanged += HandleDoubloonsChanged;
                    subscribedProgression.ExperienceChanged += HandleExperienceChanged;
                    subscribedProgression.LevelChanged += HandleLevelChanged;
                }
            }
        }

        private void Unsubscribe()
        {
            if (subscribedInventory != null) subscribedInventory.GoldChanged -= HandleGoldChanged;
            if (subscribedProgression != null) subscribedProgression.MoneyChanged -= HandleDoubloonsChanged;
            if (subscribedProgression != null) subscribedProgression.ExperienceChanged -= HandleExperienceChanged;
            if (subscribedProgression != null) subscribedProgression.LevelChanged -= HandleLevelChanged;
            boundHealth = null;
            subscribedInventory = null;
            subscribedProgression = null;
            xpNeedsSnap = true;
            lastXpStart = -1;
        }

        private void RefreshAll()
        {
            if (coinSource == CoinSource.InventoryGold) HandleGoldChanged(inventory != null ? inventory.Gold : 0);
            else HandleDoubloonsChanged(progression != null ? progression.Doubloons : 0);

            if (progression != null)
            {
                HandleLevelChanged(progression.CurrentLevel);
                HandleExperienceChanged(progression.TotalExperience, progression.NextLevelXpThreshold);
            }
            else
            {
                if (levelText != null) levelText.text = "LEVEL  --";
                if (experienceText != null) experienceText.text = "XP  -- / --";
                ApplyExperienceFill(0f, -1);
            }
            RefreshSkull();
            RefreshMap();
        }

        private void HandleGoldChanged(int amount)
        {
            if (coinSource == CoinSource.InventoryGold && coinsText != null)
                coinsText.text = $"{coinsLabel}  {amount:N0}";
        }

        private void HandleDoubloonsChanged(int amount)
        {
            if (coinSource == CoinSource.ProgressionDoubloons && coinsText != null)
                coinsText.text = $"{coinsLabel}  {amount:N0}";
        }

        private void HandleLevelChanged(int level)
        {
            if (levelText != null) levelText.text = $"LEVEL  {level}";
        }

        private void HandleExperienceChanged(int totalExperience, int nextLevelThreshold)
        {
            if (progression == null) return;

            int start = progression.CurrentLevelXpStart;

            if (nextLevelThreshold <= 0)
            {
                ApplyExperienceFill(1f, start);
                if (experienceText != null) experienceText.text = "LEGENDARY RANK";
                return;
            }

            int required = Mathf.Max(1, nextLevelThreshold - start);
            int earned = Mathf.Clamp(totalExperience - start, 0, required);
            ApplyExperienceFill(earned / (float)required, start);
            if (experienceText != null) experienceText.text = $"XP  {earned:N0} / {required:N0}";
        }

        /// <summary>
        /// Routes an XP fraction to the gain trail. A rise in the level's starting XP means
        /// the player levelled up, so the ring fills to full, resets, then continues.
        /// </summary>
        private void ApplyExperienceFill(float fraction, int xpStart)
        {
            bool snap = xpNeedsSnap;
            bool levelUp = !snap && lastXpStart >= 0 && xpStart > lastXpStart;
            xpNeedsSnap = false;
            lastXpStart = xpStart;

            if (experienceTrail == null)
            {
                if (experienceFill != null) experienceFill.fillAmount = fraction;
                return;
            }

            if (snap) experienceTrail.Snap(fraction);
            else if (levelUp) experienceTrail.LevelUp(fraction);
            else experienceTrail.Set(fraction);
        }

        private void RefreshMap()
        {
            if (mapImage == null) return;

            mapImage.texture = mapTexture;
            // Keep the parchment and X visible until a RenderTexture is assigned.
            mapImage.color = mapTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        private void RefreshSkull()
        {
            bool hasCustomSprite = pirateSkullSprite != null;
            if (skullImage != null)
            {
                skullImage.sprite = pirateSkullSprite;
                skullImage.enabled = hasCustomSprite;
            }
            if (skullFallback != null && skullFallback.activeSelf != !hasCustomSprite)
                skullFallback.SetActive(!hasCustomSprite);
        }

        private void UpdateMarker()
        {
            if (playerMarker == null || player == null) return;
            playerMarker.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
        }
    }
}
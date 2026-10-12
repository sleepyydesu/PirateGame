using UnityEngine;
using UnityEngine.InputSystem;

namespace PirateGame.Quests
{
    /// <summary>
    /// A world item that exists only while its condition holds (e.g. Quest A in Active_Search).
    /// Glows when the player is near, is picked up with Q, reports "item_collected:&lt;itemId&gt;"
    /// and respawns at its original spot whenever the condition becomes true again — which
    /// covers the "item lost/dropped" edge case.
    /// </summary>
    public class QuestItemPickup : Interactable
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(1)] private int amount = 1;
        [Tooltip("Defaults to 'item_collected:<itemId>'")]
        [SerializeField] private string pickupEvent;
        [SerializeField] private QuestStateCondition visibleWhen = new QuestStateCondition();

        [Header("Visuals")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Light glowLight;
        [SerializeField] private Renderer[] glowRenderers;
        [SerializeField] private Color glowColor = new Color(1f, 0.75f, 0.3f);
        [SerializeField] private float highlightRange = 12f;
        [SerializeField] private float maxLightIntensity = 3f;
        [SerializeField] private float emissionStrength = 4f;
        [SerializeField] private float spinSpeed = 35f;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool visible = true;
        private float highlight;
        private MaterialPropertyBlock mpb;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public ItemDefinition Item => item;
        public string PickupEvent => string.IsNullOrEmpty(pickupEvent) && item != null ? "item_collected:" + item.Id : pickupEvent;
        public bool IsVisible => visible;

        private void Reset()
        {
            key = Key.Q;
            promptText = "Pick up";
            range = 2.5f;
        }

        private void Awake()
        {
            if (visualRoot == null && transform.childCount > 0) visualRoot = transform.GetChild(0).gameObject;
            homePosition = transform.position;
            homeRotation = transform.rotation;
            mpb = new MaterialPropertyBlock();
        }

        private void Start()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed += Refresh;
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed -= Refresh;
            if (PlayerInventory.Instance != null) PlayerInventory.Instance.Changed -= Refresh;
        }

        public override string PromptText => item != null ? $"Pick up {item.displayName}" : promptText;

        public override bool CanInteract(GameObject player) => visible;

        public override void Interact(GameObject player)
        {
            if (!visible) return;
            PlayerInventory.Instance?.Add(item, amount);
            QuestAudio.Play(QuestSound.Pickup);
            QuestUI.Toast("Item obtained", item != null ? item.displayName : "Item", ToastKind.Item);
            SetVisible(false);
            QuestManager.Instance?.Report(PickupEvent);
        }

        private void Refresh()
        {
            QuestManager m = QuestManager.Instance;
            bool carrying = PlayerInventory.Instance != null && PlayerInventory.Instance.Has(item);
            bool should = m != null && visibleWhen.Evaluate(m) && !carrying;
            if (should && !visible) Respawn();
            SetVisible(should);
        }

        /// <summary>Put the item back at its original spot.</summary>
        public void Respawn()
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        private void SetVisible(bool v)
        {
            visible = v;
            if (visualRoot != null) visualRoot.SetActive(v);
            if (glowLight != null) glowLight.enabled = v;
        }

        private void Update()
        {
            if (!visible) return;

            float target = 0f;
            if (QuestPlayer.TryGetPosition(out Vector3 p))
                target = Mathf.Clamp01(1f - (Vector3.Distance(p, transform.position) - range) / Mathf.Max(0.1f, highlightRange - range));
            highlight = Mathf.MoveTowards(highlight, target, Time.deltaTime * 2f);

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 4f);
            float k = Mathf.Lerp(0.15f, 1f, highlight) * pulse;

            if (glowLight != null)
            {
                glowLight.color = glowColor;
                glowLight.intensity = maxLightIntensity * k;
            }

            if (glowRenderers != null)
            {
                Color e = glowColor * (emissionStrength * k);
                foreach (Renderer r in glowRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(EmissionId, e);
                    r.SetPropertyBlock(mpb);
                }
            }

            if (visualRoot != null)
            {
                visualRoot.transform.Rotate(0f, spinSpeed * highlight * Time.deltaTime, 0f, Space.World);
                Vector3 lp = visualRoot.transform.localPosition;
                lp.y = 0.15f * highlight * (0.5f + 0.5f * Mathf.Sin(Time.time * 2f));
                visualRoot.transform.localPosition = lp;
            }
        }

        // Builder API
        public void Setup(ItemDefinition def, QuestStateCondition condition, GameObject visuals, Light light, Renderer[] glow)
        {
            item = def; visibleWhen = condition; visualRoot = visuals; glowLight = light; glowRenderers = glow;
            key = Key.Q; promptText = "Pick up"; range = 2.5f;
        }
    }
}

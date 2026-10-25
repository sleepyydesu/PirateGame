using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Opens the illustrated world map when the TreasureMap section on PirateHUD is clicked,
/// closes it through the close button or Escape, and positions the player marker on the artwork.
/// Attach this component to the always-active WorldMapCanvas, not FullWorldMapPanel.
/// </summary>
public sealed class IllustratedWorldMapController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject fullWorldMapPanel;
    [SerializeField] private Image combinedMapImage;
    [SerializeField] private RectTransform playerMarker;
    [Tooltip("The clickable TreasureMap section on the existing PirateHUD.")]
    [SerializeField] private Button treasureMapButton;
    [SerializeField] private Button closeWorldMapButton;

    [Header("Player")]
    [SerializeField] private Transform player;

    [Header("World Area Represented by the Entire Image")]
    [Tooltip("Lowest Unity world X coordinate represented by the left edge of the map.")]
    [SerializeField] private float worldMinX = -100f;
    [Tooltip("Highest Unity world X coordinate represented by the right edge of the map.")]
    [SerializeField] private float worldMaxX = 100f;
    [Tooltip("Lowest Unity world Z coordinate represented by the bottom edge of the map.")]
    [SerializeField] private float worldMinZ = -100f;
    [Tooltip("Highest Unity world Z coordinate represented by the top edge of the map.")]
    [SerializeField] private float worldMaxZ = 100f;

    [Header("Map Alignment")]
    [Tooltip("Keep the marker at the nearest edge if the player moves outside the configured world bounds.")]
    [SerializeField] private bool clampMarkerToMapEdges = true;
    [Tooltip("Enable if increasing Unity X should move the marker left on the illustration.")]
    [SerializeField] private bool invertMapX = false;
    [Tooltip("Enable if increasing Unity Z should move the marker down on the illustration.")]
    [SerializeField] private bool invertMapZ = false;

    private void Awake()
    {
        if (playerMarker != null)
        {
            playerMarker.anchorMin = new Vector2(0.5f, 0.5f);
            playerMarker.anchorMax = new Vector2(0.5f, 0.5f);
            playerMarker.pivot = new Vector2(0.5f, 0.5f);
        }
    }

    private void OnEnable()
    {
        if (treasureMapButton != null)
            treasureMapButton.onClick.AddListener(OpenMap);

        if (closeWorldMapButton != null)
            closeWorldMapButton.onClick.AddListener(CloseMap);
    }

    private void Start()
    {
        // The full-screen map starts closed; the TreasureMap section on PirateHUD stays visible.
        SetMapVisible(false);
        UpdatePlayerMarker();
    }

    private void Update()
    {
        if (!IsMapOpen || !EscapePressedThisFrame())
            return;

        CloseMap();
    }

    private void LateUpdate()
    {
        UpdatePlayerMarker();
    }

    private void OnDisable()
    {
        if (treasureMapButton != null)
            treasureMapButton.onClick.RemoveListener(OpenMap);

        if (closeWorldMapButton != null)
            closeWorldMapButton.onClick.RemoveListener(CloseMap);
    }

    public void OpenMap()
    {
        SetMapVisible(true);
    }

    public void CloseMap()
    {
        SetMapVisible(false);
    }

    public void ToggleMap()
    {
        SetMapVisible(!IsMapOpen);
    }

    private bool IsMapOpen => fullWorldMapPanel != null && fullWorldMapPanel.activeSelf;

    private void SetMapVisible(bool visible)
    {
        if (fullWorldMapPanel != null && fullWorldMapPanel.activeSelf != visible)
            fullWorldMapPanel.SetActive(visible);

        // Hide the entire TreasureMap HUD section while the full-screen map is open.
        // The controller lives on WorldMapCanvas, so it remains active while this button is hidden.
        if (treasureMapButton != null && treasureMapButton.gameObject.activeSelf == visible)
            treasureMapButton.gameObject.SetActive(!visible);
    }

    private static bool EscapePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void UpdatePlayerMarker()
    {
        if (player == null || combinedMapImage == null || playerMarker == null || combinedMapImage.sprite == null)
            return;

        float xRange = worldMaxX - worldMinX;
        float zRange = worldMaxZ - worldMinZ;
        if (xRange <= 0.001f || zRange <= 0.001f)
            return;

        float u = (player.position.x - worldMinX) / xRange;
        float v = (player.position.z - worldMinZ) / zRange;

        bool insideBounds = u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        if (!insideBounds && !clampMarkerToMapEdges)
        {
            if (playerMarker.gameObject.activeSelf)
                playerMarker.gameObject.SetActive(false);
            return;
        }

        if (clampMarkerToMapEdges)
        {
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
        }

        if (invertMapX) u = 1f - u;
        if (invertMapZ) v = 1f - v;

        if (!playerMarker.gameObject.activeSelf)
            playerMarker.gameObject.SetActive(true);

        Vector2 displayedSize = GetDisplayedSpriteSize(combinedMapImage);
        playerMarker.anchoredPosition = new Vector2(
            (u - 0.5f) * displayedSize.x,
            (v - 0.5f) * displayedSize.y
        );
    }

    /// <summary>Accounts for Image.preserveAspect, which letterboxes the image inside its RectTransform.</summary>
    private static Vector2 GetDisplayedSpriteSize(Image image)
    {
        Rect rect = image.rectTransform.rect;
        Sprite sprite = image.sprite;

        if (sprite == null || rect.width <= 0f || rect.height <= 0f)
            return rect.size;

        float spriteAspect = sprite.rect.width / sprite.rect.height;
        float containerAspect = rect.width / rect.height;

        if (containerAspect > spriteAspect)
            return new Vector2(rect.height * spriteAspect, rect.height);

        return new Vector2(rect.width, rect.width / spriteAspect);
    }
}

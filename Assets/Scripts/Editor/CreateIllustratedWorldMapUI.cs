#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates the illustrated full-screen world map UI for PirateGame. The existing HUD TreasureMap opens it.
/// Menu: Tools > Pirate Game > Create Illustrated World Map UI
///
/// Put this file inside an Editor folder (for example Assets/Editor/).
/// Requires IllustratedWorldMapController.cs in the regular Assets folder.
/// </summary>
public static class CreateIllustratedWorldMapUI
{
    private const string CreateMenu = "Tools/Pirate Game/Create Illustrated World Map UI";
    private const string RebuildMenu = "Tools/Pirate Game/Rebuild Illustrated World Map UI";
    private const string MarkerSpritePath = "Assets/PirateGameUI/Generated/WorldMapPlayerMarker.png";

    private static readonly Color Navy = Hex("#101D2A");
    private static readonly Color NavySoft = Hex("#172B3B");
    private static readonly Color Brass = Hex("#D4A64A");
    private static readonly Color Parchment = Hex("#E7C982");
    private static readonly Color Ink = Hex("#382819");
    private static readonly Color TextColor = Hex("#FFF1CD");
    private static readonly Color PirateRed = Hex("#B8473D");

    [MenuItem(CreateMenu)]
    public static void Create()
    {
        CreateInternal(false);
    }

    [MenuItem(RebuildMenu)]
    public static void Rebuild()
    {
        if (!EditorUtility.DisplayDialog(
                "Rebuild Illustrated World Map UI?",
                "This will delete the current WorldMapCanvas object in the active scene and create it again. Any manual changes to that canvas will be lost. Your map image asset will not be deleted.",
                "Rebuild UI", "Cancel"))
            return;

        CreateInternal(true);
    }

    private static void CreateInternal(bool replaceExisting)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            EditorUtility.DisplayDialog(
                "Illustrated World Map UI",
                "Exit Play Mode and open a normal gameplay scene before creating the UI.",
                "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject existing = FindSceneObject("WorldMapCanvas", scene);
        if (existing != null)
        {
            if (!replaceExisting)
            {
                Selection.activeGameObject = existing;
                EditorUtility.DisplayDialog(
                    "World Map UI Already Exists",
                    "WorldMapCanvas already exists in this scene. Select it in the Hierarchy, or use Tools > Pirate Game > Rebuild Illustrated World Map UI if you want to replace it.",
                    "OK");
                return;
            }

        }

        if (TMP_Settings.defaultFontAsset == null)
        {
            EditorUtility.DisplayDialog(
                "TextMeshPro Resources Required",
                "Import TextMeshPro Essential Resources first: Window > TextMeshPro > Import TMP Essential Resources. Then run this command again.",
                "OK");
            return;
        }

        Sprite mapSprite = FindAndImportCombinedMapSprite();
        if (mapSprite == null)
        {
            EditorUtility.DisplayDialog(
                "Combined Map Image Not Found",
                "Import your combined island artwork into the Unity project first. The image should be named Combined_Pirate_Map.jpg (or Combined_Pirate_Map.png) and imported as a Sprite. The creator searches the Assets folder for that filename.",
                "OK");
            return;
        }

        // The full-screen map is opened from the TreasureMap section on the existing HUD.
        GameObject pirateHud = FindSceneObject("PirateHUD", scene);
        Transform treasureMapTransform = FindDescendant(pirateHud != null ? pirateHud.transform : null, "TreasureMap");
        if (treasureMapTransform == null)
        {
            EditorUtility.DisplayDialog(
                "Pirate HUD Not Found",
                "Create the PirateHUD first (Tools > Pirate Game > Create Pirate HUD), then create/rebuild the illustrated world map. The full map will use the TreasureMap section on that HUD as its open button.",
                "OK");
            return;
        }

        Button treasureMapButton = EnsureTreasureMapButton(treasureMapTransform);

        EnsureFolder("Assets/PirateGameUI");
        EnsureFolder("Assets/PirateGameUI/Generated");
        Sprite markerSprite = EnsurePlayerMarkerSprite();
        if (markerSprite == null)
        {
            EditorUtility.DisplayDialog("Marker Sprite Error", "The player marker sprite could not be created. Check the Console for details.", "OK");
            return;
        }

        // Only destroy the previous canvas after all prerequisites have been validated.
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        // Root Canvas.
        GameObject canvasObject = new GameObject(
            "WorldMapCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Illustrated World Map UI");

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Full-screen panel. It remains active in Edit Mode so the artwork can be previewed;
        // IllustratedWorldMapController closes it automatically when Play Mode starts.
        GameObject panelObject = CreateUIObject("FullWorldMapPanel", canvasObject.transform);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        Stretch(panelRect, Vector2.zero, Vector2.zero);
        Image panelBackground = panelObject.AddComponent<Image>();
        panelBackground.color = new Color(Navy.r, Navy.g, Navy.b, 0.97f);
        panelBackground.raycastTarget = true;

        // A brass backing/frame behind the image.
        GameObject frameObject = CreateUIObject("MapFrame", panelObject.transform);
        RectTransform frameRect = frameObject.GetComponent<RectTransform>();
        Stretch(frameRect, new Vector2(18f, 24f), new Vector2(-18f, -76f));
        Image frameImage = frameObject.AddComponent<Image>();
        frameImage.sprite = GetBuiltinUISprite();
        frameImage.type = Image.Type.Sliced;
        frameImage.color = Brass;
        frameImage.raycastTarget = false;
        AspectRatioFitter frameAspect = frameObject.AddComponent<AspectRatioFitter>();
        frameAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        frameAspect.aspectRatio = mapSprite.rect.width / mapSprite.rect.height;

        // The original combined illustration. Preserve Aspect is essential because the image is wide.
        GameObject mapObject = CreateUIObject("CombinedMapImage", panelObject.transform);
        RectTransform mapRect = mapObject.GetComponent<RectTransform>();
        Stretch(mapRect, new Vector2(24f, 30f), new Vector2(-24f, -82f));
        Image mapImage = mapObject.AddComponent<Image>();
        mapImage.sprite = mapSprite;
        mapImage.type = Image.Type.Simple;
        mapImage.preserveAspect = true;
        mapImage.color = Color.white;
        mapImage.raycastTarget = false;
        AspectRatioFitter mapAspect = mapObject.AddComponent<AspectRatioFitter>();
        mapAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        mapAspect.aspectRatio = mapSprite.rect.width / mapSprite.rect.height;

        // Player marker is a child of the map image. The runtime controller maps world X/Z to its position.
        GameObject markerObject = CreateUIObject("PlayerMarker", mapObject.transform);
        RectTransform markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;
        markerRect.sizeDelta = new Vector2(32f, 32f);
        Image markerImage = markerObject.AddComponent<Image>();
        markerImage.sprite = markerSprite;
        markerImage.preserveAspect = true;
        markerImage.raycastTarget = false;

        // Decorative title plate.
        GameObject titlePlate = CreateUIObject("WorldMapTitlePlate", panelObject.transform);
        RectTransform titlePlateRect = titlePlate.GetComponent<RectTransform>();
        SetAnchored(titlePlateRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(410f, 48f));
        Image titlePlateImage = titlePlate.AddComponent<Image>();
        titlePlateImage.sprite = GetBuiltinUISprite();
        titlePlateImage.type = Image.Type.Sliced;
        titlePlateImage.color = Parchment;
        titlePlateImage.raycastTarget = false;
        AddOutline(titlePlateImage, Brass, 2f);
        CreateTMPLabel(titlePlate.transform, "Title", "CHART OF THE ISLES", 24f, Ink,
            TextAlignmentOptions.Center);

        // Close button, at the top right of the map panel.
        Button closeButton = CreatePirateButton(
            panelObject.transform,
            "CloseWorldMapButton",
            "CLOSE MAP",
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-34f, -28f),
            new Vector2(148f, 46f));

        // Opening is handled by the TreasureMap panel on the existing PirateHUD;
        // no separate WORLD MAP button is created.

        // The player can be auto-assigned if the scene object is tagged Player.
        Transform playerTransform = FindPlayerTransform();
        markerObject.SetActive(playerTransform != null);

        // Attach controller and populate its private serialized fields without changing its code.
        IllustratedWorldMapController controller = canvasObject.AddComponent<IllustratedWorldMapController>();
        SerializedObject serializedController = new SerializedObject(controller);
        SetObjectReference(serializedController, "fullWorldMapPanel", panelObject);
        SetObjectReference(serializedController, "combinedMapImage", mapImage);
        SetObjectReference(serializedController, "playerMarker", markerRect);
        SetObjectReference(serializedController, "treasureMapButton", treasureMapButton);
        SetObjectReference(serializedController, "closeWorldMapButton", closeButton);
        SetObjectReference(serializedController, "player", playerTransform);
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        // Re-run OnEnable after assigning the references so the controller can register button listeners.
        controller.enabled = false;
        controller.enabled = true;

        EnsureEventSystem(scene);

        Selection.activeGameObject = canvasObject;
        EditorSceneManager.MarkSceneDirty(scene);

        if (playerTransform == null)
        {
            Debug.LogWarning(
                "World Map UI created, but no active GameObject tagged 'Player' was found. Select WorldMapCanvas and assign the Player Transform on IllustratedWorldMapController.",
                canvasObject);
        }

        Debug.Log(
            "Illustrated World Map UI created. Click PirateHUD > TreasureMap to open it; use CLOSE MAP or Escape to close it. Calibrate World Min/Max X/Z so the marker aligns with the illustrated islands.",
            canvasObject);
    }

    /// <summary>
    /// Makes the existing HUD TreasureMap root clickable without changing its artwork.
    /// Its existing child images have raycastTarget disabled, so this transparent Image
    /// acts as the raycast surface for the Button.
    /// </summary>
    private static Button EnsureTreasureMapButton(Transform treasureMapTransform)
    {
        Image hitTarget = treasureMapTransform.GetComponent<Image>();
        if (hitTarget == null)
            hitTarget = Undo.AddComponent<Image>(treasureMapTransform.gameObject);

        hitTarget.sprite = GetBuiltinUISprite();
        hitTarget.type = Image.Type.Simple;
        hitTarget.color = new Color(1f, 1f, 1f, 0f);
        hitTarget.raycastTarget = true;

        Button button = treasureMapTransform.GetComponent<Button>();
        if (button == null)
            button = Undo.AddComponent<Button>(treasureMapTransform.gameObject);

        button.targetGraphic = hitTarget;
        button.transition = Selectable.Transition.None;
        button.interactable = true;

        return button;
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDescendant(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Button CreatePirateButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 position,
        Vector2 size)
    {
        GameObject buttonObject = CreateUIObject(name, parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetAnchored(rect, anchorMin, anchorMax, position, size);

        Image image = buttonObject.AddComponent<Image>();
        image.sprite = GetBuiltinUISprite();
        image.type = Image.Type.Sliced;
        image.color = NavySoft;
        image.raycastTarget = true;
        AddOutline(image, Brass, 2f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = NavySoft;
        colors.highlightedColor = Hex("#284353");
        colors.pressedColor = Hex("#0C1720");
        colors.selectedColor = Hex("#284353");
        colors.disabledColor = new Color(NavySoft.r, NavySoft.g, NavySoft.b, 0.45f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        CreateTMPLabel(buttonObject.transform, "Label", label, 18f, Parchment,
            TextAlignmentOptions.Center);
        return button;
    }

    private static TextMeshProUGUI CreateTMPLabel(
        Transform parent,
        string name,
        string text,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateUIObject(name, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(6f, 4f);
        rect.offsetMax = new Vector2(-6f, -4f);

        TextMeshProUGUI tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        tmp.fontStyle = FontStyles.Bold;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return tmp;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void AddOutline(Graphic graphic, Color color, float thickness)
    {
        Outline outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, -thickness);
    }

    private static Sprite FindAndImportCombinedMapSprite()
    {
        string[] guids = AssetDatabase.FindAssets("Combined_Pirate_Map");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            string baseName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(baseName, "Combined_Pirate_Map", StringComparison.OrdinalIgnoreCase))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
                return sprite;
        }

        return null;
    }

    private static Sprite EnsurePlayerMarkerSprite()
    {
        if (!File.Exists(MarkerSpritePath))
        {
            EnsureFolder("Assets/PirateGameUI");
            EnsureFolder("Assets/PirateGameUI/Generated");

            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "WorldMapPlayerMarker_Generated";
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

            // Draw a clean compass-like target: dark outline, brass ring, parchment ring, red center.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    Color pixel = Color.clear;
                    if (distance <= 43f) pixel = Ink;
                    if (distance <= 38f) pixel = Brass;
                    if (distance <= 29f) pixel = TextColor;
                    if (distance <= 19f) pixel = PirateRed;
                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(MarkerSpritePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(MarkerSpritePath, ImportAssetOptions.ForceUpdate);
        }

        TextureImporter markerImporter = AssetImporter.GetAtPath(MarkerSpritePath) as TextureImporter;
        if (markerImporter == null)
            return null;

        bool settingsChanged = false;
        if (markerImporter.textureType != TextureImporterType.Sprite)
        {
            markerImporter.textureType = TextureImporterType.Sprite;
            settingsChanged = true;
        }
        if (markerImporter.spriteImportMode != SpriteImportMode.Single)
        {
            markerImporter.spriteImportMode = SpriteImportMode.Single;
            settingsChanged = true;
        }
        if (!markerImporter.alphaIsTransparency)
        {
            markerImporter.alphaIsTransparency = true;
            settingsChanged = true;
        }
        if (markerImporter.mipmapEnabled)
        {
            markerImporter.mipmapEnabled = false;
            settingsChanged = true;
        }
        if (markerImporter.filterMode != FilterMode.Bilinear)
        {
            markerImporter.filterMode = FilterMode.Bilinear;
            settingsChanged = true;
        }
        if (settingsChanged)
            markerImporter.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(MarkerSpritePath);
    }

    private static void EnsureEventSystem(Scene scene)
    {
        EventSystem existing = null;
        EventSystem[] eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < eventSystems.Length; i++)
        {
            if (eventSystems[i] != null && eventSystems[i].gameObject.scene == scene)
            {
                existing = eventSystems[i];
                break;
            }
        }

        if (existing == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem for World Map UI");
            existing = eventSystemObject.GetComponent<EventSystem>();
        }

        if (existing.GetComponent<BaseInputModule>() != null)
            return;

        // Prefer the New Input System UI module when its package is installed,
        // while avoiding a hard compile-time dependency on that package.
        Type inputSystemModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputSystemModule != null && typeof(BaseInputModule).IsAssignableFrom(inputSystemModule))
        {
            existing.gameObject.AddComponent(inputSystemModule);
        }
        else
        {
            existing.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    private static Transform FindPlayerTransform()
    {
        try
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                return player.transform;
        }
        catch (UnityException)
        {
            // The project may not currently define a Player tag; leave the field for manual assignment.
        }

        return null;
    }

    private static GameObject FindSceneObject(string objectName, Scene scene)
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].name == objectName && objects[i].scene == scene)
                return objects[i];
        }
        return null;
    }

    private static Sprite GetBuiltinUISprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
            AssetDatabase.CreateFolder(parent, folderName);
    }

    private static Color Hex(string html)
    {
        ColorUtility.TryParseHtmlString(html, out Color color);
        return color;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError("IllustratedWorldMapController is missing serialized field '" + propertyName + "'.");
            return;
        }

        property.objectReferenceValue = value;
    }
}
#endif

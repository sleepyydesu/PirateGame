#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PirateGame.Combat;
using PirateGame.Enemies;

namespace PirateGame.UI.Editor
{
    /// <summary>
    /// Creates a scene-local, screen-space pirate HUD.
    /// Top-left : circular XP medallion (ring fill + gain trail + skull + level badge) with tightly stacked,
    ///            cut-corner Health (damage trail) and Coins bars.
    /// Top-right: circular treasure map.
    /// </summary>
    public static class PirateHudCreator
    {
        private const string Menu = "Tools/Pirate Game/Create Pirate HUD";
        private const string SpritePath = "Assets/PirateGameUI/Generated/PirateHudWhite.png";
        private const string CircleSpritePath = "Assets/PirateGameUI/Generated/PirateHudCircle.png";
        private const string RingSpritePath = "Assets/PirateGameUI/Generated/PirateHudXpRing.png";
        private const string CutSpritePath = "Assets/PirateGameUI/Generated/PirateHudCut.png";
        // Put the supplied skull image here (transparent PNG).
        private const string SkullSpritePath = "Assets/PirateGameUI/PirateSkull.png";

        private static readonly Color Navy = Hex("#101D2A");
        private static readonly Color NavySoft = Hex("#172B3B");
        private static readonly Color Brass = Hex("#D4A64A");
        private static readonly Color Parchment = Hex("#E7C982");
        private static readonly Color Ink = Hex("#382819");
        private static readonly Color Red = Hex("#B8473D");
        private static readonly Color Text = Hex("#FFF1CD");
        private static readonly Color DamageTrail = Hex("#F2C46B");

        [MenuItem(Menu)]
        public static void Create()
        {
            CreateHud(false);
        }

        [MenuItem("Tools/Pirate Game/Rebuild Pirate HUD")]
        public static void Rebuild()
        {
            CreateHud(true);
        }

        private static void CreateHud(bool replaceExisting)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorUtility.DisplayDialog("Pirate HUD", "Exit Play Mode and open a gameplay scene first.", "OK");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.name == "PirateHUD" && canvas.gameObject.scene == scene)
                {
                    if (replaceExisting)
                    {
                        Undo.DestroyObjectImmediate(canvas.gameObject);
                        break;
                    }

                    Selection.activeGameObject = canvas.gameObject;
                    EditorUtility.DisplayDialog("Pirate HUD", "PirateHUD already exists in this scene.", "OK");
                    return;
                }
            }

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                EditorUtility.DisplayDialog("TMP resources required", "Import TextMeshPro Essential Resources, then run this command again.", "OK");
                return;
            }

            Sprite white = EnsureWhiteSprite();
            Sprite circle = EnsureShapeSprite(CircleSpritePath, false);
            Sprite ring = EnsureShapeSprite(RingSpritePath, true);
            Sprite cut = EnsureCutSprite();
            if (white == null || circle == null || ring == null || cut == null) return;
            Sprite skull = LoadSkullSprite();

            GameObject root = new GameObject("PirateHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PirateHudController));
            Canvas canvasRoot = root.GetComponent<Canvas>();
            canvasRoot.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRoot.sortingOrder = 25;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            PirateHudController hud = root.GetComponent<PirateHudController>();
            CreateStatus(root.transform, white, cut, circle, ring, skull, font, hud);
            CreateMap(root.transform, white, cut, circle, font, hud);
            CreateBossBar(root.transform, white, cut, font);

            // The controller owns the skull sprite and re-applies it in RefreshSkull(),
            // so the sprite must be stored on the controller or it would be cleared.
            if (skull != null) SetPrivate(hud, "pirateSkullSprite", skull);

            Undo.RegisterCreatedObjectUndo(root, "Create Pirate HUD");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);
            if (skull == null)
                Debug.LogWarning("Skull image not found at " + SkullSpritePath + ". A simple fallback skull was used. Add the PNG there and run Tools > Pirate Game > Rebuild Pirate HUD.", root);
            Debug.Log("Pirate HUD created. It automatically finds the Player-tagged Health and PlayerInventory. Assign a minimap RenderTexture on Pirate HUD > Pirate Hud Controller when ready.", root);
        }

        // ------------------------------------------------------------------
        // TOP-LEFT: medallion + stacked cut-corner bars (no gaps, like the reference)
        // ------------------------------------------------------------------
        private static void CreateStatus(Transform parent, Sprite white, Sprite cut, Sprite circle, Sprite ring, Sprite skull, TMP_FontAsset font, PirateHudController hud)
        {
            const float emblemSize = 160f;          // brass frame diameter
            const float cx = emblemSize * 0.5f;     // medallion centre (x and -y) inside the status rect
            const float barLeft = 100f;             // bars start tucked UNDER the medallion
            const float textInset = 68f;            // content starts just right of the medallion edge

            RectTransform status = Rect(parent, "CaptainStatus", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(500f, 205f));

            // ---- Bars are created FIRST so the medallion draws on top of them ----

            // Health bar (top)
            RectTransform healthPanel = Box(status, "HealthPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(barLeft, -32f), new Vector2(380f, 36f), cut, Navy);
            Outline(healthPanel.GetComponent<Image>(), Brass, 2f);

            RectTransform track = Rect(healthPanel, "HealthTrack", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(textInset, 0f), new Vector2(304f, 24f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = cut;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = Ink;
            trackImage.raycastTarget = false;
            track.gameObject.AddComponent<Mask>();   // clips both fills to the cut corners

            // Damage trail first so it sits BEHIND the real fill
            Image trail = StretchImage(track, "HealthTrail", white, DamageTrail, 0f);
            trail.type = Image.Type.Filled;
            trail.fillMethod = Image.FillMethod.Horizontal;
            trail.fillOrigin = (int)Image.OriginHorizontal.Left;
            trail.fillAmount = 1f;

            Image fill = StretchImage(track, "HealthFill", white, Red, 0f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            TMP_Text hp = Label(track, "Value", "100 / 100", font, 17, Text, Vector2.zero, new Vector2(304f, 24f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));

            // Your existing HealthBarUI drives the fill, gold damage trail and numbers.
            // PirateHudController hands it the player's Health at runtime.
            HealthBarUI healthBar = track.gameObject.AddComponent<HealthBarUI>();
            healthBar.healthFill = fill;
            healthBar.damageTrail = trail;
            healthBar.valueText = hp;
            healthBar.showNumbers = true;
            healthBar.damageDelay = 0.45f;
            healthBar.trailSpeed = 0.6f;

            // Coins bar (directly under health, borders overlap slightly so it reads as one unit)
            RectTransform coinsPanel = Box(status, "CoinsPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(barLeft, -66f), new Vector2(300f, 32f), cut, Navy);
            Outline(coinsPanel.GetComponent<Image>(), Brass, 2f);
            Image coin = CreateImage(coinsPanel, "Coin", circle, Brass, new Vector2(textInset, 0f), new Vector2(22f, 22f), new Vector2(0f, 0.5f));
            Label(coin.transform, "Mark", "D", font, 14, Ink, Vector2.zero, new Vector2(22f, 22f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));
            TMP_Text coins = Label(coinsPanel, "Coins", "DOUBLOONS  0", font, 17, Text, new Vector2(textInset + 30f, 0f), new Vector2(200f, 28f), TextAlignmentOptions.Left, new Vector2(0f, 0.5f));

            // ---- Medallion (drawn over the bars) ----
            RectTransform emblem = Rect(status, "ExperienceEmblem", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(cx, -cx), new Vector2(emblemSize, emblemSize));
            CreateImage(emblem, "BrassMedallion", circle, Brass, Vector2.zero, new Vector2(emblemSize, emblemSize), new Vector2(0.5f, 0.5f));
            CreateImage(emblem, "ExperienceTrack", ring, Ink, Vector2.zero, new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));

            // Circular inset that clips the skull artwork to the circle
            Image inset = CreateImage(emblem, "SeaInset", circle, NavySoft, Vector2.zero, new Vector2(114f, 114f), new Vector2(0.5f, 0.5f));
            inset.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            RectTransform skullLayer = Rect(inset.transform, "SkullLayer", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(114f, 114f));
            GameObject fallback = CreateSkullFallback(skullLayer, circle, white);
            Image skullImage = CreateImage(skullLayer, "CustomSkull", skull != null ? skull : white, Color.white, Vector2.zero, new Vector2(126f, 100f), new Vector2(0.5f, 0.5f));
            skullImage.preserveAspect = true;
            skullImage.enabled = skull != null;
            fallback.SetActive(skull == null);

            // XP gain preview ring (white, jumps instantly) sits BEHIND the real XP ring
            Image xpGain = CreateImage(emblem, "ExperienceGain", ring, Color.white, Vector2.zero, new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));
            xpGain.type = Image.Type.Filled;
            xpGain.fillMethod = Image.FillMethod.Radial360;
            xpGain.fillOrigin = (int)Image.Origin360.Top;
            xpGain.fillClockwise = true;
            xpGain.fillAmount = 0f;

            // Real XP ring (parchment) - catches up to the gain preview
            Image xpFill = CreateImage(emblem, "ExperienceRing", ring, Parchment, Vector2.zero, new Vector2(150f, 150f), new Vector2(0.5f, 0.5f));
            xpFill.type = Image.Type.Filled;
            xpFill.fillMethod = Image.FillMethod.Radial360;
            xpFill.fillOrigin = (int)Image.Origin360.Top;
            xpFill.fillClockwise = true;
            xpFill.fillAmount = 0f;

            HudFillTrail xpTrail = emblem.gameObject.AddComponent<HudFillTrail>();
            xpTrail.Init(xpFill, xpGain, HudFillTrail.Mode.GainTrail, 0.25f, 0.35f);

            // Level badge overlapping the bottom of the circle
            RectTransform rankPlate = Box(status, "RankPlate", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(cx, -(emblemSize - 4f)), new Vector2(112f, 26f), cut, Navy);
            Outline(rankPlate.GetComponent<Image>(), Brass, 2f);
            TMP_Text rank = Label(rankPlate, "Rank", "LEVEL  1", font, 15, Parchment, Vector2.zero, new Vector2(112f, 26f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));

            // XP numbers tucked right under the badge
            TMP_Text xp = Label(status, "Experience", "XP  0 / 100", font, 15, Parchment, new Vector2(cx - 90f, -(emblemSize + 20f)), new Vector2(180f, 22f), TextAlignmentOptions.Center, new Vector2(0f, 1f));

            SetPrivate(hud, "healthBar", healthBar);
            SetPrivate(hud, "levelText", rank);
            SetPrivate(hud, "experienceText", xp);
            SetPrivate(hud, "experienceFill", xpFill);
            SetPrivate(hud, "experienceTrail", xpTrail);
            SetPrivate(hud, "coinsText", coins);
            SetPrivate(hud, "skullImage", skullImage);
            SetPrivate(hud, "skullFallback", fallback);
        }

        // ------------------------------------------------------------------
        // TOP-RIGHT: circular map
        // ------------------------------------------------------------------
        private static void CreateMap(Transform parent, Sprite white, Sprite cut, Sprite circle, TMP_FontAsset font, PirateHudController hud)
        {
            RectTransform panel = Rect(parent, "TreasureMap", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(250f, 250f));
            CreateImage(panel, "Frame", circle, Brass, Vector2.zero, new Vector2(250f, 250f), new Vector2(0.5f, 0.5f));

            Image viewport = CreateImage(panel, "MapViewport", circle, Parchment, Vector2.zero, new Vector2(226f, 226f), new Vector2(0.5f, 0.5f));
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            RawImage raw = StretchRawImage(viewport.transform, "MapTexture", null, 0f);
            raw.color = new Color(1f, 1f, 1f, 0f);
            raw.raycastTarget = false;
            Label(viewport.transform, "Placeholder", "X", font, 72, new Color(Ink.r, Ink.g, Ink.b, 0.35f), Vector2.zero, new Vector2(226f, 226f), TextAlignmentOptions.Center);
            Image marker = CreateImage(viewport.transform, "PlayerMarker", white, Red, Vector2.zero, new Vector2(16f, 22f), new Vector2(0.5f, 0.5f));
            marker.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);

            // North marker sits on the brass rim
            Label(panel, "North", "N", font, 17, Ink, new Vector2(0f, -1f), new Vector2(30f, 22f), TextAlignmentOptions.Center, new Vector2(0.5f, 1f));

            // Small title plate overlapping the bottom of the circle
            RectTransform plate = Box(panel, "TitlePlate", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(150f, 24f), cut, Navy);
            Outline(plate.GetComponent<Image>(), Brass, 2f);
            Label(plate, "Title", "TREASURE MAP", font, 13, Brass, Vector2.zero, new Vector2(150f, 24f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));

            SetPrivate(hud, "mapImage", raw);
            SetPrivate(hud, "playerMarker", marker.rectTransform);
        }

        // ------------------------------------------------------------------
        // TOP-CENTER: boss health bar (fades in when the boss engages)
        // ------------------------------------------------------------------
        private static void CreateBossBar(Transform parent, Sprite white, Sprite cut, TMP_FontAsset font)
        {
            BossAI boss = Object.FindFirstObjectByType<BossAI>(FindObjectsInactive.Include);
            Health playerHealth = null;
            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null) playerHealth = taggedPlayer.GetComponentInChildren<Health>();

            GameObject rootObject = new GameObject("BossHealthBar", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -28f);
            root.sizeDelta = new Vector2(640f, 72f);

            TMP_Text nameText = Label(root, "Name", "BOSS", font, 24, Brass, Vector2.zero, new Vector2(640f, 30f), TextAlignmentOptions.Center, new Vector2(0.5f, 1f));

            RectTransform panel = Box(root, "BossPanel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(640f, 34f), cut, Navy);
            Outline(panel.GetComponent<Image>(), Brass, 2f);

            RectTransform track = Rect(panel, "BossTrack", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(624f, 22f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = cut;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = Ink;
            trackImage.raycastTarget = false;
            track.gameObject.AddComponent<Mask>();

            Image trail = StretchImage(track, "BossTrail", white, DamageTrail, 0f);
            trail.type = Image.Type.Filled;
            trail.fillMethod = Image.FillMethod.Horizontal;
            trail.fillOrigin = (int)Image.OriginHorizontal.Left;
            trail.fillAmount = 1f;

            Image fill = StretchImage(track, "BossFill", white, Red, 0f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            TMP_Text value = Label(track, "Value", "-- / --", font, 15, Text, Vector2.zero, new Vector2(624f, 22f), TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));

            // BossHealthUI requires a CanvasGroup and a HealthBarUI on the same object.
            HealthBarUI bar = rootObject.AddComponent<HealthBarUI>();
            bar.healthFill = fill;
            bar.damageTrail = trail;
            bar.nameText = nameText;
            bar.valueText = value;
            bar.showNumbers = true;
            bar.displayName = boss != null ? boss.name.Replace('_', ' ').ToUpperInvariant() : "BOSS";
            bar.damageDelay = 0.5f;
            bar.trailSpeed = 0.5f;

            BossHealthUI bossUi = rootObject.AddComponent<BossHealthUI>();
            bossUi.bar = bar;
            bossUi.group = rootObject.GetComponent<CanvasGroup>();
            bossUi.boss = boss;
            bossUi.playerHealth = playerHealth;

            if (boss == null)
                Debug.LogWarning("No BossAI found in the scene. Assign one on PirateHUD > BossHealthBar > Boss Health UI, or call SetBoss() at runtime.", rootObject);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Sprite sprite, Color color)
        {
            RectTransform rect = Rect(parent, name, anchor, pivot, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color, Vector2 position, Vector2 size, Vector2 anchor)
        {
            RectTransform rect = Rect(parent, name, anchor, anchor, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject CreateSkullFallback(Transform parent, Sprite circle, Sprite white)
        {
            RectTransform root = Rect(parent, "FallbackSkull", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            CreateImage(root, "Cranium", circle, Parchment, new Vector2(0f, 7f), new Vector2(60f, 58f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "Jaw", white, Parchment, new Vector2(0f, -25f), new Vector2(40f, 18f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "Bandana", white, Red, new Vector2(0f, 27f), new Vector2(62f, 8f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "LeftEye", circle, Ink, new Vector2(-14f, 8f), new Vector2(17f, 17f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "RightEye", circle, Ink, new Vector2(14f, 8f), new Vector2(17f, 17f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "Nose", white, Ink, new Vector2(0f, -7f), new Vector2(8f, 8f), new Vector2(0.5f, 0.5f));
            CreateImage(root, "Teeth", white, Ink, new Vector2(0f, -23f), new Vector2(25f, 3f), new Vector2(0.5f, 0.5f));
            return root.gameObject;
        }

        private static Image StretchImage(Transform parent, string name, Sprite sprite, Color color, float inset)
        {
            RectTransform rect = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage StretchRawImage(Transform parent, string name, Texture texture, float inset)
        {
            RectTransform rect = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string value, TMP_FontAsset font, float size, Color color, Vector2 position, Vector2 bounds, TextAlignmentOptions alignment, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform rect = Rect(parent, name, a, a, position, bounds);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Shadow shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static void Outline(Graphic graphic, Color color, float distance)
        {
            Outline outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        private static void SetPrivate<T>(PirateHudController hud, string name, T value) where T : Object
        {
            SerializedObject serialized = new SerializedObject(hud);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                Debug.LogError("PirateHudController has no serialized field named '" + name + "'.", hud);
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Color Hex(string html)
        {
            ColorUtility.TryParseHtmlString(html, out Color color);
            return color;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PirateGameUI")) AssetDatabase.CreateFolder("Assets", "PirateGameUI");
            if (!AssetDatabase.IsValidFolder("Assets/PirateGameUI/Generated")) AssetDatabase.CreateFolder("Assets/PirateGameUI", "Generated");
        }

        /// <summary>Loads the supplied skull PNG as a sprite, or returns null if it has not been added yet.</summary>
        private static Sprite LoadSkullSprite()
        {
            if (!File.Exists(SkullSpritePath)) return null;
            TextureImporter importer = AssetImporter.GetAtPath(SkullSpritePath) as TextureImporter;
            if (importer == null) return null;
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(SkullSpritePath);
        }

        private static Sprite EnsureWhiteSprite()
        {
            EnsureFolders();
            if (!File.Exists(SpritePath))
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                texture.Apply();
                File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(SpritePath);
            }
            TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        private static Sprite EnsureShapeSprite(string path, bool ring)
        {
            EnsureFolders();
            if (!File.Exists(path))
            {
                const int size = 128;
                const float outerRadius = 63f;
                const float innerRadius = 49f;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color[] pixels = new Color[size * size];
                Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = Vector2.Distance(new Vector2(x, y), center);
                        float alpha = Mathf.Clamp01(outerRadius - distance + 0.5f);
                        if (ring) alpha *= Mathf.Clamp01(distance - innerRadius + 0.5f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Rectangle with 45-degree cut corners (10px cut, 9-sliced) used for the bars and plates.</summary>
        private static Sprite EnsureCutSprite()
        {
            EnsureFolders();
            if (!File.Exists(CutSpritePath))
            {
                const int size = 64;
                const float cut = 10f;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // Distance from the nearest edge on each axis (pixel centres).
                        float dx = Mathf.Min(x + 0.5f, size - x - 0.5f);
                        float dy = Mathf.Min(y + 0.5f, size - y - 0.5f);
                        float alpha = 1f;
                        if (dx < cut && dy < cut)
                        {
                            // Signed distance to the diagonal cut line, anti-aliased.
                            float signedDistance = (dx + dy - cut) / 1.41421356f;
                            alpha = Mathf.Clamp01(signedDistance + 0.5f);
                        }
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(CutSpritePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(CutSpritePath);
            }
            TextureImporter importer = AssetImporter.GetAtPath(CutSpritePath) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(10f, 10f, 10f, 10f);
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(CutSpritePath);
        }
    }
}
#endif
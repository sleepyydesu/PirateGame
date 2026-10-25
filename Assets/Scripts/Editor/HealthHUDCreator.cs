#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace PirateGame.UI.Editor
{
    public static class HealthHUDCreator
    {
        private const string Menu = "Tools/Pirate Game/Create Health HUD";
        private const string SpritePath = "Assets/PirateGameUI/Generated/HealthBarWhite.png";

        [MenuItem(Menu)]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorUtility.DisplayDialog("Health HUD", "Exit Play Mode and open your gameplay scene first.", "OK");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            foreach (Canvas existing in Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include))
            {
                if (existing.name == "CombatHealthHUD" && existing.gameObject.scene == scene)
                {
                    Selection.activeGameObject = existing.gameObject;
                    EditorUtility.DisplayDialog("Health HUD", "CombatHealthHUD already exists in this scene. Select its PlayerHealth or BossHealth child to configure it.", "OK");
                    return;
                }
            }

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                EditorUtility.DisplayDialog("Import TextMeshPro resources first",
                    "Choose Window > TextMeshPro > Import TMP Essential Resources. Import them, then run Create Health HUD again.", "OK");
                return;
            }

            Sprite white = EnsureWhiteSprite();
            if (white == null)
            {
                Debug.LogError("Health HUD: could not create the generated white UI sprite at " + SpritePath);
                return;
            }

            GameObject root = new GameObject("CombatHealthHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            CreateBar(root.transform, "PlayerHealth", "PLAYER", 520f,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -36), white, font);
            HealthBarUI bossBar = CreateBar(root.transform, "BossHealth", "CAPTAIN", 1000f,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 56), white, font);
            CanvasGroup group = bossBar.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            BossHealthUI visibility = bossBar.gameObject.AddComponent<BossHealthUI>();
            visibility.bar = bossBar;
            visibility.group = group;

            Undo.RegisterCreatedObjectUndo(root, "Create Combat Health HUD");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("Health HUD created. PlayerHealth > Health Bar UI > Source: assign player root. BossHealth > Boss Health UI > Boss: assign boss root. Optionally assign Player Health there too. Save the scene.", root);
        }

        private static HealthBarUI CreateBar(Transform parent, string objectName, string label,
            float width, Vector2 anchor, Vector2 pivot, Vector2 position, Sprite white, TMP_FontAsset font)
        {
            RectTransform panel = Rect(parent, objectName, anchor, pivot, position, new Vector2(width, 86));
            HealthBarUI bar = panel.gameObject.AddComponent<HealthBarUI>();
            bar.displayName = label;
            bar.nameText = Label(panel, "Name", label, font, 28,
                new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(width - 160, 34), TextAlignmentOptions.Left);
            bar.valueText = Label(panel, "HealthNumbers", "100 / 100", font, 22,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(150, 30), TextAlignmentOptions.Right);

            RectTransform frame = Rect(panel, "Frame", Vector2.zero, Vector2.zero,
                new Vector2(0, 14), new Vector2(width, 28));
            Image frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.sprite = white;
            frameImage.color = Hex("#A4824C");
            frameImage.raycastTarget = false;

            Image track = StretchImage(frame, "Track", white, Hex("#211B19"), 2);
            bar.damageTrail = StretchImage(track.transform, "DamageTrail", white, Hex("#D3A45B"), 0);
            bar.healthFill = StretchImage(track.transform, "HealthFill", white, Hex("#B44335"), 0);
            foreach (Image image in new[] { bar.damageTrail, bar.healthFill })
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 1f;
            }
            return bar;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image StretchImage(Transform parent, string name, Sprite white, Color color, float inset)
        {
            RectTransform rect = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = white;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string value, TMP_FontAsset font,
            float size, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 bounds, TextAlignmentOptions alignment)
        {
            RectTransform rect = Rect(parent, name, anchor, pivot, position, bounds);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = Hex("#F5E6CB");
            text.alignment = alignment;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out Color color);
            return color;
        }

        private static Sprite EnsureWhiteSprite()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PirateGameUI"))
                AssetDatabase.CreateFolder("Assets", "PirateGameUI");
            if (!AssetDatabase.IsValidFolder("Assets/PirateGameUI/Generated"))
                AssetDatabase.CreateFolder("Assets/PirateGameUI", "Generated");
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
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }
    }
}
#endif

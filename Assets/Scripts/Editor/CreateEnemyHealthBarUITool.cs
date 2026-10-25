using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using PirateGame.Combat;

namespace PirateGame.UI.EditorTools
{
    public static class CreateEnemyHealthBarUITool
    {
        private const string MenuPath =
            "Tools/Pirate Game/Create Enemy Health Bar UI";

        private const string CanvasName = "EnemyHealthBarCanvas";

        [MenuItem(MenuPath)]
        private static void CreateEnemyHealthBar()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected == null)
            {
                EditorUtility.DisplayDialog(
                    "Enemy Health Bar",
                    "Select an enemy GameObject in the Hierarchy first.",
                    "OK"
                );
                return;
            }

            // Find the Health component on the selected enemy,
            // a parent, or one of its children.
            Health health = selected.GetComponentInParent<Health>();

            if (health == null)
                health = selected.GetComponentInChildren<Health>(true);

            if (health == null)
            {
                EditorUtility.DisplayDialog(
                    "Enemy Health Bar",
                    "No Health component was found on the selected object, " +
                    "its parents, or its children.\n\n" +
                    "Select an enemy that uses PirateGame.Combat.Health.",
                    "OK"
                );
                return;
            }

            if (EditorUtility.IsPersistent(health.gameObject))
            {
                EditorUtility.DisplayDialog(
                    "Enemy Health Bar",
                    "Select an enemy instance in the Hierarchy, " +
                    "or open the enemy prefab in Prefab Mode, " +
                    "then try again.",
                    "OK"
                );
                return;
            }

            // Prevent accidental duplicate health bars.
            EnemyHealthBar existing =
                health.GetComponentInChildren<EnemyHealthBar>(true);

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);

                EditorUtility.DisplayDialog(
                    "Enemy Health Bar",
                    "This enemy already has an EnemyHealthBar component. " +
                    "The existing health bar has been selected.",
                    "OK"
                );
                return;
            }

            CreateUI(health);
        }

        private static void CreateUI(Health health)
        {
            Transform enemyTransform = health.transform;
            Camera gameCamera = FindGameCamera();

            // -------------------------------------------------
            // 1. CREATE THE WORLD SPACE CANVAS
            // -------------------------------------------------

            GameObject canvasObject = new GameObject(
                CanvasName,
                typeof(RectTransform),
                typeof(Canvas)
            );

            Undo.RegisterCreatedObjectUndo(
                canvasObject,
                "Create Enemy Health Bar"
            );

            Undo.SetTransformParent(
                canvasObject.transform,
                enemyTransform,
                "Parent Enemy Health Bar"
            );

            RectTransform canvasRect =
                canvasObject.GetComponent<RectTransform>();

            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.01f;
            canvasRect.sizeDelta = new Vector2(180f, 18f);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = gameCamera;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;

            // -------------------------------------------------
            // 2. CREATE THE DARK BACKGROUND
            // -------------------------------------------------

            Image background = CreateImage(
                "Background",
                canvasObject.transform,
                new Color(0.12f, 0.09f, 0.09f, 0.96f)
            );

            RectTransform backgroundRect =
                background.rectTransform;

            StretchToParent(
                backgroundRect,
                Vector2.zero,
                Vector2.zero
            );

            // -------------------------------------------------
            // DAMAGE TRAIL - RENDERS BEHIND THE RED HEALTH FILL
            // -------------------------------------------------

            Image damageTrail = CreateImage(
                "DamageTrail",
                canvasObject.transform,
                new Color(1f, 0.62f, 0.12f, 1f) // Amber / gold
            );

            StretchToParent(
                damageTrail.rectTransform,
                new Vector2(2f, 2f),
                new Vector2(-2f, -2f)
            );

            damageTrail.type = Image.Type.Filled;
            damageTrail.fillMethod = Image.FillMethod.Horizontal;
            damageTrail.fillOrigin = (int)Image.OriginHorizontal.Left;
            damageTrail.fillAmount = 1f;
            damageTrail.raycastTarget = false;

            // -------------------------------------------------
            // 3. CREATE THE RED HEALTH FILL
            // -------------------------------------------------

            Image fill = CreateImage(
                "Fill",
                canvasObject.transform,
                new Color(0.80f, 0.12f, 0.12f, 1f)
            );

            RectTransform fillRect = fill.rectTransform;

            // Slight inset leaves a dark border around the fill.
            StretchToParent(
                fillRect,
                new Vector2(2f, 2f),
                new Vector2(-2f, -2f)
            );

            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;

            // Ensure the fill renders above the background.
            fill.transform.SetAsLastSibling();

            // -------------------------------------------------
            // 4. ADD THE RUNTIME HEALTH BAR CONTROLLER
            // -------------------------------------------------

            EnemyHealthBar healthBar =
                Undo.AddComponent<EnemyHealthBar>(canvasObject);

            SerializedObject serializedBar =
                new SerializedObject(healthBar);

            serializedBar.Update();

            SetObjectReference(
                serializedBar, "followTarget", enemyTransform
            );

            SetFloat(
                serializedBar, "heightOffset", 2.2f
            );

            SetObjectReference(
                serializedBar, "targetCamera", gameCamera
            );

            SetObjectReference(
                serializedBar, "worldCanvas", canvas
            );

            SetObjectReference(
                serializedBar, "healthFill", fill
            );

            SetObjectReference(
                serializedBar, "damageTrailFill", damageTrail
            );

            SetFloat(
                serializedBar, "damageTrailDelay", 0.2f
            );

            SetFloat(
                serializedBar, "damageTrailSpeed", 1.2f
            );

            SetObjectReference(
                serializedBar, "healthValueText", null
            );

            SetBool(
                serializedBar, "hideWhenDead", true
            );

            serializedBar.ApplyModifiedProperties();

            // Record prefab overrides when working on a prefab instance.
            if (PrefabUtility.IsPartOfPrefabInstance(health.gameObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    enemyTransform
                );

                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    canvas
                );

                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    healthBar
                );
            }

            EditorSceneManager.MarkSceneDirty(
                health.gameObject.scene
            );

            Selection.activeGameObject = canvasObject;
            EditorGUIUtility.PingObject(canvasObject);
            SceneView.RepaintAll();

            Debug.Log(
                $"Enemy health bar created for '{health.gameObject.name}'.",
                canvasObject
            );
        }

        // -----------------------------------------------------
        // HELPER METHODS
        // -----------------------------------------------------

        private static Image CreateImage(
            string objectName,
            Transform parent,
            Color color)
        {
            GameObject imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );

            Undo.RegisterCreatedObjectUndo(
                imageObject,
                $"Create {objectName}"
            );

            Undo.SetTransformParent(
                imageObject.transform,
                parent,
                $"Parent {objectName}"
            );

            RectTransform rect =
                imageObject.GetComponent<RectTransform>();

            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            Image image = imageObject.GetComponent<Image>();

            // Assign Unity's built-in UI sprite so Image.Type.Filled works.
            Sprite uiSprite =
                AssetDatabase.GetBuiltinExtraResource<Sprite>(
                    "UI/Skin/UISprite.psd"
                );

            if (uiSprite != null)
            {
                image.sprite = uiSprite;
            }

            image.color = color;
            image.raycastTarget = false;

            return image; ;
        }

        private static void StretchToParent(
            RectTransform rect,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static Camera FindGameCamera()
        {
            Camera mainCamera = Camera.main;

            if (mainCamera != null)
                return mainCamera;

            Camera[] cameras =
                Object.FindObjectsByType<Camera>(
                    FindObjectsSortMode.None
                );

            foreach (Camera camera in cameras)
            {
                if (camera != null && camera.enabled)
                    return camera;
            }

            return null;
        }

        private static void SetObjectReference(
            SerializedObject serializedObject,
            string propertyName,
            Object value)
        {
            SerializedProperty property =
                serializedObject.FindProperty(propertyName);

            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetFloat(
            SerializedObject serializedObject,
            string propertyName,
            float value)
        {
            SerializedProperty property =
                serializedObject.FindProperty(propertyName);

            if (property != null)
                property.floatValue = value;
        }

        private static void SetBool(
            SerializedObject serializedObject,
            string propertyName,
            bool value)
        {
            SerializedProperty property =
                serializedObject.FindProperty(propertyName);

            if (property != null)
                property.boolValue = value;
        }
    }
}
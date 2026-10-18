using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PirateGame.Combat;

namespace PirateGame.PlayerV2
{
    // A working, plain controls screen. It needs no Canvas or EventSystem.
    // Later, its rebinding logic can be connected to a styled pause menu.
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(PlayerInput), typeof(PlayerInputReader))]
    public sealed class PlayerControlsMenu : MonoBehaviour
    {
        private PlayerInputReader reader;
        [Header("Menu size")]
        [SerializeField, Range(1f, 2.5f)]
        private float menuScale = 1.5f;
        private PlayerCombat combat;
        private Health health;
        private InputActionAsset asset;
        private InputActionRebindingExtensions.RebindingOperation operation;
        private InputActionMap cameraMap;
        private bool opened, gameplayWasEnabled, cameraWasEnabled;
        private bool cursorWasVisible;
        private CursorLockMode previousCursorLock;
        private float previousTimeScale;
        private Vector2 scroll;
        private string status = "Click Change, then press a key or mouse button.";

        private static string SaveKey(InputActionAsset actions) =>
            "Pirate.Controls.Chords.v1." + actions.name;

        public static void LoadSavedBindings(InputActionAsset actions)
        {
            string key = SaveKey(actions);
            if (!PlayerPrefs.HasKey(key)) return;
            try { actions.LoadBindingOverridesFromJson(PlayerPrefs.GetString(key)); }
            catch (Exception error)
            {
                actions.RemoveAllBindingOverrides();
                Debug.LogWarning("Saved controls could not be loaded: " + error.Message);
            }
        }

        private void Awake()
        {
            reader = GetComponent<PlayerInputReader>();
            combat = GetComponent<PlayerCombat>();
            health = GetComponent<Health>();
        }

        private void Update()
        {
            // Wait until PlayerInput has established this player's action instance.
            if (asset == null)
            {
                asset = GetComponent<PlayerInput>().actions;
                if (asset != null)
                    cameraMap = asset.FindActionMap("CameraControls", true);
            }

            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                if (opened) Close();
                else Open();
            }
        }

        public void Open()
        {
            if (opened || asset == null || reader == null ||
                combat == null || !combat.Ready ||
                (health != null && health.IsDead)) return;

            gameplayWasEnabled = reader.GameplayEnabled;
            cameraWasEnabled = cameraMap.enabled;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            cursorWasVisible = Cursor.visible;
            opened = true;
            reader.SetGameplayEnabled(false);
            cameraMap.Disable();
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!opened) return;
            operation?.Cancel();
            opened = false;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = cursorWasVisible;

            bool canResume = reader.isActiveAndEnabled && combat.Ready &&
                combat.isActiveAndEnabled && (health == null || !health.IsDead);
            if (canResume)
            {
                reader.SetGameplayEnabled(gameplayWasEnabled);
                if (cameraWasEnabled) cameraMap.Enable();
            }
        }

        private void OnDisable() { Close(); }

        private void OnGUI()
        {
            if (!opened || asset == null) return;
            Matrix4x4 previousMatrix = GUI.matrix;

            // Enlarge text, buttons, and spacing together.
            float scale = Mathf.Min(
                menuScale,
                Mathf.Min(Screen.width / 560f, Screen.height / 400f));

            GUI.matrix = previousMatrix * Matrix4x4.Scale(
                new Vector3(scale, scale, 1f));

            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;

            float width = Mathf.Min(720f, screenWidth - 20f);
            float height = Mathf.Min(720f, screenHeight - 20f);

            GUILayout.BeginArea(
                new Rect(
                    (screenWidth - width) / 2f,
                    (screenHeight - height) / 2f,
                    width,
                    height),
                GUI.skin.window);
            GUILayout.Label("CONTROLS — F1 opens/closes this screen");
            GUILayout.Label("For combinations: hold the modifier FIRST, then press the main button.");
            GUILayout.Label(status);

            scroll = GUILayout.BeginScrollView(scroll);
            GUI.enabled = operation == null;
            foreach (InputAction action in asset.FindActionMap("Player", true).actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite) continue;
                    GUILayout.BeginHorizontal();
                    string part = binding.isPartOfComposite ? " / " + binding.name : "";
                    GUILayout.Label(action.name + part, GUILayout.Width(width * 0.38f));
                    GUILayout.Label(action.GetBindingDisplayString(i), GUILayout.Width(width * 0.28f));
                    if (GUILayout.Button("Change")) StartRebind(action, i);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Reset all controls"))
            {
                asset.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(SaveKey(asset));
                PlayerPrefs.Save();
                status = "Default controls restored.";
            }
            GUI.enabled = true;
            if (operation != null && GUILayout.Button("Cancel change")) operation.Cancel();
            if (GUILayout.Button("Close")) Close();
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void StartRebind(InputAction action, int index)
        {
            if (operation != null) return;
            // Disable the exact action/map that we are about to rebind.
            reader.SetGameplayEnabled(false);
            action.actionMap?.Disable();
            action.Disable();
            string oldOverrides = asset.SaveBindingOverridesAsJson();
            InputBinding binding = action.bindings[index];
            bool modifier = binding.isPartOfComposite &&
                binding.name.StartsWith("modifier", StringComparison.OrdinalIgnoreCase);

            status = "Press a new " + (modifier ? "modifier key" : "key or mouse button") +
                " for " + action.name + ". Escape cancels.";

            operation = action.PerformInteractiveRebinding(index)
                .WithExpectedControlType("Button")
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/f1")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithTimeout(15f)
                .OnMatchWaitForAnother(0.1f)
                .OnCancel(op =>
                {
                    operation = null;
                    op.Dispose();
                    status = "Change cancelled.";
                })
                .OnComplete(op =>
                {
                    operation = null;
                    op.Dispose();
                    string conflict = FindConflict();
                    if (conflict != null)
                    {
                        asset.LoadBindingOverridesFromJson(oldOverrides);
                        status = conflict + " Previous binding restored.";
                        return;
                    }
                    PlayerPrefs.SetString(SaveKey(asset), asset.SaveBindingOverridesAsJson());
                    PlayerPrefs.Save();
                    reader.Clear();
                    status = "Controls saved.";
                });

            if (!modifier) operation.WithControlsHavingToMatchPath("<Mouse>");
            operation.Start();
        }

        private string FindConflict()
        {
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputAction action in asset.FindActionMap("Player", true).actions)
            {
                var movementParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding b = action.bindings[i];
                    if (b.isPartOfComposite)
                    {
                        if (action.name == "Move" && !string.IsNullOrEmpty(b.effectivePath) &&
                            !movementParts.Add(b.effectivePath))
                            return "Two movement directions cannot use the same key.";
                        continue;
                    }
                    string primary = b.effectivePath;
                    string modifier = "";
                    if (b.isComposite)
                    {
                        if (action.name == "Move") continue;
                        primary = "";
                        for (int j = i + 1; j < action.bindings.Count &&
                            action.bindings[j].isPartOfComposite; j++)
                        {
                            InputBinding part = action.bindings[j];
                            if (part.name.Equals("modifier", StringComparison.OrdinalIgnoreCase))
                                modifier = part.effectivePath;
                            else primary = part.effectivePath;
                        }
                        if (string.IsNullOrEmpty(modifier) || string.IsNullOrEmpty(primary) ||
                            modifier.Equals(primary, StringComparison.OrdinalIgnoreCase))
                            return "A combination needs two different buttons.";
                        modifiers.Add(modifier);
                    }
                    if (string.IsNullOrEmpty(primary)) continue;
                    string signature = modifier + "|" + primary;
                    if (used.TryGetValue(signature, out string other) && other != action.name)
                        return "That complete binding is already used by " + other + ".";
                    used[signature] = action.name;
                }
            }
            // A press action on a modifier would fire before the combo is complete.
            // Held actions (Sprint/Defense) and movement may intentionally share it.
            foreach (InputAction action in asset.FindActionMap("Player", true).actions)
            {
                if (action.name == "Move" || action.name == "Sprint" ||
                    action.name == "Defense") continue;
                foreach (InputBinding b in action.bindings)
                    if (!b.isComposite && !b.isPartOfComposite &&
                        !string.IsNullOrEmpty(b.effectivePath) && modifiers.Contains(b.effectivePath))
                        return "That modifier is also used by " + action.name +
                            ". Move that action to another key first.";
            }
            return null;
        }
    }
}

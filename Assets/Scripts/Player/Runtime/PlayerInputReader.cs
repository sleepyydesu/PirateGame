using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace PirateGame.PlayerV2
{
    public enum PlayerCommand { Jump, Roll, Crouch, Equip, Light, Special, Thrust, Special2, Heal, Count }
    [DefaultExecutionOrder(100), RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField, Min(0.02f)] private float bufferSeconds = 0.18f;
        private InputActionMap gameplay, cameraMap;
        private InputAction move, sprint, block;
        private InputAction[] buttons;
        private Action<InputAction.CallbackContext>[] callbacks;
        private readonly float[] expires = new float[(int)PlayerCommand.Count];
        private readonly bool[] pending = new bool[(int)PlayerCommand.Count];
        private bool bound, acceptsGameplay = true;
        public Vector2 Move => Ready ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        public bool GameplayEnabled => acceptsGameplay;

        public bool Sprint =>
            Ready &&
            sprint.IsPressed() &&
            !ActionClaimedByChord(sprint);

        public bool Block =>
            Ready &&
            block.IsPressed() &&
            !ActionClaimedByChord(block);
        private bool Ready => bound && acceptsGameplay && isActiveAndEnabled && gameplay.enabled;
        private void OnEnable()
        {
            var asset = GetComponent<PlayerInput>().actions; // Read THIS player's instance, not a second wrapper.
            PlayerControlsMenu.LoadSavedBindings(asset);
            gameplay = asset.FindActionMap("Player", true);
            cameraMap = asset.FindActionMap("CameraControls", true);
            move = gameplay.FindAction("Move", true);
            sprint = gameplay.FindAction("Sprint", true);
            block = gameplay.FindAction("Defense", true);
            string[] names = { "Jump", "Roll", "Crouch", "DrawWeapon", "Attack", "SpecialAttack", "ThrustAttack", "SpecialAttack2", "UsePotion" };
            buttons = new InputAction[names.Length]; callbacks = new Action<InputAction.CallbackContext>[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                buttons[i] = gameplay.FindAction(names[i], true);
                callbacks[i] = _ => { if (Ready) { pending[index] = true; expires[index] = Time.time + bufferSeconds; } };
                buttons[i].performed += callbacks[i];
            }
            bound = true;
            SetGameplayEnabled(acceptsGameplay);
            cameraMap.Enable(); // Independent of weapon mode. Pause policy may disable it explicitly.
        }
        private void OnDisable()
        {
            if (!bound) return;
            for (int i = 0; i < buttons.Length; i++) buttons[i].performed -= callbacks[i];
            gameplay.Disable(); cameraMap.Disable(); Clear(); bound = false;
        }
        public void SetGameplayEnabled(bool enabled)
        {
            acceptsGameplay = enabled; Clear();
            if (!bound) return;
            if (enabled) gameplay.Enable(); else gameplay.Disable();
        }
        public bool Has(PlayerCommand command)
        {
            int i = (int)command;
            if (!Ready || !pending[i]) return false;
            if (Time.time <= expires[i]) return true;
            pending[i] = false; return false;
        }
        public bool Consume(PlayerCommand command)
        {
            if (!Has(command)) return false;
            pending[(int)command] = false; return true;
        }
        public void ClearAttacks()
        {
            pending[(int)PlayerCommand.Light] = pending[(int)PlayerCommand.Special] =
                pending[(int)PlayerCommand.Thrust] = pending[(int)PlayerCommand.Special2] = false;
        }
        public void Clear() { Array.Clear(pending, 0, pending.Length); }
        private bool HasComposite(InputAction action)
        {
            foreach (InputBinding binding in action.bindings)
            {
                if (binding.isComposite)
                    return true;
            }

            return false;
        }

        private bool ActionClaimedByChord(InputAction plainAction)
        {
            if (buttons == null)
                return false;

            foreach (InputAction chord in buttons)
            {
                if (!HasComposite(chord) ||
                    (!chord.IsPressed() &&
                     !chord.WasPerformedThisFrame()))
                {
                    continue;
                }

                foreach (InputBinding part in chord.bindings)
                {
                    bool mainButton =
                        string.Equals(
                            part.name,
                            "binding",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            part.name,
                            "button",
                            StringComparison.OrdinalIgnoreCase);

                    if (!part.isPartOfComposite ||
                        !mainButton ||
                        string.IsNullOrEmpty(part.effectivePath))
                    {
                        continue;
                    }

                    foreach (InputControl control in plainAction.controls)
                    {
                        if (InputControlPath.Matches(
                            part.effectivePath,
                            control))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void Update()
        {
            if (!Ready)
                return;

            // Input callbacks have finished before we resolve overlaps.
            // PlayerControllerV2 reads the result afterward.
            for (int i = 0; i < buttons.Length; i++)
            {
                if (pending[i] &&
                    !HasComposite(buttons[i]) &&
                    ActionClaimedByChord(buttons[i]))
                {
                    pending[i] = false;
                }
            }
        }
        private void OnApplicationFocus(bool focus) { if (!focus) Clear(); }
    }
}

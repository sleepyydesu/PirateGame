using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PirateGame.Quests
{
    /// <summary>
    /// Freezes player movement/combat and camera look while a menu or dialogue is open,
    /// and frees the cursor. Reference counted, so nested UIs (dialogue -> shop) are safe.
    ///
    /// Works by disabling the player's enabled action maps (the state machine then reads
    /// zero input and idles naturally) rather than disabling PlayerController.
    /// </summary>
    public static class PlayerControlLock
    {
        private static int locks;
        private static readonly List<InputActionMap> disabledMaps = new List<InputActionMap>();
        private static readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();
        private static CursorLockMode previousLockMode;
        private static bool previousCursorVisible;

        public static bool IsLocked => locks > 0;
        public static event Action<bool> LockChanged;

        public static void Lock()
        {
            if (locks++ > 0) return;

            previousLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            GameObject player = QuestPlayer.GameObject;
            PlayerInput input = player != null ? player.GetComponent<PlayerInput>() : null;
            if (input != null && input.actions != null)
            {
                foreach (InputActionMap map in input.actions.actionMaps)
                {
                    if (!map.enabled) continue;
                    disabledMaps.Add(map);
                    map.Disable();
                }
            }

            foreach (var axis in UnityEngine.Object.FindObjectsByType<CinemachineInputAxisController>())
            {
                if (!axis.enabled) continue;
                axis.enabled = false;
                disabledBehaviours.Add(axis);
            }

            LockChanged?.Invoke(true);
        }

        public static void Unlock()
        {
            if (locks == 0) return;
            if (--locks > 0) return;

            foreach (InputActionMap map in disabledMaps) map?.Enable();
            disabledMaps.Clear();
            foreach (Behaviour b in disabledBehaviours) if (b != null) b.enabled = true;
            disabledBehaviours.Clear();

            Cursor.lockState = previousLockMode;
            Cursor.visible = previousCursorVisible;
            LockChanged?.Invoke(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            locks = 0;
            disabledMaps.Clear();
            disabledBehaviours.Clear();
            LockChanged = null;
        }
    }
}

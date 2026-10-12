using System.Collections.Generic;
using PirateGame.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PirateGame.Quests
{
    /// <summary>
    /// On the player. Finds the closest usable <see cref="Interactable"/> for each key
    /// (E to talk, Q to pick up…), shows the prompts, and fires on key press.
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        private readonly Dictionary<Key, Interactable> best = new Dictionary<Key, Interactable>();
        private readonly List<InteractionPrompt> prompts = new List<InteractionPrompt>();
        private Health health;

        private void Awake() => health = GetComponent<Health>();

        private void Update()
        {
            best.Clear();
            prompts.Clear();

            bool blocked = PlayerControlLock.IsLocked || (health != null && health.IsDead);
            if (!blocked)
            {
                Vector3 p = transform.position;
                var bestDist = new Dictionary<Key, float>();
                foreach (Interactable it in Interactable.All)
                {
                    if (it == null || !it.isActiveAndEnabled) continue;
                    float d = it.HorizontalDistance(p);
                    if (d > it.Range || Mathf.Abs(it.transform.position.y - p.y) > 3f) continue;
                    if (!it.CanInteract(gameObject)) continue;
                    if (bestDist.TryGetValue(it.Key, out float bd) && bd <= d) continue;
                    bestDist[it.Key] = d;
                    best[it.Key] = it;
                }
                foreach (var kv in best) prompts.Add(new InteractionPrompt(kv.Key, kv.Value.PromptText));
            }

            QuestUI.Instance?.ShowPrompts(prompts);
            if (blocked || Time.frameCount - QuestUI.LastMenuCloseFrame < 2) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            foreach (var kv in best)
            {
                if (!kb[kv.Key].wasPressedThisFrame) continue;
                kv.Value.Interact(gameObject);
                break;
            }
        }
    }

    public readonly struct InteractionPrompt
    {
        public readonly Key Key;
        public readonly string Text;
        public InteractionPrompt(Key key, string text) { Key = key; Text = text; }
    }
}

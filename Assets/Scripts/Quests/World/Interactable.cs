using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PirateGame.Quests
{
    /// <summary>
    /// Anything the player can use with a key press when close enough. The player's
    /// <see cref="InteractionController"/> picks the nearest one per key and shows a prompt.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();

        [SerializeField] protected string promptText = "Interact";
        [SerializeField] protected Key key = Key.E;
        [SerializeField, Min(0.1f)] protected float range = 3f;

        public Key Key => key;
        public float Range => range;
        public virtual string PromptText => promptText;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        public virtual bool CanInteract(GameObject player) => true;
        public abstract void Interact(GameObject player);

        public float HorizontalDistance(Vector3 from)
        {
            Vector3 d = transform.position - from; d.y = 0f;
            return d.magnitude;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, range);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();
    }
}

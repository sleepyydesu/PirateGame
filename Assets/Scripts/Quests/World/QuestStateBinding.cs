using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PirateGame.Quests
{
    /// <summary>
    /// Shows/hides objects (or fires events) depending on quest state. This is how the scene
    /// reacts to quests without custom code: e.g. "merchant at shop only after storm_done and
    /// not merchant_kidnapped", "beach pirates only during Active_Search".
    /// Keep this component on an object that stays active — it toggles the *targets*.
    /// </summary>
    public class QuestStateBinding : MonoBehaviour
    {
        public enum Match { All, Any }

        [SerializeField] private Match match = Match.All;
        [SerializeField] private List<QuestStateCondition> conditions = new List<QuestStateCondition>();

        [Header("Targets")]
        [SerializeField] private List<GameObject> activeWhenTrue = new List<GameObject>();
        [SerializeField] private List<GameObject> activeWhenFalse = new List<GameObject>();
        [Tooltip("Delay before hiding objects after the condition changes (lets an NPC finish a line).")]
        [SerializeField, Min(0f)] private float hideDelay;

        public UnityEvent onBecameTrue;
        public UnityEvent onBecameFalse;

        private bool? last;
        private Coroutine pending;

        public bool Value => last ?? false;

        private void Start()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (QuestManager.Instance != null) QuestManager.Instance.Changed -= Refresh;
        }

        public void Refresh()
        {
            bool value = Evaluate();
            if (last == value) return;
            bool first = last == null;
            last = value;

            if (pending != null) StopCoroutine(pending);
            if (first || hideDelay <= 0f) Apply(value);
            else pending = StartCoroutine(ApplyLater(value));

            if (!first)
            {
                if (value) onBecameTrue?.Invoke();
                else onBecameFalse?.Invoke();
            }
        }

        private IEnumerator ApplyLater(bool value)
        {
            // Show immediately, hide after the delay.
            foreach (GameObject go in value ? activeWhenTrue : activeWhenFalse) if (go) go.SetActive(true);
            yield return new WaitForSeconds(hideDelay);
            Apply(value);
            pending = null;
        }

        private void Apply(bool value)
        {
            foreach (GameObject go in activeWhenTrue) if (go) go.SetActive(value);
            foreach (GameObject go in activeWhenFalse) if (go) go.SetActive(!value);
        }

        private bool Evaluate()
        {
            QuestManager m = QuestManager.Instance;
            if (m == null) return false;
            if (conditions.Count == 0) return true;
            if (match == Match.All)
            {
                foreach (QuestStateCondition c in conditions) if (!c.Evaluate(m)) return false;
                return true;
            }
            foreach (QuestStateCondition c in conditions) if (c.Evaluate(m)) return true;
            return false;
        }

        // Builder / code API
        public void Setup(Match m, IEnumerable<QuestStateCondition> conds, IEnumerable<GameObject> whenTrue, IEnumerable<GameObject> whenFalse = null, float delay = 0f)
        {
            match = m;
            conditions = new List<QuestStateCondition>(conds);
            activeWhenTrue = new List<GameObject>(whenTrue ?? new GameObject[0]);
            activeWhenFalse = new List<GameObject>(whenFalse ?? new GameObject[0]);
            hideDelay = delay;
        }
    }
}

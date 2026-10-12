using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Sets a flag (and optionally reports an event) once a condition holds — optionally only
    /// when the player is far enough away that they won't see the change happen.
    /// Used for "the merchant gets kidnapped while you're away from the shop".
    /// </summary>
    public class QuestFlagSetter : MonoBehaviour
    {
        [SerializeField] private QuestStateCondition condition = new QuestStateCondition();
        [SerializeField] private string flagToSet;
        [SerializeField] private string eventToReport;
        [Tooltip("If > 0, only fires while the player is at least this far from this object.")]
        [SerializeField, Min(0f)] private float minPlayerDistance;

        private float timer;

        public void Setup(QuestStateCondition cond, string flag, string evt, float distance)
        {
            condition = cond; flagToSet = flag; eventToReport = evt; minPlayerDistance = distance;
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.4f;

            QuestManager m = QuestManager.Instance;
            if (m == null || m.HasFlag(flagToSet) || !condition.Evaluate(m)) return;

            if (minPlayerDistance > 0f)
            {
                if (!QuestPlayer.TryGetPosition(out Vector3 p)) return;
                if (Vector3.Distance(p, transform.position) < minPlayerDistance) return;
            }

            m.SetFlag(flagToSet);
            if (!string.IsNullOrEmpty(eventToReport)) m.Report(eventToReport);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// A named place the quest marker and guide trail can point at. Stages reference it by
    /// <see cref="targetId"/>. Give it an <see cref="areaRadius"/> for "search this area"
    /// objectives — the trail then stops at the edge and a ring is drawn on the ground.
    /// Only active objects are found, so toggling an NPC off also removes its marker.
    /// </summary>
    public class QuestTarget : MonoBehaviour
    {
        public string targetId = "target";
        [Min(0f)] public float areaRadius;
        public float markerHeight = 2.3f;

        private static readonly Dictionary<string, List<QuestTarget>> registry = new Dictionary<string, List<QuestTarget>>();

        public Vector3 MarkerPosition => transform.position + Vector3.up * markerHeight;
        public bool IsArea => areaRadius > 0.01f;

        private void OnEnable()
        {
            if (!registry.TryGetValue(targetId, out var list)) registry[targetId] = list = new List<QuestTarget>();
            list.Add(this);
        }

        private void OnDisable()
        {
            if (registry.TryGetValue(targetId, out var list)) list.Remove(this);
        }

        public static QuestTarget Find(string id)
        {
            if (string.IsNullOrEmpty(id) || !registry.TryGetValue(id, out var list)) return null;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) return list[i];
            return null;
        }

        public bool Contains(Vector3 worldPos)
        {
            if (!IsArea) return false;
            Vector3 d = worldPos - transform.position; d.y = 0f;
            return d.magnitude <= areaRadius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawLine(transform.position, MarkerPosition);
            Gizmos.DrawWireSphere(MarkerPosition, 0.3f);
            if (IsArea)
            {
                const int seg = 48;
                Vector3 prev = transform.position + new Vector3(areaRadius, 0, 0);
                for (int i = 1; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * areaRadius;
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => registry.Clear();
    }

    /// <summary>Resolves "where should the player go right now" for the marker and trail.</summary>
    public static class QuestGuidance
    {
        public static bool TryGetCurrent(out QuestDefinition quest, out QuestStage stage, out QuestTarget target)
        {
            quest = null; stage = null; target = null;
            QuestManager m = QuestManager.Instance;
            if (m == null || m.TrackedQuest == null) return false;
            quest = m.TrackedQuest;
            stage = m.GetCurrentStage(quest);
            if (stage == null) return false;
            target = QuestTarget.Find(stage.targetId);
            return target != null;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using PirateGame.Combat;
using PirateGame.Enemies;
using UnityEngine;

namespace PirateGame.Quests
{
    /// <summary>
    /// Spawns a group of enemies when the player enters the area while the encounter is armed.
    /// Reports:
    ///   encounter_started:&lt;id&gt;   when the fight begins
    ///   enemy_defeated:&lt;id&gt;     per kill (use with a stage's Required Count)
    ///   encounter_cleared:&lt;id&gt;   when all are dead
    ///   encounter_reset:&lt;id&gt;     if the player leaves mid-fight (enemies despawn and respawn on return)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CombatEncounter : MonoBehaviour
    {
        public enum State { Idle, Fighting, Cleared }

        [SerializeField] private string encounterId = "encounter";
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        [SerializeField] private QuestStateCondition armedWhen = new QuestStateCondition();
        [Tooltip("How far outside the area the player must go before the fight resets.")]
        [SerializeField, Min(0f)] private float leaveMargin = 5f;
        [SerializeField] private float spawnInterval = 0.2f;
        [SerializeField] private GameObject spawnEffectPrefab;

        private Collider area;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private int alive;

        public State Current { get; private set; } = State.Idle;
        public int Alive => alive;
        public int Total => spawnPoints.Count;
        public string Id => encounterId;

        private void Awake()
        {
            area = GetComponent<Collider>();
            area.isTrigger = true;
        }

        public void Setup(string id, GameObject prefab, List<Transform> points, QuestStateCondition armed)
        {
            encounterId = id; enemyPrefab = prefab; spawnPoints = points; armedWhen = armed;
        }

        private void Update()
        {
            if (Current == State.Cleared) return;
            if (!QuestPlayer.TryGetPosition(out Vector3 p)) return;

            Vector3 probe = p + Vector3.up * 0.5f;
            float outside = (area.ClosestPoint(probe) - probe).magnitude;

            if (Current == State.Idle)
            {
                if (outside <= 0.001f && armedWhen.Evaluate(QuestManager.Instance))
                    StartCoroutine(Begin());
            }
            else if (Current == State.Fighting && outside > leaveMargin)
            {
                ResetEncounter();
            }
        }

        private IEnumerator Begin()
        {
            Current = State.Fighting;
            alive = 0;
            QuestManager.Instance?.Report("encounter_started:" + encounterId);

            foreach (Transform point in spawnPoints)
            {
                if (Current != State.Fighting) yield break;
                SpawnAt(point);
                if (spawnInterval > 0f) yield return new WaitForSeconds(spawnInterval);
            }

            // EnemyAI.Start() sets its initial state, so alert after it has run.
            yield return null;
            yield return null;
            if (QuestPlayer.TryGetPosition(out Vector3 p))
                foreach (GameObject go in spawned)
                    if (go != null && go.TryGetComponent(out EnemyAI ai)) ai.OnGroupAlert(p);
        }

        private void SpawnAt(Transform point)
        {
            if (enemyPrefab == null || point == null) return;
            Quaternion rot = point.rotation;
            if (QuestPlayer.TryGetPosition(out Vector3 p))
            {
                Vector3 d = p - point.position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(d);
            }

            GameObject go = Instantiate(enemyPrefab, point.position, rot);
            go.name = $"{encounterId}_{spawned.Count + 1}";
            spawned.Add(go);
            if (spawnEffectPrefab != null) Destroy(Instantiate(spawnEffectPrefab, point.position, Quaternion.identity), 4f);

            if (go.TryGetComponent(out Health h))
            {
                alive++;
                h.OnDeath += () => OnEnemyDied(go);
            }
        }

        private void OnEnemyDied(GameObject enemy)
        {
            if (Current != State.Fighting) return;
            alive = Mathf.Max(0, alive - 1);
            QuestManager.Instance?.Report("enemy_defeated:" + encounterId);
            if (alive == 0)
            {
                Current = State.Cleared;
                QuestManager.Instance?.Report("encounter_cleared:" + encounterId);
            }
        }

        /// <summary>Player left mid-fight: remove everyone so the full group fights again on return.</summary>
        public void ResetEncounter()
        {
            StopAllCoroutines();
            foreach (GameObject go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
            alive = 0;
            Current = State.Idle;
            QuestManager.Instance?.Report("encounter_reset:" + encounterId);
            QuestUI.Toast("You fled the fight", "The kidnappers regroup. Return to try again.", ToastKind.Warning);
        }

        /// <summary>QA helper: kill every living enemy in this encounter.</summary>
        public void DebugKillAll()
        {
            foreach (GameObject go in spawned.ToArray())
                if (go != null && go.TryGetComponent(out Health h) && !h.IsDead)
                    h.TakeDamage(new DamageInfo(99999f, gameObject, go.transform.position));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.8f);
            foreach (Transform t in spawnPoints) if (t) Gizmos.DrawWireSphere(t.position + Vector3.up, 0.5f);
        }
    }
}

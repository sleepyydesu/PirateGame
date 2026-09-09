using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Enemies
{
    /// <summary>
    /// Makes multiple enemies fight as a group instead of dog-piling the player:
    ///
    ///  1. ATTACK TOKENS — only <see cref="maxSimultaneousAttackers"/> enemies may be
    ///     in their Attack state at once. Everyone else circles the player and waits
    ///     for a token to free up (classic "Batman brawl" coordination).
    ///
    ///  2. SURROUND SLOTS — engaged enemies are each given an angle around the player
    ///     so they spread out and flank instead of stacking on one side.
    ///
    ///  3. GROUP ALERTS — when one enemy spots the player, it shouts to nearby
    ///     enemies so the whole camp joins the fight.
    ///
    /// A single instance is created automatically the first time an enemy needs it —
    /// no manual scene setup required (but you can place one to tweak values).
    /// </summary>
    public class EnemyGroupCoordinator : MonoBehaviour
    {
        [Header("Group Attack Settings")]
        [Tooltip("How many enemies are allowed to attack the player at the same time.")]
        [SerializeField] private int maxSimultaneousAttackers = 2;

        [Tooltip("How far a spotted-player alert travels to other enemies.")]
        [SerializeField] private float alertRadius = 20f;

        private static EnemyGroupCoordinator instance;

        private readonly List<EnemyAI> allEnemies = new List<EnemyAI>();
        private readonly List<EnemyAI> engagedEnemies = new List<EnemyAI>();   // chasing/circling/attacking
        private readonly List<EnemyAI> tokenHolders = new List<EnemyAI>();     // currently allowed to attack

        private static bool isTearingDown;

        /// <summary>
        /// The coordinator, created on demand. Returns null while the scene is being
        /// torn down so shutdown code never resurrects it (Unity logs an error if a
        /// GameObject is spawned during scene unload).
        /// </summary>
        public static EnemyGroupCoordinator Instance
        {
            get
            {
                if (isTearingDown) return null;

                if (instance == null)
                {
                    instance = FindAnyObjectByType<EnemyGroupCoordinator>();
                    if (instance == null)
                    {
                        var go = new GameObject("EnemyGroupCoordinator");
                        instance = go.AddComponent<EnemyGroupCoordinator>();
                    }
                }
                return instance;
            }
        }

        /// <summary>The existing coordinator, or null. Never creates one — safe in OnDisable/OnDestroy.</summary>
        public static EnemyGroupCoordinator Existing => instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            isTearingDown = false;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void OnApplicationQuit() => isTearingDown = true;

        // ---------------------------------------------------------------- registration

        public void Register(EnemyAI enemy)
        {
            if (!allEnemies.Contains(enemy)) allEnemies.Add(enemy);
        }

        public void Unregister(EnemyAI enemy)
        {
            allEnemies.Remove(enemy);
            engagedEnemies.Remove(enemy);
            tokenHolders.Remove(enemy);
        }

        // ---------------------------------------------------------------- engagement

        /// <summary>Called by an enemy when it starts fighting the player.</summary>
        public void ReportEngaged(EnemyAI enemy)
        {
            if (!engagedEnemies.Contains(enemy)) engagedEnemies.Add(enemy);
        }

        /// <summary>Called by an enemy when it loses the player / dies / resets.</summary>
        public void ReportDisengaged(EnemyAI enemy)
        {
            engagedEnemies.Remove(enemy);
            ReleaseAttackToken(enemy);
        }

        /// <summary>
        /// One enemy saw the player — wake up everyone nearby so they attack together.
        /// </summary>
        public void AlertNearbyEnemies(EnemyAI alerter, Vector3 playerPosition)
        {
            foreach (var enemy in allEnemies)
            {
                if (enemy == null || enemy == alerter) continue;
                if ((enemy.transform.position - alerter.transform.position).sqrMagnitude
                    <= alertRadius * alertRadius)
                {
                    enemy.OnGroupAlert(playerPosition);
                }
            }
        }

        // ---------------------------------------------------------------- attack tokens

        /// <summary>Ask for permission to attack. Returns true if a slot is free.</summary>
        public bool RequestAttackToken(EnemyAI enemy)
        {
            tokenHolders.RemoveAll(e => e == null);

            if (tokenHolders.Contains(enemy)) return true;
            if (tokenHolders.Count >= maxSimultaneousAttackers) return false;

            tokenHolders.Add(enemy);
            return true;
        }

        public void ReleaseAttackToken(EnemyAI enemy)
        {
            tokenHolders.Remove(enemy);
        }

        public bool HasAttackToken(EnemyAI enemy) => tokenHolders.Contains(enemy);

        // ---------------------------------------------------------------- surround slots

        /// <summary>
        /// A point on a ring around the player, unique per engaged enemy, so the
        /// group fans out and surrounds instead of clumping into one blob.
        /// </summary>
        public Vector3 GetSurroundPosition(EnemyAI enemy, Vector3 playerPosition, float ringRadius)
        {
            engagedEnemies.RemoveAll(e => e == null);

            if (engagedEnemies.Count == 0)
            {
                Vector3 dir = (enemy.transform.position - playerPosition).normalized;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                return playerPosition + dir * ringRadius;
            }

            int index = engagedEnemies.IndexOf(enemy);
            int count = Mathf.Max(1, engagedEnemies.Count);
            if (index < 0) index = 0;

            // Spread engaged enemies evenly around the circle. Bias the whole ring
            // toward each enemy's current side of the player so they don't all run
            // across to "their" slot and cross paths awkwardly.
            float baseAngle = Vector3.SignedAngle(
                Vector3.forward,
                (engagedEnemies[0].transform.position - playerPosition).normalized,
                Vector3.up);

            float angle = baseAngle + index * (360f / count);
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            return playerPosition + rot * Vector3.forward * ringRadius;
        }
    }
}

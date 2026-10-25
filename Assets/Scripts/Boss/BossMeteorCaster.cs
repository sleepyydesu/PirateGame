using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.Enemies
{
    // Put this on the boss root. BossAI starts and stops it.
    // It spawns meteors over time, around the player, and holds the spell pose.
    public sealed class BossMeteorCaster : MonoBehaviour
    {
        [Header("Testing only (leave empty in the real fight)")]
        [SerializeField] private MeteorSpell testSpell;

        private Animator animator;
        private Coroutine routine;
        private float savedAnimatorSpeed = 1f;
        private bool speedChanged;
        private readonly List<Meteor> active = new List<Meteor>();
        private readonly List<Vector3> recentTargets = new List<Vector3>();

        public bool IsCasting { get; private set; }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
        }

        private void OnDisable()
        {
            Stop(true);
        }

        // Right-click the component title in Play mode and pick "Test Cast".
        [ContextMenu("Test Cast")]
        private void TestCast()
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null || testSpell == null)
            {
                Debug.LogWarning("Test Cast needs a Test Spell and an object tagged Player.", this);
                return;
            }
            StartCast(testSpell, p.transform, gameObject, 0);
        }

        public void StartCast(MeteorSpell spell, Transform target, GameObject owner, int phaseIndex)
        {
            if (spell == null || spell.meteorPrefab == null || target == null) return;
            Stop(false);
            routine = StartCoroutine(Cast(spell, target, owner, phaseIndex));
        }

        // clearMeteors false: meteors already falling keep falling (a cancelled spell).
        // clearMeteors true: remove them all (boss died).
        public void Stop(bool clearMeteors)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            IsCasting = false;
            RestoreAnimator();

            if (clearMeteors)
            {
                foreach (Meteor m in active)
                    if (m != null) m.Cancel();
                active.Clear();
            }
        }

        private IEnumerator Cast(MeteorSpell spell, Transform target, GameObject owner, int phaseIndex)
        {
            IsCasting = true;
            bool later = phaseIndex > 0;
            int count = Mathf.Max(1, Mathf.RoundToInt(spell.meteorCount * (later ? spell.laterPhaseCountMultiplier : 1f)));
            float fallSpeed = spell.fallSpeed * (later ? spell.laterPhaseSpeedMultiplier : 1f);
            float duration = Mathf.Max(0f, spell.castDuration);
            float interval = Mathf.Max(0.05f, spell.waveInterval);
            // Epsilon stops float error (3 / 0.1 = 30.000002) from adding an extra wave.
            int waveCount = Mathf.Max(1, Mathf.CeilToInt(duration / interval - 0.001f));
            recentTargets.Clear();

            if (spell.holdPose && animator != null)
            {
                savedAnimatorSpeed = animator.speed;
                animator.speed = spell.holdPoseSpeed;
                speedChanged = true;
            }

            for (int wave = 0; wave < waveCount; wave++)
            {
                if (target == null) break;

                bool directHitThisWave = Random.value < spell.directHitChance;
                for (int i = 0; i < count; i++)
                    SpawnMeteor(spell, target, owner, fallSpeed, directHitThisWave && i == 0);

                if (wave < waveCount - 1) yield return new WaitForSeconds(interval);
            }

            while (HasMeteorsInFlight()) yield return null;

            IsCasting = false;
            RestoreAnimator();
            routine = null;
        }

        private void SpawnMeteor(MeteorSpell spell, Transform target, GameObject owner, float fallSpeed, bool directHit)
        {
            Vector3 center = target.position;
            Vector3 point = center;

            // Keep center hits from stacking: each wave can have at most one.
            if (!directHit)
                point = PickScatterPoint(spell, center);

            point = SnapToGround(spell, point, center.y);
            recentTargets.Add(point);
            if (recentTargets.Count > 12) recentTargets.RemoveAt(0);

            MeteorParts parts = spell.prefabPlaysItself
                ? spell.meteorPrefab.GetComponent<MeteorParts>()
                : null;
            bool hasSeparateBody = parts != null && parts.body != null;
            Vector3 spawnPosition = hasSeparateBody
                ? point
                : point + Vector3.up * spell.spawnHeight;
            GameObject go = Instantiate(spell.meteorPrefab, spawnPosition, Quaternion.identity);
            if (!go.TryGetComponent(out Meteor meteor)) meteor = go.AddComponent<Meteor>();

            active.RemoveAll(m => m == null);
            active.Add(meteor);
            meteor.Begin(spell, point, owner, fallSpeed);
        }

        private bool HasMeteorsInFlight()
        {
            active.RemoveAll(m => m == null);
            foreach (Meteor meteor in active)
                if (meteor.IsInFlight) return true;
            return false;
        }

        private Vector3 PickScatterPoint(MeteorSpell spell, Vector3 center)
        {
            Vector3 best = center;
            float bestDistance = -1f;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float radius = Random.Range(spell.scatterMinRadius, Mathf.Max(spell.scatterMinRadius, spell.scatterMaxRadius));
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                float nearest = float.MaxValue;
                foreach (Vector3 other in recentTargets)
                    nearest = Mathf.Min(nearest, Vector3.Distance(p, other));

                if (nearest >= spell.minSpacing) return p;
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = p;
                }
            }
            return best;
        }

        private static Vector3 SnapToGround(MeteorSpell spell, Vector3 point, float fallbackY)
        {
            point.y = fallbackY;
            if (spell.ground.value != 0 &&
                Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f,
                    spell.ground, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }
            return point;
        }

        private void RestoreAnimator()
        {
            if (speedChanged && animator != null) animator.speed = savedAnimatorSpeed;
            speedChanged = false;
        }
    }
}
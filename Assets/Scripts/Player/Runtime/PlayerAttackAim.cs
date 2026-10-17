using UnityEngine;
using PirateGame.Combat;

namespace PirateGame.PlayerV2
{
    /// <summary>
    /// Picks one visible enemy when a sword attack starts. It only supplies facing;
    /// it never moves the player, changes damage, or steers a dodge.
    /// Put this on the same player root as PlayerMotor and PlayerCombat.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerAttackAim : MonoBehaviour
    {
        [Header("Switch")]
        [SerializeField] private bool useAimAssist = true;
        [Tooltip("Restore the old camera-facing start. Aim assist then adds a small turn toward the chosen enemy.")]
        [SerializeField] private bool faceCameraOnAttackStart = true;

        [Header("What can be selected")]
        [Tooltip("Enemy/boss body collider layers only. Use the same enemy layers as Weapon Bridge Victims.")]
        [SerializeField] private LayerMask enemyLayers;
        [Tooltip("Solid walls/rocks/terrain only. Exclude player, enemies, weapons and VFX.")]
        [SerializeField] private LayerMask walls;

        [Header("How much help")]
        [SerializeField, Min(0.1f)] private float range = 3f;
        [Tooltip("30 means up to 30 degrees left OR right of camera-forward.")]
        [SerializeField, Range(1f, 80f)] private float halfAngle = 30f;
        [Tooltip("Reject enemies much higher or lower than the player. Compare the Health object roots.")]
        [SerializeField, Min(0f)] private float maximumHeightDifference = 1.8f;
        [Tooltip("Extra limit on early tracking. AE_Commit can end it sooner.")]
        [SerializeField, Min(0f)] private float maximumAssistSeconds = 0.25f;
        [Tooltip("Height of the visibility check above the player root.")]
        [SerializeField, Min(0.1f)] private float sightHeight = 1f;

        private readonly Collider[] nearby = new Collider[64];
        private PlayerMotor motor;
        private Health ownHealth, target;
        private Collider targetCollider;
        private Vector3 cameraForward, direction;
        private float startedAt;
        private bool tracking;

        public Health CurrentTarget => target;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            ownHealth = GetComponent<Health>();
        }

        private void Start()
        {
            if (useAimAssist && (enemyLayers.value == 0 || walls.value == 0))
                Debug.LogWarning("PlayerAttackAim: assign Enemy Layers and Walls. Until then, attacks use camera-forward only.", this);
        }

        public Vector3 BeginAttack()
        {
            EndAttack();
            cameraForward = motor.CameraDirection(Vector2.up).normalized;
            direction = cameraForward;
            startedAt = Time.time;

            // Matching the camera at entry restores the old aiming rule. After
            // this, the attack asset's bounded turn speed controls assistance.
            if (faceCameraOnAttackStart) motor.Face(cameraForward, 360f, 1f);

            // Fail closed if the wall mask is missing: never select through scenery.
            if (!useAimAssist || enemyLayers.value == 0 || walls.value == 0) return direction;

            Vector3 origin = transform.position + Vector3.up * sightHeight;
            int count = Physics.OverlapSphereNonAlloc(origin, range, nearby, enemyLayers,
                QueryTriggerInteraction.Collide);
            Collider[] candidates = nearby;
            if (count == nearby.Length)
            {
                // Rare crowd case. Do not let an arbitrary truncated query choose the target.
                candidates = Physics.OverlapSphere(origin, range, enemyLayers, QueryTriggerInteraction.Collide);
                count = candidates.Length;
            }

            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = candidates[i];
                if (candidate == null || candidate.transform.IsChildOf(transform)) continue;
                Health health = candidate.GetComponentInParent<Health>();
                if (!Eligible(health, candidate, out Vector3 toward, out float distance, out float angle)) continue;

                // Prefer the enemy the camera is aimed at; distance breaks close choices.
                float score = (angle / Mathf.Max(1f, halfAngle)) * 0.75f +
                    (distance / Mathf.Max(0.1f, range)) * 0.25f;
                if (score >= bestScore) continue;
                bestScore = score; target = health; targetCollider = candidate; direction = toward;
            }

            tracking = target != null;
            return direction;
        }

        public Vector3 DirectionBeforeCommit()
        {
            if (!tracking) return direction;
            if (!isActiveAndEnabled || !useAimAssist || Time.time - startedAt >= maximumAssistSeconds)
            {
                Commit(); return direction;
            }
            if (!Eligible(target, targetCollider, out Vector3 toward, out _, out _))
            {
                // Losing an enemy never switches to a different enemy mid-swing.
                // Keep the last valid direction, then let the swing finish.
                target = null; targetCollider = null; Commit(); return direction;
            }
            direction = toward;
            return direction;
        }

        public void Commit() { tracking = false; }

        public void EndAttack()
        {
            tracking = false; target = null; targetCollider = null;
        }

        private bool Eligible(Health health, Collider body, out Vector3 toward, out float distance, out float angle)
        {
            toward = Vector3.zero; distance = angle = 0f;
            if (walls.value == 0 || health == null || health == ownHealth || health.IsDead || !health.isActiveAndEnabled ||
                health.transform.IsChildOf(transform) || body == null || !body.enabled ||
                !body.gameObject.activeInHierarchy) return false;
            if ((enemyLayers.value & (1 << body.gameObject.layer)) == 0) return false;

            Vector3 delta = health.transform.position - transform.position;
            if (Mathf.Abs(delta.y) > maximumHeightDifference) return false;
            delta.y = 0f;
            distance = delta.magnitude;
            if (distance < 0.05f || distance > range) return false;
            toward = delta / distance;
            angle = Vector3.Angle(cameraForward, toward);
            if (angle > halfAngle) return false;

            Vector3 eye = transform.position + Vector3.up * sightHeight;
            return !Physics.Linecast(eye, body.bounds.center, walls, QueryTriggerInteraction.Ignore);
        }

        private void OnDisable() { EndAttack(); }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
            Vector3 p = transform.position;
            Gizmos.DrawWireSphere(p + Vector3.up * sightHeight, range);
            var m = GetComponent<PlayerMotor>();
            Vector3 forward = Application.isPlaying && m != null ? m.CameraDirection(Vector2.up).normalized : transform.forward;
            Gizmos.DrawRay(p, Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * range);
            Gizmos.DrawRay(p, Quaternion.AngleAxis(halfAngle, Vector3.up) * forward * range);
            if (target != null) Gizmos.DrawLine(p, target.transform.position);
        }
    }
}

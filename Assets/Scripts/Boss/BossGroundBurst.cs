using System.Collections.Generic;
using UnityEngine;
using PirateGame.Combat;

namespace PirateGame.Enemies
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class BossGroundBurst : MonoBehaviour
    {
        [SerializeField] private Transform impactPoint;
        [Tooltip("Optional separate ground-burst point for standing combo slams.")]
        [SerializeField] private Transform comboImpactPoint;
        [SerializeField] private LayerMask ground;
        [SerializeField] private LayerMask victims;
        [SerializeField] private LayerMask blocking;

        [Header("Camera shake")]
        [SerializeField] private CameraShake cameraShake;
        [SerializeField] private Transform shakeListener;

        [SerializeField, Min(0.1f)]
        private float shakeRadius = 10f;

        [SerializeField, Min(0f)]
        private float shakeAmplitude = 1.5f;

        [SerializeField, Min(0f)]
        private float shakeFrequency = 2f;

        [SerializeField, Min(0f)]
        private float shakeSeconds = 0.2f;

        private readonly Collider[] buffer = new Collider[64];

        private readonly HashSet<IDamageable> hit =
            new HashSet<IDamageable>();

        private GameObject owner;
        private CaptainAttack move;
        private Vector3 fallbackPoint;
        private bool queued;
        private bool comboImpact;
        private Transform queuedImpactPoint;

        public void QueueImpact(
            GameObject attacker,
            CaptainAttack attack,
            Vector3 landingPoint,
            bool fromCombo = false,
            Transform pointOverride = null)
        {
            if (!isActiveAndEnabled || queued || attack == null)
                return;

            owner = attacker;
            move = attack;
            fallbackPoint = landingPoint;
            comboImpact = fromCombo;
            queuedImpactPoint = pointOverride;
            queued = true;
        }

        private void OnDisable()
        {
            queued = false;
        }

        private void LateUpdate()
        {
            if (!queued)
                return;

            queued = false;

            if (owner == null || move == null)
                return;

            Health ownerHealth = owner.GetComponent<Health>();

            if (ownerHealth != null && ownerHealth.IsDead)
                return;

            Transform point = queuedImpactPoint != null
                ? queuedImpactPoint
                : comboImpact && comboImpactPoint != null
                    ? comboImpactPoint
                    : impactPoint;

            Vector3 source = point != null
                ? point.position
                : fallbackPoint;

            if (!Physics.Raycast(
                    source + Vector3.up * 2f,
                    Vector3.down,
                    out RaycastHit floor,
                    6f,
                    ground,
                    QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning(
                    "Ground burst found no floor. " +
                    "Check Ground layers and Impact Point.",
                    this);
                return;
            }

            Vector3 center = floor.point;

            if (move.groundBurstVfx != null)
            {
                GameObject effect = Instantiate(
                    move.groundBurstVfx,
                    center + floor.normal * 0.02f,
                    Quaternion.FromToRotation(
                        Vector3.up,
                        floor.normal));

                Debug.Log(
    $"GROUND BURST SPAWNED | Frame: {Time.frameCount}",
    effect);

                foreach (ParticleSystem particles in
                         effect.GetComponentsInChildren<ParticleSystem>())
                {
                    Transform parent = particles.transform.parent;

                    if (parent == null ||
                        parent.GetComponentInParent<ParticleSystem>() == null)
                    {
                        particles.Play(true);
                    }
                }

                Destroy(
                    effect,
                    Mathf.Max(0.1f, move.groundBurstVfxLifetime));
            }

            if (cameraShake != null &&
                shakeListener != null &&
                shakeRadius > 0f)
            {
                float strength = 1f - Mathf.Clamp01(
                    Vector3.Distance(
                        center,
                        shakeListener.position) / shakeRadius);

                if (strength > 0f)
                {
                    cameraShake.Shake(
                        shakeAmplitude * strength,
                        shakeFrequency,
                        shakeSeconds);
                }
            }

            float radius =
                Mathf.Max(0.1f, move.groundBurstRadius);

            float height =
                Mathf.Max(0.05f, move.groundBurstHeight);

            float searchRadius =
                Mathf.Sqrt(radius * radius + height * height);

            int count = Physics.OverlapSphereNonAlloc(
                center,
                searchRadius,
                buffer,
                victims,
                QueryTriggerInteraction.Collide);

            Collider[] contacts = buffer;

            if (count == buffer.Length)
            {
                contacts = Physics.OverlapSphere(
                    center,
                    searchRadius,
                    victims,
                    QueryTriggerInteraction.Collide);

                count = contacts.Length;
            }

            hit.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider col = contacts[i];

                if (col == null ||
                    col.transform.IsChildOf(owner.transform))
                {
                    continue;
                }

                IDamageable receiver =
                    col.GetComponentInParent<IDamageable>();

                if (receiver == null ||
                    receiver.IsDead ||
                    hit.Contains(receiver))
                {
                    continue;
                }

                Vector3 contact = col.ClosestPoint(center);
                Vector3 offset = contact - center;

                if (Mathf.Abs(offset.y) > height)
                    continue;

                offset.y = 0f;

                if (offset.sqrMagnitude > radius * radius)
                    continue;

                if (Physics.Linecast(
                        center + Vector3.up * 0.15f,
                        col.bounds.center,
                        blocking,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Vector3 away = col.bounds.center - center;
                away.y = 0f;

                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector3.ProjectOnPlane(
                        owner.transform.forward,
                        Vector3.up);
                }

                DamageInfo info = new DamageInfo(
                    comboImpact ? move.comboBurstDamage : move.damage,
                    owner,
                    contact,
                    comboImpact ? move.comboBurstReaction : move.hitReaction,
                    comboImpact ? move.comboBurstReactionMode : move.hitReactionMode);

                info.HasImpactOrigin = true;
                info.ImpactOrigin = center;

                info.KnockbackDirection = away.normalized;
                info.KnockbackDistance =
                    Mathf.Max(0f, move.knockbackDistance);

                info.KnockbackSeconds =
                    Mathf.Max(0.05f, move.knockbackSeconds);

                hit.Add(receiver);
                receiver.TakeDamage(info);
            }
        }
    }
}
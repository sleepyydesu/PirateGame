using UnityEngine;

namespace PirateGame.Combat
{
    public sealed class SwordAirSlashProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.01f)]
        private float radius = 0.35f;

        [SerializeField, Min(0.1f)]
        private float maximumDistance = 25f;

        [SerializeField, Min(0.1f)]
        private float lifetime = 4f;

        [SerializeField] private LayerMask targets;
        [SerializeField] private LayerMask blocking;
        [SerializeField] private ParticleSystem mainVisual;

        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly RaycastHit[] castBuffer = new RaycastHit[32];

        private GameObject owner;
        private Vector3 direction;
        private float speed, damage, distanceLeft, timeLeft;
        private HitReactionType reaction;
        private HitReactionMode reactionMode;
        private bool launched, finished;

        public void Launch(
            GameObject attacker,
            Vector3 heading,
            float launchSpeed,
            float amount,
            HitReactionType hitType,
            HitReactionMode hitMode,
            AudioClip flightSound = null,
            float flightSoundVolume = 1f)
        {
            if (launched || heading.sqrMagnitude < 0.0001f)
            {
                Destroy(gameObject);
                return;
            }

            owner = attacker;
            direction = heading.normalized;
            speed = Mathf.Max(0.1f, launchSpeed);
            damage = Mathf.Max(0f, amount);
            reaction = hitType;
            reactionMode = hitMode;

            radius = Mathf.Max(0.01f, radius);
            distanceLeft = Mathf.Max(0.1f, maximumDistance);
            timeLeft = Mathf.Max(0.1f, lifetime);

            launched = true;
            CheckStartingOverlap();

            if (!finished && flightSound != null)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = 1f;
                source.maxDistance = 30f;
                source.volume = Mathf.Clamp01(flightSoundVolume);
                source.clip = flightSound;
                source.Play();
            }

            if (!finished && mainVisual != null)
                mainVisual.Play(true);
        }

        private bool Classify(
            Collider col,
            out IDamageable receiver)
        {
            receiver = null;

            if (col == null ||
                owner == null ||
                col.transform.IsChildOf(owner.transform))
            {
                return false;
            }

            int bit = 1 << col.gameObject.layer;

            // Solid world geometry stops the projectile.
            if ((blocking.value & bit) != 0 && !col.isTrigger)
                return true;

            if ((targets.value & bit) == 0)
                return false;

            receiver = col.GetComponentInParent<IDamageable>();

            return receiver != null && !receiver.IsDead;
        }

        private void CheckStartingOverlap()
        {
            int mask = targets.value | blocking.value;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                radius,
                overlapBuffer,
                mask,
                QueryTriggerInteraction.Collide);

            Collider[] contacts = overlapBuffer;

            if (count == overlapBuffer.Length)
            {
                contacts = Physics.OverlapSphere(
                    transform.position,
                    radius,
                    mask,
                    QueryTriggerInteraction.Collide);

                count = contacts.Length;
            }

            IDamageable victim = null;
            Vector3 point = transform.position;

            for (int i = 0; i < count; i++)
            {
                if (!Classify(contacts[i], out IDamageable receiver))
                    continue;

                // A wall takes priority over an overlapping victim.
                if (receiver == null)
                {
                    Impact(null, transform.position);
                    return;
                }

                if (victim == null)
                {
                    victim = receiver;
                    point = contacts[i].ClosestPoint(transform.position);
                }
            }

            if (victim != null)
                Impact(victim, point);
        }

        private void Update()
        {
            if (!launched || finished)
                return;

            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;

            if (dt <= 0f)
                return;

            timeLeft -= dt;

            if (timeLeft <= 0f || distanceLeft <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            CheckStartingOverlap();

            if (finished)
                return;

            float step = Mathf.Min(speed * dt, distanceLeft);
            int mask = targets.value | blocking.value;

            int count = Physics.SphereCastNonAlloc(
                transform.position,
                radius,
                direction,
                castBuffer,
                step,
                mask,
                QueryTriggerInteraction.Collide);

            RaycastHit[] hits = castBuffer;

            if (count == castBuffer.Length)
            {
                hits = Physics.SphereCastAll(
                    transform.position,
                    radius,
                    direction,
                    step,
                    mask,
                    QueryTriggerInteraction.Collide);

                count = hits.Length;
            }

            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            IDamageable nearestReceiver = null;

            for (int i = 0; i < count; i++)
            {
                if (!Classify(
                        hits[i].collider,
                        out IDamageable receiver))
                {
                    continue;
                }

                bool closer = hits[i].distance < nearestDistance;

                bool tiedWall =
                    Mathf.Approximately(
                        hits[i].distance,
                        nearestDistance) &&
                    receiver == null;

                if (!closer && !tiedWall)
                    continue;

                nearest = i;
                nearestDistance = hits[i].distance;
                nearestReceiver = receiver;
            }

            if (nearest >= 0)
            {
                transform.position += direction * nearestDistance;
                Impact(nearestReceiver, hits[nearest].point);
                return;
            }

            transform.position += direction * step;
            distanceLeft -= step;
        }

        private void Impact(IDamageable receiver, Vector3 point)
        {
            if (finished)
                return;

            finished = true;
            Destroy(gameObject);

            if (receiver != null)
            {
                receiver.TakeDamage(new DamageInfo(
                    damage,
                    owner,
                    point,
                    reaction,
                    reactionMode));
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
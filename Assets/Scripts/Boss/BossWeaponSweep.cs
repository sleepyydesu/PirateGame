using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using PirateGame.Combat;

namespace PirateGame.Enemies
{
    public sealed class BossWeaponSweep : MonoBehaviour
    {
        [SerializeField] private Transform bladeBase, bladeTip;
        [SerializeField, Min(0.01f)] private float radius = 0.12f;
        [SerializeField] private LayerMask victims;
        [Tooltip("Environment only. Exclude boss and player layers.")]
        [SerializeField] private LayerMask obstruction;
        public UnityEvent onContact = new UnityEvent();
        private readonly HashSet<IDamageable> hit = new HashSet<IDamageable>();
        private Collider[] overlaps = new Collider[32];
        private GameObject owner;
        private float damage;
        private HitReactionType hitReaction;
        private HitReactionMode hitReactionMode;
        private bool open;
        private Vector3 previousBase, previousTip;
        public void Open(
        GameObject attacker,
        float amount,
        HitReactionType reaction = HitReactionType.Default,
        HitReactionMode reactionMode = HitReactionMode.Stagger)
        {
            Close();
            hitReaction = reaction;
            hitReactionMode = reactionMode;
            if (bladeBase == null || bladeTip == null) return;
            owner = attacker; damage = amount; hit.Clear(); open = true;
            previousBase = bladeBase.position; previousTip = bladeTip.position;
        }
        public void Close() { open = false; }
        private void OnDisable() { Close(); }
        private void LateUpdate()
        {
            if (!open || bladeBase == null || bladeTip == null) return;
            Vector3 a = bladeBase.position, b = bladeTip.position;
            // Sample the swept blade, rather than testing distance from the boss pivot.
            // Capped work is deliberate; extremely fast blades need a dedicated swept-volume solver.
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
                Vector3.Distance(previousBase, a), Vector3.Distance(previousTip, b)) / radius), 1, 12);
            for (int i = 0; i <= steps && open; i++)
                Sample(Vector3.Lerp(previousBase, a, (float)i / steps),
                       Vector3.Lerp(previousTip, b, (float)i / steps));
            previousBase = a; previousTip = b;
        }
        private void Sample(Vector3 a, Vector3 b)
        {
            int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlaps, victims,
                QueryTriggerInteraction.Collide);
            // Never silently drop victims if the non-alloc buffer fills.
            if (count == overlaps.Length)
            {
                overlaps = Physics.OverlapCapsule(a, b, radius, victims, QueryTriggerInteraction.Collide);
                count = overlaps.Length;
            }
            for (int i = 0; i < count && open; i++)
            {
                Collider col = overlaps[i];
                if (col == null || col.transform.IsChildOf(owner.transform)) continue;
                IDamageable receiver = null;
                foreach (MonoBehaviour component in col.GetComponentsInParent<MonoBehaviour>())
                    if (component is IDamageable candidate) { receiver = candidate; break; }
                if (receiver == null || receiver.IsDead || hit.Contains(receiver)) continue;
                Vector3 point = col.ClosestPoint(b);
                if (Physics.Linecast(owner.transform.position + Vector3.up, point, obstruction,
                    QueryTriggerInteraction.Ignore)) continue;
                // Deduplicate by receiver, not collider: one body may have many colliders.
                hit.Add(receiver);
                receiver.TakeDamage(new DamageInfo(
                damage,
                owner,
                point,
                hitReaction,
                hitReactionMode));
                onContact.Invoke(); // Presentation hook, not proof that health actually decreased.
            }
        }
    }
}

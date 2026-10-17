using System.Collections.Generic;
using UnityEngine;
using PirateGame.Combat;
namespace PirateGame.PlayerV2
{
    // Retains your existing spawning, upgrades entry point and attachment/VFX methods.
    // Legacy DamageDealer is DISABLED; only this component samples contacts.
    public sealed class PlayerWeaponBridge : MonoBehaviour
    {
        [SerializeField] private EquipmentSystem equipment;
        [SerializeField] private LayerMask victims, obstruction;
        [SerializeField, Min(0.01f)] private float radius = 0.1f;
        private Transform bladeBase, bladeTip;
        private DamageDealer legacyDealer;
        private readonly HashSet<IDamageable> hit = new HashSet<IDamageable>();
        private readonly Collider[] buffer = new Collider[64];
        private bool open, ready;
        private bool trackingAttack;
        private bool attackHadHitWindow;
        private bool attackTouchedEnemy;
        private float damage;
        private HitReactionType hitReaction;
        private HitReactionMode hitReactionMode;
        private Vector3 previousBase, previousTip;
        public bool Drawn { get; private set; }
        public bool Ready => ready;

        public void BeginAttackTracking()
        {
            trackingAttack = true;
            attackHadHitWindow = false;
            attackTouchedEnemy = false;
        }

        public bool FinishAttackTracking()
        {
            bool missed = trackingAttack &&
                          attackHadHitWindow &&
                          !attackTouchedEnemy;

            CancelAttackTracking();
            return missed;
        }

        public void CancelAttackTracking()
        {
            trackingAttack = false;
            attackHadHitWindow = false;
            attackTouchedEnemy = false;
        }

        public void Initialize()
        {
            if (equipment == null) equipment = GetComponent<EquipmentSystem>();
            legacyDealer = equipment != null ? equipment.WeaponDamageDealer : null;
            if (legacyDealer == null) { Debug.LogError("Weapon bridge needs the existing spawned DamageDealer.", this); return; }
            legacyDealer.EndDealDamage(); legacyDealer.enabled = false;
            // These names match EquipmentSystem's usual weapon conventions; validate your sword prefab.
            foreach (Transform t in equipment.WeaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "SwordTrailPoint") bladeBase = t;
                if (t.name == "SlashSpawnPoint") bladeTip = t;
            }
            ready = bladeBase != null && bladeTip != null;
            if (!ready) Debug.LogError("Sword needs unique SwordTrailPoint and SlashSpawnPoint children.", this);
            Close(); // Existing EquipmentSystem spawns sheathed.
        }
        public void Attach(bool draw)
        {
            Close();
            if (equipment == null) return;
            if (draw) equipment.DrawWeapon(); else equipment.SheathWeapon();
            Drawn = draw;
        }
        public void Open(
        float amount,
        HitReactionType reaction = HitReactionType.Default,
        HitReactionMode reactionMode = HitReactionMode.Stagger)
        {
            hitReaction = reaction;
            hitReactionMode = reactionMode;
            Close(); if (!ready || !Drawn) return;
            if (trackingAttack)
                attackHadHitWindow = true;
            damage = amount * (legacyDealer != null ? Mathf.Max(0f, legacyDealer.damageMultiplier) : 1f);
            hit.Clear(); previousBase = bladeBase.position; previousTip = bladeTip.position; open = true;
            equipment.StartTrail();
        }
        public void Close()
        {
            open = false;
            if (equipment != null) equipment.StopTrail();
            if (legacyDealer != null) legacyDealer.EndDealDamage();
        }
        private void OnDisable() { Close(); }
        private void LateUpdate()
        {
            if (!open || bladeBase == null || bladeTip == null) return;
            Vector3 a = bladeBase.position, b = bladeTip.position;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Vector3.Distance(previousBase, a),
                Vector3.Distance(previousTip, b)) / radius), 1, 12);
            for (int i = 0; i <= steps && open; i++)
            {
                Vector3 from = Vector3.Lerp(previousBase, a, (float)i / steps), to = Vector3.Lerp(previousTip, b, (float)i / steps);
                int n = Physics.OverlapCapsuleNonAlloc(from, to, radius, buffer, victims, QueryTriggerInteraction.Collide);
                Collider[] contacts = buffer;
                if (n == buffer.Length)
                {
                    contacts = Physics.OverlapCapsule(from, to, radius, victims, QueryTriggerInteraction.Collide);
                    n = contacts.Length; // Rare fallback: correctness over silent truncation.
                }
                for (int j = 0; j < n && open; j++)
                {
                    Collider c = contacts[j]; if (c.transform.IsChildOf(transform)) continue;
                    IDamageable receiver = c.GetComponentInParent<IDamageable>();
                    if (receiver == null || receiver.IsDead || hit.Contains(receiver)) continue;
                    Vector3 point = c.ClosestPoint(to);
                    if (Physics.Linecast(transform.position + Vector3.up, point, obstruction, QueryTriggerInteraction.Ignore)) continue;
                    hit.Add(receiver);

                    if (trackingAttack)
                        attackTouchedEnemy = true;

                    receiver.TakeDamage(new DamageInfo(
                    damage,
                    gameObject,
                    point,
                    hitReaction,
                    hitReactionMode));
                }
            }
            previousBase = a; previousTip = b;
        }
    }
}

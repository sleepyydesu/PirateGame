using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PirateGame.Combat;

namespace PirateGame.Enemies
{
    // One meteor. It forms in the air, falls, then explodes and hurts anything in the blast.
    // You do not need to add this to the prefab. The caster adds it for you.
    public sealed class Meteor : MonoBehaviour
    {
        private GameObject marker;
        private MeteorParts parts;

        public bool IsInFlight { get; private set; }

        public void Begin(MeteorSpell spell, Vector3 groundPoint, GameObject owner, float fallSpeed)
        {
            IsInFlight = true;
            StartCoroutine(Run(spell, groundPoint, owner, fallSpeed));
        }

        public void Cancel()
        {
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (marker != null) Destroy(marker);
        }

        private IEnumerator Run(MeteorSpell spell, Vector3 groundPoint, GameObject owner, float fallSpeed)
        {
            // The meteor must never push anything. Damage comes from the blast.
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

            // Self-playing VFX can use a separate body so its ground marker stays in place.
            if (spell.prefabPlaysItself)
            {
                parts = GetComponent<MeteorParts>();
                bool hasSeparateBody = parts != null && parts.body != null;

                float safeFallSpeed = Mathf.Max(0.1f, fallSpeed);
                float travelTime = spell.spawnHeight / safeFallSpeed;
                float warningTime = Mathf.Max(0f, spell.impactDelay - travelTime);
                ParticleSystem[] markerSystems = null;

                if (hasSeparateBody)
                {
                    ResetParticles();
                    SetImpactEffectsActive(false);
                    transform.position = groundPoint;
                    Vector3 bodyLocalPosition = parts.body.localPosition;
                    bodyLocalPosition.y = spell.spawnHeight;
                    parts.body.localPosition = bodyLocalPosition;
                    PlaySound(spell.formingSound, parts.body.position, spell.formingSoundVolume);

                    // Small margin so a slow frame cannot end them a moment before impact.
                    float untilImpact = warningTime + travelTime + 0.25f;

                    if (parts.marker != null)
                    {
                        parts.marker.transform.position = groundPoint + Vector3.up * 0.05f;
                        markerSystems = parts.marker.GetComponentsInChildren<ParticleSystem>(true);
                        KeepUntilImpact(markerSystems, untilImpact);
                        PlayParticles(markerSystems);
                    }

                    KeepUntilImpact(parts.flightEffects, untilImpact);
                    PlayParticles(parts.flightEffects);
                }
                else
                {
                    parts = null;
                    transform.position = groundPoint + Vector3.up * spell.spawnHeight;
                    PlaySound(spell.formingSound, transform.position, spell.formingSoundVolume);
                }

                if (warningTime > 0f) yield return new WaitForSeconds(warningTime);

                if (hasSeparateBody)
                {
                    Vector3 bodyLocalPosition = parts.body.localPosition;
                    while (bodyLocalPosition.y > 0.01f)
                    {
                        bodyLocalPosition.y = Mathf.MoveTowards(
                            bodyLocalPosition.y, 0f, safeFallSpeed * Time.deltaTime);
                        parts.body.localPosition = bodyLocalPosition;
                        yield return null;
                    }
                    bodyLocalPosition.y = 0f;
                    parts.body.localPosition = bodyLocalPosition;
                }
                else
                {
                    while (transform.position.y > groundPoint.y + 0.01f)
                    {
                        transform.position = Vector3.MoveTowards(
                            transform.position, groundPoint, safeFallSpeed * Time.deltaTime);
                        yield return null;
                    }
                }

                StopParticles(parts != null ? parts.flightEffects : null);
                StopParticles(markerSystems);
                SetImpactEffectsActive(true);
                PlayParticles(parts != null ? parts.impactParticleSystems : null);
                PlaySound(spell.impactSound, groundPoint, spell.impactSoundVolume);
                DealDamage(spell, groundPoint, owner);
                IsInFlight = false;
                Destroy(gameObject, spell.prefabLifetime);
                yield break;
            }

            // ---- Normal mode (separate prefabs), same as the original ----
            Vector3 fullScale = transform.localScale;
            transform.position = groundPoint + Vector3.up * spell.spawnHeight;
            transform.localScale = Vector3.zero;
            PlaySound(spell.formingSound, transform.position, spell.formingSoundVolume);

            if (spell.groundMarkerPrefab != null)
            {
                marker = Instantiate(spell.groundMarkerPrefab, groundPoint + Vector3.up * 0.05f, Quaternion.identity);
                Vector3 s = marker.transform.localScale;
                float diameter = spell.blastRadius * 2f;
                marker.transform.localScale = new Vector3(diameter * s.x, s.y, diameter * s.z);
            }

            // 1. Form
            float t = 0f;
            while (t < spell.formTime)
            {
                t += Time.deltaTime;
                float k = spell.formTime <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, t / spell.formTime);
                transform.localScale = fullScale * k;
                transform.Rotate(Vector3.one * 120f * Time.deltaTime);
                yield return null;
            }
            transform.localScale = fullScale;

            // 2. Fall
            while (transform.position.y > groundPoint.y + 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, groundPoint, fallSpeed * Time.deltaTime);
                transform.Rotate(Vector3.one * 240f * Time.deltaTime);
                yield return null;
            }

            // 3. Impact
            PlaySound(spell.impactSound, groundPoint, spell.impactSoundVolume);
            DealDamage(spell, groundPoint, owner);
            IsInFlight = false;

            if (spell.impactEffectPrefab != null)
            {
                GameObject fx = Instantiate(spell.impactEffectPrefab, groundPoint, Quaternion.identity);
                Destroy(fx, 4f);
            }

            Destroy(gameObject);
        }

        private static void PlaySound(AudioClip clip, Vector3 point, float volume)
        {
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, point, Mathf.Clamp01(volume));
        }

        private void SetImpactEffectsActive(bool active)
        {
            if (parts == null || parts.impactEffects == null) return;

            foreach (GameObject effect in parts.impactEffects)
            {
                if (effect == null) continue;

                effect.SetActive(active);
                if (active)
                    PlayParticles(effect.GetComponentsInChildren<ParticleSystem>(true));
            }
        }

        private void ResetParticles()
        {
            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void PlayParticles(ParticleSystem[] systems)
        {
            if (systems == null) return;

            foreach (ParticleSystem system in systems)
            {
                if (system == null) continue;

                system.Clear(false);
                system.Play(false);
            }
        }

        // The VFX is authored as a ~1 s one-shot. Stretch particle lifetimes on this instance
        // so they survive until impact, and drop authored velocity because this script drives the fall.
        private static void KeepUntilImpact(ParticleSystem[] systems, float seconds)
        {
            if (systems == null) return;

            foreach (ParticleSystem system in systems)
            {
                if (system == null) continue;

                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocity.enabled = false;

                ParticleSystem.MainModule main = system.main;
                ParticleSystem.MinMaxCurve lifetime = main.startLifetime;
                float shortest = float.MaxValue;
                for (int i = 0; i <= 10; i++)
                    shortest = Mathf.Min(shortest, lifetime.Evaluate(i / 10f, 0f), lifetime.Evaluate(i / 10f, 1f));

                if (shortest <= 0f || shortest >= seconds) continue;

                float scale = seconds / shortest;
                switch (lifetime.mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        lifetime.constant *= scale;
                        break;
                    case ParticleSystemCurveMode.TwoConstants:
                        lifetime.constantMin *= scale;
                        lifetime.constantMax *= scale;
                        break;
                    default:
                        lifetime.curveMultiplier *= scale;
                        break;
                }
                main.startLifetime = lifetime;
            }
        }

        private static void StopParticles(ParticleSystem[] systems)
        {
            if (systems == null) return;

            foreach (ParticleSystem system in systems)
                if (system != null)
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void DealDamage(MeteorSpell spell, Vector3 groundPoint, GameObject owner)
        {
            Collider[] hits = Physics.OverlapSphere(groundPoint, spell.blastRadius, spell.victims,
                QueryTriggerInteraction.Ignore);
            var alreadyHit = new HashSet<IDamageable>();
            foreach (Collider c in hits)
            {
                if (owner != null && c.transform.IsChildOf(owner.transform)) continue;
                IDamageable target = c.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead || !alreadyHit.Add(target)) continue;
                target.TakeDamage(new DamageInfo(spell.damage, owner, c.bounds.ClosestPoint(groundPoint)));
            }
        }
    }
}
using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using PirateGame.Combat;
using PirateGame.PlayerV2;
using Random = UnityEngine.Random;

namespace PirateGame.Enemies
{
    // Another part of the EXISTING BossAI class, not a second component.
    // Place in the same assembly/folder as BossAI.cs and install the guide's hooks.
    public sealed partial class BossAI
    {
        [Serializable]
        public sealed class DuelSettings
        {
            [Tooltip("Enable after adding the defense Animator states and events.")]
            public bool enabled;
            public bool allowGuard = true;
            public bool allowBackstep = true;

            [Header("Visible player attacks")]
            [Min(0.1f)] public float noticeDistance = 3.6f;
            [Range(1f, 180f)] public float sightHalfAngle = 85f;
            [Range(1f, 180f)] public float playerAttackHalfAngle = 80f;
            public Vector2 reactionSeconds = new Vector2(0.18f, 0.30f);
            [Range(0f, 1f)] public float guardChance = 0.45f;
            [Range(0f, 1f)] public float backstepChance = 0.20f;

            [Header("Guard")]
            public string guardState = "Base Layer.Boss_Guard";
            public string blockHitState = "Base Layer.Boss_BlockHit";
            [Min(0.1f)] public float guardHoldSeconds = 0.85f;
            [Min(0.1f)] public float guardRaiseWatchdog = 0.65f;
            [Min(0.1f)] public float blockHitWatchdog = 1f;
            [Min(0f)] public float guardCooldown = 1f;
            [Range(0f, 180f)] public float blockHalfAngle = 60f;
            [Range(0f, 1f)] public float chipFraction = 0.05f;
            [Min(0f)] public float blockedPoiseMultiplier = 0.6f;
            [Min(1f)] public float heavyGuardMultiplier = 1.5f;
            [Min(0f)] public float guardPushDistance = 0.15f;
            [Min(0.05f)] public float guardPushSeconds = 0.12f;
            [Min(0f)] public float fatiguePerBlock = 5f;

            [Header("Backstep - in-place clip; no invincibility")]
            public string backstepState = "Base Layer.Boss_Backstep";
            [Min(0.1f)] public float backstepDistance = 1.2f;
            [Min(0.05f)] public float backstepMoveSeconds = 0.3f;
            [Min(0.1f)] public float backstepWatchdog = 1.3f;
            [Min(0f)] public float backstepCooldown = 2.5f;
            [Min(0f)] public float backstepFatigue = 12f;
            [Min(0f)] public float defenseRecovery = 0.18f;

            [Header("Counter after an observed opening")]
            [Range(0f, 1f)] public float counterChance = 0.6f;
            [Min(0f)] public float counterCooldown = 2f;
            [Min(0.1f)] public float openingLifetime = 0.9f;
            [Tooltip("Existing melee assets, also present in the boss Attacks list. Empty uses eligible melee attacks from that list.")]
            public CaptainAttack[] counterAttacks = new CaptainAttack[0];

            [Header("Presentation only")]
            public UnityEvent onGuardImpact = new UnityEvent();
            public UnityEvent onGuardBroken = new UnityEvent();
            public UnityEvent onBackstep = new UnityEvent();
            public UnityEvent onCounter = new UnityEvent();
        }

        [Header("Reactive duel")]
        [SerializeField] private DuelSettings duel = new DuelSettings();

        private BossStateMachine.Node duelGuard, duelBackstep;
        private PlayerCombat duelPlayerCombat;
        private Transform duelPlayerReference;
        private uint duelObservedSerial, duelThreatSerial;
        private bool duelPlayerWasAttacking, duelThreatPending;
        private float duelThreatAt, duelThreatExpires;
        private float duelGuardReadyAt, duelBackstepReadyAt, duelCounterReadyAt;
        private float duelOpeningAt = float.NegativeInfinity;
        private float duelOpeningExpires = float.NegativeInfinity;
        private float duelSeenWhiffAt = float.NegativeInfinity;
        private bool duelGuardRaised, duelGuardEverRaised, duelGuardImpactPlaying;
        private bool duelGuardImpactQueued, duelGuardBroken, duelClipCompleted;
        private int duelImpactSignals;
        private float duelGuardUntil;
        private Vector3 duelPushDirection;
        private float duelPushDistance, duelPushElapsed, duelPushDuration;
        private Vector3 duelBackstepDirection;
        private float duelBackstepRemaining, duelMoveAge;
        private bool duelBackstepMoving, duelBackstepStarted;

        private bool DuelNeutral => CurrentState == State.Chase || CurrentState == State.Circle;
        private bool DuelHasPush => duelPushElapsed < duelPushDuration;
        private float DuelReactionDelay()
        {
            float a = Mathf.Max(0.05f, Mathf.Min(duel.reactionSeconds.x, duel.reactionSeconds.y));
            float b = Mathf.Max(a, Mathf.Max(duel.reactionSeconds.x, duel.reactionSeconds.y));
            return Random.Range(a, b);
        }

        private void BuildDuelStates()
        {
            duelGuard = Leaf("Guard", combat, State.Guard, EnterDuelGuard, TickDuelGuard);
            duelBackstep = Leaf("Backstep", combat, State.Backstep, EnterDuelBackstep, TickDuelBackstep);
        }

        private bool HasDuelState(string path)
        {
            return animator != null && !string.IsNullOrWhiteSpace(path) &&
                animator.HasState(0, Animator.StringToHash(path));
        }

        private void ValidateDuelSetup()
        {
            if (!duel.enabled) return;
            if (duel.allowGuard && !HasDuelState(duel.guardState))
                Debug.LogWarning("Boss duel: Guard state is missing; guarding will be unavailable.", this);
            if (duel.allowBackstep && !HasDuelState(duel.backstepState))
                Debug.LogWarning("Boss duel: Backstep state is missing; backsteps will be unavailable.", this);
            foreach (var a in duel.counterAttacks ?? new CaptainAttack[0])
                if (a != null && Array.IndexOf(attacks, a) < 0)
                    Debug.LogWarning("Boss duel: add counter asset to the normal Attacks list too: " + a.name, this);
        }

        private void ObserveDuelPlayer()
        {
            if (duelPlayerReference != player)
            {
                duelPlayerReference = player;
                duelPlayerCombat = player == null ? null : player.GetComponentInParent<PlayerCombat>();
                if (duelPlayerCombat == null && player != null)
                    duelPlayerCombat = player.GetComponentInChildren<PlayerCombat>();
                duelObservedSerial = 0;
                duelPlayerWasAttacking = false;
                duelThreatPending = false;
                duelOpeningExpires = float.NegativeInfinity;
            }

            if (duelPlayerCombat == null || !duelPlayerCombat.Ready)
            {
                duelPlayerWasAttacking = false;
                duelThreatPending = false;
                return;
            }

            bool visibleAttack = duelPlayerCombat.IsAttackAnimationVisible;
            if (duelPlayerWasAttacking && !visibleAttack &&
                (duelPlayerCombat.IsInAttackRecovery || duelPlayerCombat.IsFree) &&
                (DuelNeutral || CurrentState == State.Guard || CurrentState == State.Backstep) &&
                Distance <= duel.noticeDistance && Visible())
                QueueDuelOpening();

            duelPlayerWasAttacking = visibleAttack;
            if (!visibleAttack || duelPlayerCombat.AttackSerial == duelObservedSerial) return;

            Vector3 towardPlayer = Flat(player.position - transform.position);
            Vector3 towardBoss = -towardPlayer;
            if (!DuelNeutral ||
                !TargetAlive || Distance > duel.noticeDistance || !Visible() ||
                fatigueOwed || fatigue >= fatigueThreshold ||
                Vector3.Angle(transform.forward, towardPlayer) > duel.sightHalfAngle ||
                Vector3.Angle(duelPlayerCombat.transform.forward, towardBoss) > duel.playerAttackHalfAngle)
                return;

            duelObservedSerial = duelPlayerCombat.AttackSerial;
            duelThreatSerial = duelObservedSerial;
            duelThreatAt = Time.time + DuelReactionDelay();
            duelThreatExpires = Time.time + 0.8f;
            duelThreatPending = true;
        }

        // Called at the END of TickCombat, after leash/phase priorities.
        // true consumes this tick; movement already owned by the NavMeshAgent continues.
        private bool TickDuelDefense()
        {
            if (!duel.enabled)
            {
                duelThreatPending = false;
                if (CurrentState == State.Guard || CurrentState == State.Backstep)
                { FinishDuelDefense(); return true; }
                return false;
            }

            ObserveDuelPlayer();
            int contacts = duelImpactSignals;
            duelImpactSignals = 0;
            for (int i = 0; i < contacts; i++) duel.onGuardImpact.Invoke();

            if (duelGuardBroken)
            {
                duelGuardBroken = false;
                if (CurrentState == State.Guard && !health.IsDead)
                {
                    machine.Change(stagger);
                    duel.onGuardBroken.Invoke();
                    return true;
                }
            }

            if (!DuelNeutral) return false;
            if (TryDuelCounter()) return true;
            if (!duelThreatPending) return false;
            if (!TargetAlive || !OnMesh || duelPlayerCombat == null ||
                !duelPlayerCombat.IsAttackAnimationVisible ||
                duelPlayerCombat.AttackSerial != duelThreatSerial ||
                Time.time > duelThreatExpires || Distance > duel.noticeDistance || !Visible() ||
                fatigueOwed || fatigue >= fatigueThreshold)
            { duelThreatPending = false; return false; }

            Face(lastSeen, 180f);
            if (Time.time < duelThreatAt) return true;
            duelThreatPending = false; // One roll, not a new chance every frame.

            bool guardAvailable = duel.allowGuard && Time.time >= duelGuardReadyAt && HasDuelState(duel.guardState);
            Vector3 away = Flat(transform.position - player.position);
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();
            bool stepAvailable = duel.allowBackstep && Time.time >= duelBackstepReadyAt &&
                HasDuelState(duel.backstepState) &&
                SafeDuelDisplacement(away * duel.backstepDistance).magnitude >= 0.4f;

            float guardWeight = guardAvailable ? Mathf.Clamp01(duel.guardChance) : 0f;
            float stepWeight = stepAvailable ? Mathf.Clamp01(duel.backstepChance) : 0f;
            float scale = Mathf.Max(1f, guardWeight + stepWeight);
            float choice = Random.value;
            if (choice < guardWeight / scale)
            { machine.Change(duelGuard); return true; }
            if (choice < (guardWeight + stepWeight) / scale)
            { machine.Change(duelBackstep); return true; }
            return false;
        }

        private void PlayDuelClip(string path, float watchdog)
        {
            foreach (int t in triggers) animator.ResetTrigger(t);
            expectedHash = Animator.StringToHash(path);
            enteredHash = 0;
            stateExited = false;
            actionAge = 0f;
            actionTimeout = Mathf.Max(0.1f, watchdog);
            duelClipCompleted = false;
            animator.CrossFadeInFixedTime(expectedHash, 0.06f, 0, 0f);
        }

        private bool DuelClipFinished()
        {
            if (duelClipCompleted || stateExited) return true;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            return enteredHash == expectedHash && info.fullPathHash == expectedHash &&
                !info.loop && info.normalizedTime >= 1f;
        }

        private void EnterDuelGuard()
        {
            Stop();
            duelGuardRaised = duelGuardEverRaised = duelGuardImpactPlaying = false;
            duelGuardUntil = Time.time + Mathf.Max(0.1f, duel.guardHoldSeconds);
            duelGuardReadyAt = duelGuardUntil + Mathf.Max(0f, duel.guardCooldown);
            PlayDuelClip(duel.guardState, duel.guardRaiseWatchdog);
        }

        public void SetDuelGuard(bool active)
        {
            if (CurrentState != State.Guard) return;
            duelGuardRaised = active;
            if (active) duelGuardEverRaised = true;
        }

        private bool TickDuelGuard()
        {
            actionAge += Time.deltaTime * Mathf.Max(0f, animator.speed);
            Face(lastSeen, 120f);
            TickDuelPush();
            if (!duelGuardEverRaised && actionAge >= duel.guardRaiseWatchdog)
            {
                Debug.LogWarning("Boss guard never raised. Check BossActionState and AE_GuardOn on the active clip.", this);
                FinishDuelDefense(); return true;
            }

            if (Time.time >= duelGuardUntil)
            { FinishDuelDefense(); return true; }

            if (duelGuardImpactPlaying && (DuelClipFinished() || actionAge >= actionTimeout))
            {
                duelGuardImpactPlaying = false;
                PlayDuelClip(duel.guardState, duel.guardRaiseWatchdog);
            }
            else if (!duelGuardImpactPlaying && (stateExited || duelClipCompleted))
            { FinishDuelDefense(); return true; }

            if (duelGuardImpactQueued)
            {
                duelGuardImpactQueued = false;
                if (!duelGuardImpactPlaying && HasDuelState(duel.blockHitState))
                {
                    duelGuardImpactPlaying = true;
                    PlayDuelClip(duel.blockHitState, duel.blockHitWatchdog);
                }
            }

            if (!duelGuardImpactPlaying && !DuelHasPush && TryDuelCounter()) return true;
            return true;
        }

        // Health calls this before HP changes. Never change the FSM inside this callback.
        public bool FilterIncoming(ref DamageInfo info)
        {
            if (!configured || !isActiveAndEnabled || !duel.enabled ||
                CurrentState != State.Guard || !duelGuardRaised ||
                !duelGuardEverRaised || duelGuardBroken || stateExited ||
                expectedHash == 0 || enteredHash != expectedHash)
                return true;

            if (!info.HasImpactOrigin && info.Attacker == null) return true;
            Vector3 source = info.HasImpactOrigin ? info.ImpactOrigin : info.Attacker.transform.position;
            Vector3 toward = Flat(source - transform.position);
            if (toward.sqrMagnitude < 0.001f ||
                Vector3.Angle(transform.forward, toward) > duel.blockHalfAngle) return true;

            float strength = info.Reaction == HitReactionType.Heavy
                ? Mathf.Max(1f, duel.heavyGuardMultiplier) : 1f;
            poiseDamagedAt = Time.time;
            poise -= Mathf.Max(1f, info.Amount * Mathf.Max(0f, duel.blockedPoiseMultiplier) * strength);
            fatigue += Mathf.Max(0f, duel.fatiguePerBlock);
            fatigueOwed |= fatigue >= fatigueThreshold;
            duelGuardBroken = poise <= 0f;
            if (duelGuardBroken) duelGuardRaised = false;
            duelImpactSignals++;
            duelGuardImpactQueued = true;
            duelPushDirection = -toward.normalized;
            duelPushDistance = Mathf.Max(0f, duel.guardPushDistance) * strength;
            duelPushDuration = Mathf.Max(0.05f, duel.guardPushSeconds);
            duelPushElapsed = 0f;
            QueueDuelOpening();

            info.WasBlocked = true;
            info.Amount *= Mathf.Clamp01(duel.chipFraction);
            // Guard presentation and poise above also work when chipFraction is zero.
            return info.Amount > 0f;
        }

        private void TickDuelPush()
        {
            if (!DuelHasPush) return;
            float before = duelPushElapsed / duelPushDuration;
            duelPushElapsed = Mathf.Min(duelPushDuration,
                duelPushElapsed + Time.deltaTime * Mathf.Max(0f, animator.speed));
            float after = duelPushElapsed / duelPushDuration;
            float progress = (1f - before) * (1f - before) - (1f - after) * (1f - after);
            MoveDuelDisplacement(duelPushDirection * duelPushDistance * progress);
        }

        private void EnterDuelBackstep()
        {
            Stop();
            duelBackstepDirection = Flat(transform.position - player.position);
            if (duelBackstepDirection.sqrMagnitude < 0.001f) duelBackstepDirection = -transform.forward;
            duelBackstepDirection.Normalize();
            duelBackstepRemaining = Mathf.Max(0f, duel.backstepDistance);
            duelMoveAge = 0f;
            duelBackstepMoving = duelBackstepStarted = false;
            duelBackstepReadyAt = Time.time + Mathf.Max(0f, duel.backstepCooldown);
            fatigue += Mathf.Max(0f, duel.backstepFatigue);
            fatigueOwed |= fatigue >= fatigueThreshold;
            PlayDuelClip(duel.backstepState, duel.backstepWatchdog);
            duel.onBackstep.Invoke();
        }

        public void SetDuelBackstepMovement(bool active)
        {
            if (CurrentState != State.Backstep) return;
            duelBackstepMoving = active;
            if (active) duelBackstepStarted = true;
        }

        private bool TickDuelBackstep()
        {
            float dt = Time.deltaTime * Mathf.Max(0f, animator.speed);
            actionAge += dt;
            Face(lastSeen, 100f);
            if (duelBackstepMoving && enteredHash == expectedHash && !stateExited)
            {
                float duration = Mathf.Max(0.05f, duel.backstepMoveSeconds);
                float used = Mathf.Min(dt, Mathf.Max(0f, duration - duelMoveAge));
                float requested = Mathf.Min(duelBackstepRemaining, duel.backstepDistance * used / duration);
                float moved = MoveDuelDisplacement(duelBackstepDirection * requested);
                duelMoveAge += used;
                duelBackstepRemaining = Mathf.Max(0f, duelBackstepRemaining - moved);
                if (moved + 0.001f < requested || duelMoveAge >= duration)
                    duelBackstepMoving = false;
            }
            if (DuelClipFinished() || actionAge >= actionTimeout)
            {
                if (!duelBackstepStarted)
                    Debug.LogWarning("Backstep had no movement event. Add AE_BackstepMoveOn/Off to its active clip.", this);
                FinishDuelDefense();
            }
            return true;
        }

        private Vector3 SafeDuelDisplacement(Vector3 delta)
        {
            delta = Flat(delta);
            if (!OnMesh || delta.sqrMagnitude < 0.000001f) return Vector3.zero;
            Vector3 start = transform.position;
            Vector3 desired = start + delta;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (NavMesh.Raycast(start, desired, out NavMeshHit edge, filter)) desired = edge.position;
            Vector3 horizontal = Flat(desired - start);
            float distance = horizontal.magnitude;
            if (distance < 0.0001f) return Vector3.zero;
            float radius = Mathf.Max(0.05f, agent.radius * 0.9f);
            float height = Mathf.Max(agent.height, radius * 2f);
            Vector3 bottom = start + Vector3.up * (agent.baseOffset + radius + 0.06f);
            Vector3 top = start + Vector3.up * (agent.baseOffset + height - radius + 0.06f);
            if (Physics.CapsuleCast(bottom, top, radius, horizontal / distance,
                out RaycastHit wall, distance, environment, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Max(0f, wall.distance - 0.03f);
                desired = start + horizontal.normalized * distance;
            }
            if (!NavMesh.SamplePosition(desired, out NavMeshHit ground, 0.2f, filter) ||
                Mathf.Abs(ground.position.y - start.y) > 0.25f ||
                Flat(ground.position - start).magnitude > delta.magnitude + 0.02f ||
                NavMesh.Raycast(start, ground.position, out edge, filter)) return Vector3.zero;
            return ground.position - start;
        }

        private float MoveDuelDisplacement(Vector3 delta)
        {
            Vector3 safe = SafeDuelDisplacement(delta);
            if (safe.sqrMagnitude < 0.000001f) return 0f;
            agent.updatePosition = false;
            transform.position += safe;
            agent.nextPosition = transform.position;
            return Flat(safe).magnitude;
        }

        private void QueueDuelOpening()
        {
            // Do not refresh the same opening indefinitely while it is already pending.
            if (Time.time <= duelOpeningExpires) return;
            duelOpeningAt = Time.time + DuelReactionDelay();
            duelOpeningExpires = Time.time + Mathf.Max(0.1f, duel.openingLifetime);
        }

        private bool TryDuelCounter()
        {
            if (duelPlayerCombat == null || !TargetAlive || !Visible() ||
                fatigueOwed || fatigue >= fatigueThreshold || Time.time < duelCounterReadyAt)
                return false;
            bool whiff = Time.time >= whiffReactAt && Time.time - whiffObservedAt < 1.5f &&
                whiffObservedAt != duelSeenWhiffAt;
            bool opening = Time.time >= duelOpeningAt && Time.time <= duelOpeningExpires;
            if ((!whiff && !opening) || duelPlayerCombat.IsAttackAnimationVisible ||
                duelPlayerCombat.Kind == PlayerActionKind.Dodge) return false;

            // Consume this opportunity even if chance or range rejects the counter.
            // The miss notification may still be waiting for its reaction delay.
            // Mark that same miss as considered when handling its recovery opening.
            if (whiff || Time.time - whiffObservedAt < 1.5f)
                duelSeenWhiffAt = whiffObservedAt;
            duelOpeningExpires = float.NegativeInfinity;
            if (Random.value >= Mathf.Clamp01(duel.counterChance)) return false;
            CaptainAttack[] options = duel.counterAttacks != null && duel.counterAttacks.Length > 0
                ? duel.counterAttacks : attacks;
            CaptainAttack chosen = null;
            double total = 0;
            foreach (var a in options)
            {
                if (a == null || a.jumpSlam || a.isProjectileAttack ||
                    Array.IndexOf(attacks, a) < 0 || !Eligible(a)) continue;
                float weight = phaseIndex == 0 ? a.phase1Weight : a.phase2Weight;
                if (a == lastAttack) weight *= CurrentPhase.repeatAttackWeight;
                if (weight <= 0f) continue;
                total += weight;
                if (Random.value * total < weight) chosen = a;
            }
            if (chosen == null) return false;
            whiffObservedAt = float.NegativeInfinity; // Cannot taunt AND counter this miss.
            duelCounterReadyAt = Time.time + Mathf.Max(0f, duel.counterCooldown);
            StartAttack(chosen, false);
            duel.onCounter.Invoke();
            return true;
        }

        private bool CompleteDuelAnimation(bool authored)
        {
            if (CurrentState != State.Guard && CurrentState != State.Backstep) return false;
            duelClipCompleted = true;
            duelBackstepMoving = false;
            return true;
        }

        private void FinishDuelDefense()
        {
            recoveryUntil = Time.time + Mathf.Max(0f, duel.defenseRecovery);
            machine.Change(recover);
        }
        // [SerializeField] private bool debugDuel = true;
        // private float duelDebugAt;

        // private void DebugDuelStatus()
        // {
        //     if (!debugDuel || Time.time < duelDebugAt) return;
        //     duelDebugAt = Time.time + 0.05f;

        //     PlayerCombat pc = player == null ? null :
        //         player.GetComponentInParent<PlayerCombat>();

        //     if (pc == null && player != null)
        //         pc = player.GetComponentInChildren<PlayerCombat>();

        //     Vector3 towardPlayer = player == null ? Vector3.zero :
        //         Flat(player.position - transform.position);

        //     float bossAngle = Vector3.Angle(
        //         transform.forward, towardPlayer);

        //     float playerAngle = pc == null ? 180f :
        //         Vector3.Angle(pc.transform.forward, -towardPlayer);

        //     float room = OnMesh && player != null ?
        //         SafeDuelDisplacement(
        //             -towardPlayer.normalized * duel.backstepDistance
        //         ).magnitude : 0f;

        //     Debug.Log(
        //         $"DUEL | Configured={configured} Enabled={duel.enabled} State={CurrentState} " +
        //         $"Neutral={DuelNeutral} OnMesh={OnMesh} Alive={TargetAlive} " +
        //         $"Distance={Distance:F2}/{duel.noticeDistance:F2} ClearSight={Visible()} " +
        //         $"BossAngle={bossAngle:F0}/{duel.sightHalfAngle:F0} " +
        //         $"PlayerAngle={playerAngle:F0}/{duel.playerAttackHalfAngle:F0} " +
        //         $"PlayerFound={pc != null} Ready={pc != null && pc.Ready} " +
        //         $"PlayerAction={(pc == null ? "MISSING" : pc.Kind.ToString())} " +
        //         $"AttackVisible={pc != null && pc.IsAttackAnimationVisible} " +
        //         $"Serial={(pc == null ? 0u : pc.AttackSerial)} Seen={duelObservedSerial} " +
        //         $"Pending={duelThreatPending} Fatigue={fatigue:F1}/{fatigueThreshold:F1} Owed={fatigueOwed} " +
        //         $"GuardAllowed={duel.allowGuard} GuardStateFound={HasDuelState(duel.guardState)} " +
        //         $"GuardReady={Time.time >= duelGuardReadyAt} " +
        //         $"StepAllowed={duel.allowBackstep} StepStateFound={HasDuelState(duel.backstepState)} " +
        //         $"StepReady={Time.time >= duelBackstepReadyAt} StepRoom={room:F2}",
        //         this);
        // }

        private void CancelDuelAction()
        {
            duelGuardRaised = duelGuardEverRaised = duelGuardImpactPlaying = false;
            duelGuardImpactQueued = duelGuardBroken = duelBackstepMoving = false;
            duelClipCompleted = false;
            duelThreatPending = false;
            duelPushElapsed = duelPushDuration = 0f;
            duelBackstepRemaining = 0f;
        }
    }
}

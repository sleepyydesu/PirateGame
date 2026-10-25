using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using PirateGame.Combat;
using Random = UnityEngine.Random;

namespace PirateGame.Enemies
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public sealed partial class BossAI : MonoBehaviour, IIncomingDamageFilter
    {
        public enum State
        {
            Idle,
            Chase,
            Circle,
            Attack,
            Recovery,
            Taunt,
            Fatigue,
            PhaseTransition,
            Stagger,
            Dead,
            ReturnHome,
            Guard,
            Backstep
        }

        [Serializable]
        public sealed class Phase
        {
            public string name = "The Gentleman";
            [Range(0f, 1f)] public float healthThreshold = 1f;
            [Min(0.1f)] public float speedMultiplier = 1f;
            [Header("Attack pace")]
            [Range(0.5f, 1.5f)]
            public float attackAnimationSpeed = 1f;

            [Range(0.3f, 2f)]
            public float decisionDelayMultiplier = 1f;

            [Range(0.3f, 2f)]
            public float recoveryMultiplier = 1f;

            [Range(0.3f, 2f)]
            public float attackCooldownMultiplier = 1f;

            [Header("Attack variety")]
            [Range(0f, 1f)]
            public float repeatAttackWeight = 0.2f;
            [Min(0.1f)] public float minimumRecovery = 0.6f;
            [Range(0, 3)] public int maxExtensions = 1;
        }
        [Serializable]
        public sealed class ActionAnimation
        {
            public string trigger;
            public string fullStateName;
            [Min(0.2f)] public float watchdog = 4f;
        }
        [Header("References (assign target explicitly)")]
        [SerializeField] private Transform player;
        [SerializeField] private Animator animator;
        [SerializeField] private BossWeaponSweep weapon;
        [SerializeField] private BossMeteorCaster caster;
        [SerializeField] private BossGroundBurst groundBurst;
        [Header("Phase-change roar burst")]
        [SerializeField] private CaptainAttack roarBurstSettings;

        private bool roarBurstUsed;
        private bool comboGroundBurstUsed;
        private bool suppressSwordForGroundBurst;
        [SerializeField] private CaptainAttack[] attacks;
        private bool jumpActive, jumpStarted, jumpLanded;
        private Vector3 jumpStart, jumpEnd;
        private float jumpStartNormalized;
        private bool attackStepOpen;
        private float attackStepRemaining;
        [SerializeField]
        private Phase[] phases = {
            new Phase(), new Phase { name = "No More Courtesy", healthThreshold = 0.5f,
                speedMultiplier = 1.12f, minimumRecovery = 0.65f, maxExtensions = 1 }
        };
        [Header("Animation contracts; full paths must match your controller")]
        [SerializeField] private string locomotionState = "Base Layer.Locomotion";
        [SerializeField]
        private ActionAnimation roar = new ActionAnimation
        {
            trigger = "Roar",
            fullStateName = "Base Layer.roar",
            watchdog = 4.5f
        };
        [SerializeField]
        private ActionAnimation staggerAnimation = new ActionAnimation
        {
            trigger = "Hit",
            fullStateName = "Base Layer.takedamage",
            watchdog = 2.5f
        };
        [SerializeField] private ActionAnimation tauntAnimation = new ActionAnimation();
        [SerializeField] private string deathTrigger = "Die";
        [Header("Perception / footwork")]
        [SerializeField] private LayerMask environment;
        [SerializeField, Min(1f)] private float detectionRadius = 18f;
        [SerializeField, Min(1f)] private float leashRadius = 25f;
        [SerializeField, Min(0f)] private float recognitionTime = 0.35f;
        [SerializeField, Min(0.1f)] private float walkSpeed = 2.2f;
        [SerializeField, Min(0.1f)] private float chaseSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float preferredDistance = 2.2f;
        [SerializeField, Min(0.1f)] private float prepareAttackDistance = 1f;
        [Header("Return home")]
        [SerializeField, Min(0.1f)] private float returnHomeSpeed = 3.5f;
        [SerializeField, Min(0.2f)] private float homeArrivalDistance = 0.6f;
        [SerializeField, Min(0f)] private float homeWaitSeconds = 1f;
        [Header("Projectile release point")]
        [SerializeField] private Transform projectileSpawnPoint;
        [SerializeField] private Transform airSlashBladeBase;
        [SerializeField] private Transform airSlashBladeTip;

        private struct PendingAirSlash
        {
            public int stateHash;
            public Vector3 aim;
        }

        private readonly Queue<PendingAirSlash> pendingAirSlashes =
            new Queue<PendingAirSlash>();
        private float nextRangedDecisionAt;

        private BossStateMachine.Node returnHome;
        private bool returningHome;
        private float homeWaitUntil = -1f;
        [SerializeField, Min(0.1f)]
        private float returnReengageDistance = 6f;

        [SerializeField, Min(0.1f)]
        private float returnChaseDistance = 12f;

        private bool fightingAfterReturn;
        private float resumedChaseDistance;
        [Header("Personality: arrogance creates openings, fatigue limits pressure")]
        [SerializeField, Range(0f, 1f)] private float arrogance = 0.7f;
        [Header("Missed attack reaction")]
        [SerializeField, Min(0.1f)]
        private float noticeMissDistance = 6f;

        [SerializeField]
        private bool testTauntOnMiss;
        [SerializeField, Min(0.1f)] private float fatigueThreshold = 75f;
        [SerializeField, Min(0.1f)] private float fatigueRestSeconds = 1.8f;
        [SerializeField, Min(0f)] private float fatigueDecay = 12f;
        [SerializeField, Min(0f)] private float recoveryFreezeSeconds = 0.3f;
        [SerializeField] private Vector2 reactionDelay = new Vector2(0.3f, 0.5f);
        [Header("Poise: separate tuning axis from health damage")]
        [SerializeField, Min(1f)] private float maxPoise = 60f;
        [SerializeField, Min(0f)] private float poiseDamageMultiplier = 1f;
        [SerializeField, Min(0f)] private float poiseRegen = 12f;
        [SerializeField, Min(0f)] private float poiseRegenDelay = 2f;
        [Header("Presentation hooks (camera, audio, subtitles, VFX)")]
        public UnityEvent onPhaseTransitionStarted = new UnityEvent();
        public UnityEvent onPhaseTransitionFinished = new UnityEvent();
        public UnityEvent onTaunt = new UnityEvent();
        public UnityEvent onCommittedAttack = new UnityEvent();
        public UnityEvent onProjectileReleased = new UnityEvent();
        public UnityEvent onDeath = new UnityEvent();

        public State CurrentState { get; private set; }
        public bool IsInParryWindow => CurrentState == State.Attack && parryWindow;
        public int CurrentPhaseIndex => phaseIndex;
        public float Fatigue => fatigue;
        private Health health, playerHealth;
        private NavMeshAgent agent;
        private BossStateMachine machine;
        private BossStateMachine.Node root, combat, idle, approach, circle, attack,
            recover, taunt, rest, transition, stagger, dead;
        private readonly Dictionary<CaptainAttack, float> readyAt = new Dictionary<CaptainAttack, float>();
        private readonly List<CaptainAttack> candidates = new List<CaptainAttack>();
        private readonly List<float> scores = new List<float>();
        private readonly HashSet<int> triggers = new HashSet<int>();
        private CaptainAttack currentAttack, lastAttack, queuedChain;
        private Vector3 home, lastSeen;
        private int phaseIndex, requestedPhase, extensions, expectedHash, enteredHash;
        private float enteredAt, actionAge, actionTimeout, decisionAt, pathAt, seenAt,
            recognizedFor, poise, poiseDamagedAt, fatigue, recoveryUntil, tauntReadyAt,
            healObservedAt = float.NegativeInfinity, healReactAt, whiffObservedAt = float.NegativeInfinity,
            whiffReactAt, circleSign = 1f;
        private bool committed, parryWindow, chainDecided, stateExited, speedParameter, configured, fatigueOwed;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackPlaybackHash = Animator.StringToHash("AttackPlayback");

        private float AttackPlayback =>
            Mathf.Clamp(CurrentPhase.attackAnimationSpeed, 0.5f, 1.5f);
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private bool directionParameters;
        private Phase CurrentPhase => phases[phaseIndex];
        private bool OnMesh => agent != null && agent.enabled && agent.isOnNavMesh;
        private bool TargetAlive => player != null && (playerHealth == null || !playerHealth.IsDead);
        private float Distance => player == null ? float.PositiveInfinity :
            Vector3.Distance(transform.position, player.position);

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); health = GetComponent<Health>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (caster == null) caster = GetComponent<BossMeteorCaster>();
            home = transform.position; poise = maxPoise;
            agent.updateRotation = false; // One owner of rotation; never NavMesh + code simultaneously.
            CacheAnimatorParameters();
            BuildHierarchy();
            BuildDuelStates();
        }
        private void OnEnable()
        {
            health.OnDamaged += HandleDamaged;
            health.OnDeath += HandleDeath;
            health.InstallDamageFilter(this);
        }
        private void OnDisable()
        {
            health.RemoveDamageFilter(this);
            health.OnDamaged -= HandleDamaged; health.OnDeath -= HandleDeath;
            CancelAction(); Stop();
            // Pool reset/revival is deliberately an encounter-level responsibility.
        }

        private void CacheAnimatorParameters()
        {
            triggers.Clear();
            speedParameter = false;
            if (animator == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger) triggers.Add(p.nameHash);
                if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float) speedParameter = true;
                if ((p.nameHash == MoveXHash || p.nameHash == MoveZHash) &&
                    p.type == AnimatorControllerParameterType.Float) directionParameters = true;
            }
        }

        private void Start()
        {
            CacheAnimatorParameters();
            if (player == null)
            {
                GameObject found = GameObject.FindGameObjectWithTag("Player");
                if (found != null) player = found.transform;
            }
            configured = ValidateConfiguration();
            if (!configured) { enabled = false; return; }
            SetTarget(player);
            ValidateDuelSetup();
            machine.Change(idle);
            if (health.IsDead) HandleDeath();
        }
        public void SetTarget(Transform target)
        {
            player = target; playerHealth = target == null ? null : target.GetComponentInParent<Health>();
            healObservedAt = whiffObservedAt = float.NegativeInfinity;
        }
        private bool ValidateConfiguration()
        {
            bool ok = animator != null && animator.runtimeAnimatorController != null &&
                animator.GetComponent<BossAnimationRelay>() != null && weapon != null && attacks != null && attacks.Length > 0 &&
                phases != null && phases.Length >= 2 && OnMesh;
            if (!ok) { Debug.LogError("Boss: assign Animator with controller/relay, weapon, attacks, two phases and bake NavMesh.", this); return false; }
            if (player == null) Debug.LogWarning("Boss has no target. Call SetTarget before the encounter.", this);
            ok &= Check(animator.HasState(0, Animator.StringToHash(locomotionState)),
    $"Locomotion State '{locomotionState}' not found in the Animator.");
            ok &= CheckAction("Roar", roar);
            ok &= CheckAction("Stagger Animation", staggerAnimation);
            if (!string.IsNullOrEmpty(tauntAnimation.trigger)) ok &= CheckAction("Taunt Animation", tauntAnimation);

            var stateNames = new HashSet<string>();
            for (int i = 0; i < attacks.Length; i++)
            {
                CaptainAttack a = attacks[i];
                if (!Check(a != null, $"Attacks slot {i} is empty.")) { ok = false; continue; }
                ok &= Check(triggers.Contains(Animator.StringToHash(a.trigger)),
                    $"{a.name}: trigger '{a.trigger}' is not an Animator trigger parameter.");
                ok &= Check(animator.HasState(0, Animator.StringToHash(a.fullStateName)),
                    $"{a.name}: state '{a.fullStateName}' not found.");
                ok &= Check(a.maxRange > a.minRange, $"{a.name}: Max Range must be bigger than Min Range.");
                ok &= Check(stateNames.Add(a.fullStateName),
                    $"{a.name}: another attack already uses state '{a.fullStateName}'.");
                if (a.followup != null)
                    ok &= Check(Array.IndexOf(attacks, a.followup) >= 0 && a.followup != a,
                        $"{a.name}: Followup must be in the Attacks list and not itself.");
            }

            for (int i = 0; i < phases.Length; i++)
            {
                ok &= Check(phases[i] != null, $"Phase {i} is empty.");
                if (i > 0 && phases[i] != null && phases[i - 1] != null)
                    ok &= Check(phases[i].healthThreshold < phases[i - 1].healthThreshold,
                        $"Phase {i} threshold must be lower than phase {i - 1}.");
            }
            return ok;
        }
        private bool ValidateAction(ActionAnimation a)
        {
            return a != null && !string.IsNullOrEmpty(a.trigger) &&
                triggers.Contains(Animator.StringToHash(a.trigger)) &&
                animator.HasState(0, Animator.StringToHash(a.fullStateName));
        }
        private void BuildHierarchy()
        {
            machine = new BossStateMachine(); root = new BossStateMachine.Node("Alive");
            combat = new BossStateMachine.Node("Combat", root) { Tick = TickCombat };
            idle = Leaf("Idle", root, State.Idle, Stop, TickIdle);
            returnHome = Leaf(
                "ReturnHome",
                root,
                State.ReturnHome,
                EnterReturnHome,
                TickReturnHome
            );
            approach = Leaf("Approach", combat, State.Chase, null, TickApproach);
            circle = Leaf("Circle", combat, State.Circle, () =>
            {
                circleSign = Random.value < 0.5f ? -1f : 1f;

                decisionAt = Time.time +
                    Random.Range(0.4f, 0.7f) *
                    CurrentPhase.decisionDelayMultiplier;
            }, TickCircle);
            attack = Leaf("Attack", combat, State.Attack, null, TickAttack);
            recover = Leaf("Recovery", combat, State.Recovery, () => { Stop(); ForceLocomotion(); }, () =>
            {
                if (player != null && !fatigueOwed && Time.time - enteredAt >= recoveryFreezeSeconds)
                    CircleMovement();
                if (Time.time >= recoveryUntil) machine.Change(fatigueOwed ? rest : circle);
                return true;
            });
            rest = Leaf("Fatigue", combat, State.Fatigue, () =>
            {
                Stop(); ForceLocomotion(); recoveryUntil = Time.time + fatigueRestSeconds;
            }, () => { if (Time.time >= recoveryUntil) { fatigue = 0f; fatigueOwed = false; machine.Change(circle); } return true; });
            taunt = Leaf("Taunt", combat, State.Taunt, () =>
            {
                roarBurstUsed = false;
                Stop(); tauntReadyAt = Time.time + 12f;
                BeginAnimation(tauntAnimation.trigger, tauntAnimation.fullStateName, tauntAnimation.watchdog);
                onTaunt.Invoke();
            }, TickAction);
            transition = Leaf("PhaseTransition", root, State.PhaseTransition, () =>
            {
                Stop(); BeginAnimation(roar.trigger, roar.fullStateName, roar.watchdog);
                onPhaseTransitionStarted.Invoke();
            }, TickAction);
            stagger = Leaf("Stagger", root, State.Stagger, () =>
            {
                Stop(); BeginAnimation(staggerAnimation.trigger, staggerAnimation.fullStateName, staggerAnimation.watchdog);
            }, TickAction);
            // Dead is outside Alive: all future perception, reactions and poise work stops.
            dead = Leaf("Dead", null, State.Dead, () =>
            {
                Stop(); if (agent.enabled) agent.enabled = false;
                Trigger(deathTrigger); onDeath.Invoke();
            }, null);
        }
        private BossStateMachine.Node Leaf(string name, BossStateMachine.Node parent, State state,
            Action enter, Func<bool> tick)
        {
            return new BossStateMachine.Node(name, parent)
            {
                Enter = () => { CurrentState = state; enteredAt = Time.time; enter?.Invoke(); },
                Exit = CancelAction,
                Tick = tick
            };
        }
        private void Update()
        {
            // DebugDuelStatus();

            if (!configured || CurrentState == State.Dead) return;
            if (health.IsDead) { HandleDeath(); return; } // Death has priority over cinematic/stagger.
            if (TargetAlive && Visible()) { lastSeen = player.position; seenAt = Time.time; }
            if (CurrentState != State.Attack) fatigue = Mathf.Max(0f, fatigue - fatigueDecay * Time.deltaTime);
            if (Time.time - poiseDamagedAt >= poiseRegenDelay && CurrentState != State.Stagger)
                poise = Mathf.Min(maxPoise, poise + poiseRegen * Time.deltaTime);
            machine.Tick();
            if (animator != null && (speedParameter || directionParameters))
            {
                Vector3 localVelocity = OnMesh ? transform.InverseTransformDirection(agent.velocity) : Vector3.zero;
                if (speedParameter)
                    animator.SetFloat(SpeedHash, localVelocity.magnitude / chaseSpeed, 0.1f, Time.deltaTime);
                if (directionParameters)
                {
                    animator.SetFloat(MoveXHash, localVelocity.x / chaseSpeed, 0.1f, Time.deltaTime);
                    animator.SetFloat(MoveZHash, localVelocity.z / chaseSpeed, 0.1f, Time.deltaTime);
                }
            }
        }

        private bool Check(bool condition, string message)
        {
            if (!condition) Debug.LogError("Boss setup problem: " + message, this);
            return condition;
        }

        private bool CheckAction(string label, ActionAnimation a)
        {
            if (a == null || string.IsNullOrEmpty(a.trigger))
                return Check(false, $"{label}: trigger is empty.");
            bool hasTrigger = Check(triggers.Contains(Animator.StringToHash(a.trigger)),
                $"{label}: trigger '{a.trigger}' is not an Animator trigger parameter.");
            bool hasState = Check(animator.HasState(0, Animator.StringToHash(a.fullStateName ?? "")),
                $"{label}: state '{a.fullStateName}' not found.");
            return hasTrigger && hasState;
        }

        private bool TickIdle()
        {
            Stop();

            // Only notice players inside the boss's allowed home area.
            bool canSeePlayer =
                TargetAlive &&
                Distance <= detectionRadius &&
                Vector3.Distance(player.position, home) <= leashRadius &&
                Visible();

            if (canSeePlayer)
                recognizedFor += Time.deltaTime;
            else
                recognizedFor = 0f;

            if (canSeePlayer && recognizedFor >= recognitionTime)
            {
                lastSeen = player.position;
                seenAt = Time.time;

                machine.Change(approach);
            }

            return true;
        }

        private bool TickCombat()
        {
            bool shouldReturn =
                !TargetAlive ||
                Time.time - seenAt > 3f;

            if (!shouldReturn)
            {
                if (fightingAfterReturn)
                {
                    // After turning back to fight, use distance
                    // from the player instead of distance from home.
                    float minimumLimit = Mathf.Max(
                        returnChaseDistance,
                        returnReengageDistance + 1f
                    );

                    // If a distant hit started the chase, slowly
                    // reduce that extra allowance as the boss closes in.
                    resumedChaseDistance = Mathf.Min(
                        resumedChaseDistance,
                        Mathf.Max(minimumLimit, Distance + 2f)
                    );

                    shouldReturn = Distance > resumedChaseDistance;
                }
                else
                {
                    // Normal combat still uses the original home limit.
                    shouldReturn =
                        Vector3.Distance(transform.position, home) > leashRadius ||
                        Vector3.Distance(player.position, home) > leashRadius;
                }
            }

            // Finish the current attack before leaving.
            if (CurrentState != State.Attack && shouldReturn)
            {
                machine.Change(returnHome);
                return false;
            }

            // Keep the existing phase-change rules.
            bool safe =
                CurrentState == State.Chase ||
                CurrentState == State.Circle ||
                CurrentState == State.Guard ||
                CurrentState == State.Backstep ||
                ((CurrentState == State.Recovery ||
                  CurrentState == State.Fatigue) &&
                 Time.time >= recoveryUntil);

            if (requestedPhase > phaseIndex && safe)
            {
                machine.Change(transition);
                return false;
            }

            return !TickDuelDefense();
        }

        private void EnterReturnHome()
        {
            fightingAfterReturn = false;
            returningHome = true;

            recognizedFor = 0f;
            homeWaitUntil = -1f;
            pathAt = 0f;

            // Forget old healing/whiff observations.
            healObservedAt = whiffObservedAt = float.NegativeInfinity;

            Stop();

            // Use the existing idle/walk/run animation.
            ForceLocomotion();
        }

        private bool TryResumeCombat(bool hitByPlayer)
        {
            if (!returningHome || !TargetAlive || !OnMesh)
                return false;

            // Coming near requires the boss to see you.
            // A real hit already tells it who attacked.
            if (!hitByPlayer &&
                (Distance > returnReengageDistance || !Visible()))
            {
                return false;
            }

            float chaseLimit = Mathf.Max(
                returnChaseDistance,
                returnReengageDistance + 1f
            );

            // A distant hit must not immediately trigger another retreat.
            resumedChaseDistance = hitByPlayer
                ? Mathf.Max(chaseLimit, Distance + 2f)
                : chaseLimit;

            returningHome = false;
            fightingAfterReturn = true;

            homeWaitUntil = -1f;
            recognizedFor = 0f;

            lastSeen = player.position;
            seenAt = Time.time;
            pathAt = 0f;

            // Let an existing stagger finish first.
            if (CurrentState != State.Stagger)
                machine.Change(approach);

            return true;
        }

        private bool TickReturnHome()
        {
            if (TryResumeCombat(false))
                return true;

            if (!OnMesh)
                return true;

            bool stillWalking =
                Vector3.Distance(transform.position, home) > homeArrivalDistance ||
                agent.pathPending ||
                (agent.hasPath &&
                agent.remainingDistance > homeArrivalDistance);

            if (stillWalking)
            {
                homeWaitUntil = -1f;

                Move(home, returnHomeSpeed);

                // Face along the route around obstacles.
                Vector3 facingPoint =
                    agent.hasPath && !agent.pathPending
                        ? agent.steeringTarget
                        : home;

                Face(facingPoint, 180f);

                return true;
            }

            // Home reached. Stop and wait briefly.
            Stop();

            if (homeWaitUntil < 0f)
                homeWaitUntil = Time.time + homeWaitSeconds;

            if (Time.time >= homeWaitUntil)
            {
                returningHome = false;
                recognizedFor = 0f;

                machine.Change(idle);
            }

            return true;
        }

        [ContextMenu("Debug Air Slash Selection")]
        private void DebugAirSlashSelection()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("Run this during Play mode.", this);
                return;
            }

            Debug.Log(
                $"AIR SLASH CHECK | State: {CurrentState} | " +
                $"Distance: {Distance:F1} | " +
                $"Visible: {Visible()} | " +
                $"Target alive: {TargetAlive} | " +
                $"Fatigue: {fatigue:F1}/{fatigueThreshold:F1}",
                this);

            foreach (CaptainAttack a in attacks)
            {
                if (a == null)
                    continue;

                float facing = player != null
                    ? Vector3.Angle(
                        transform.forward,
                        Flat(player.position - transform.position))
                    : -1f;

                float cooldownLeft = readyAt.TryGetValue(a, out float readyTime)
                    ? Mathf.Max(0f, readyTime - Time.time)
                    : 0f;

                Debug.Log(
                    $"Attack: {a.name}\n" +
                    $"Projectile enabled: {a.isProjectileAttack}\n" +
                    $"Prefab assigned: {a.projectilePrefab != null}\n" +
                    $"Range: {a.minRange} to {a.maxRange}\n" +
                    $"Facing: {facing:F1}, allowed: {a.maxFacingAngle}\n" +
                    $"Phase weights: {a.phase1Weight}, {a.phase2Weight}\n" +
                    $"Cooldown remaining: {cooldownLeft:F1}\n" +
                    $"Eligible: {Eligible(a)}",
                    this);
            }
        }

        private bool TickApproach()
        {
            Face(lastSeen, 240f);

            if (Time.time >= nextRangedDecisionAt &&
                fatigue < fatigueThreshold &&
                Visible())
            {
                nextRangedDecisionAt = Time.time +
                    0.35f * CurrentPhase.decisionDelayMultiplier;

                CaptainAttack ranged =
                    SelectAttack(projectileOnly: true);

                if (ranged != null)
                {
                    StartAttack(ranged, false);
                    return true;
                }
            }

            Move(lastSeen, chaseSpeed * CurrentPhase.speedMultiplier);

            if (Distance <= preferredDistance + prepareAttackDistance && Visible())
            {
                // Keep the boss's required fatigue break.
                if (fatigueOwed || fatigue >= fatigueThreshold)
                {
                    machine.Change(rest);
                    return true;
                }

                // Try an available attack immediately on arrival.
                CaptainAttack arrivalAttack = SelectAttack();

                if (arrivalAttack != null)
                {
                    StartAttack(arrivalAttack, false);
                    return true;
                }

                // If no attack is available yet, circle and check next frame.
                machine.Change(circle);
                decisionAt = Time.time;

                return true;
            }

            return true;
        }
        private bool TickCircle()
        {
            if (player == null) return true;
            CircleMovement();
            if (Time.time < decisionAt) return true;
            decisionAt = Time.time +
                Random.Range(0.3f, 0.5f) *
                CurrentPhase.decisionDelayMultiplier;
            if (fatigue >= fatigueThreshold) { machine.Change(rest); return true; }
            bool observedWhiff =
            Time.time >= whiffReactAt &&
            Time.time - whiffObservedAt < 1.5f;

            bool canConsiderTaunt =
                observedWhiff &&
                Time.time >= tauntReadyAt &&
                Distance <= noticeMissDistance &&
                Visible() &&
                ValidateAction(tauntAnimation);

            if (canConsiderTaunt)
            {
                // Give this miss one chance to cause a taunt.
                whiffObservedAt = float.NegativeInfinity;

                float chance = arrogance *
                    (phaseIndex == 0 ? 0.6f : 0.15f);

                if (testTauntOnMiss || Random.value < chance)
                {
                    machine.Change(taunt);
                    return true;
                }
            }
            CaptainAttack selected = SelectAttack();
            if (selected != null) StartAttack(selected, false);
            else if (Distance > preferredDistance + 0.8f) machine.Change(approach);
            return true;
        }

        private void CircleMovement()
        {
            Face(lastSeen, 150f);
            Vector3 away = transform.position - lastSeen; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            Vector3 tangent = Vector3.Cross(Vector3.up, away) * circleSign;
            Vector3 destination = lastSeen + away * preferredDistance + tangent * 0.65f;
            Move(destination, walkSpeed * CurrentPhase.speedMultiplier);
        }

        private CaptainAttack SelectAttack(bool projectileOnly = false)
        {
            if (!TargetAlive || !Visible()) return null;
            candidates.Clear(); scores.Clear(); float total = 0f;
            foreach (CaptainAttack a in attacks)
            {
                if (!Eligible(a)) continue;
                if (projectileOnly && !a.isProjectileAttack)
                    continue;
                float score = phaseIndex == 0 ? a.phase1Weight : a.phase2Weight;
                if (a == lastAttack)
                    score *= CurrentPhase.repeatAttackWeight; // Soft novelty bias, not forced random alternation.
                if (a.punishHealing && Time.time >= healReactAt && Time.time - healObservedAt < 1.5f) score *= 3f;
                score *= Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(fatigue / fatigueThreshold));
                if (score <= 0f) continue;
                candidates.Add(a); scores.Add(score); total += score;
            }
            if (total <= 0f) return null;
            float roll = Random.value * total;
            for (int i = 0; i < candidates.Count; i++) { roll -= scores[i]; if (roll <= 0f) return candidates[i]; }
            return candidates[candidates.Count - 1];
        }
        private bool Eligible(CaptainAttack a)
        {
            if (a == null || !TargetAlive) return false;
            if (a.isProjectileAttack && a.projectilePrefab == null)
                return false;
            float weight = phaseIndex == 0 ? a.phase1Weight : a.phase2Weight;
            return weight > 0f && Distance >= a.minRange && Distance <= a.maxRange &&
                Vector3.Angle(transform.forward, Flat(player.position - transform.position)) <= a.maxFacingAngle &&
                (!readyAt.TryGetValue(a, out float t) || Time.time >= t);
        }
        private void StartAttack(CaptainAttack a, bool continuation)
        {
            // Extensions have their own authored tell. Never restart the same state in a blend.
            CancelAction();
            currentAttack = a; queuedChain = null; chainDecided = false;
            pendingAirSlashes.Clear();
            if (!continuation) extensions = 0;
            machine.Change(attack); Stop();
            committed = false; parryWindow = false;
            attackStepOpen = false;
            attackStepRemaining = a.attackStepDistance;
            lastAttack = a;

            readyAt[a] = Time.time +
                a.cooldown * CurrentPhase.attackCooldownMultiplier;
            fatigue += a.fatigueCost; // Spend on attempt, including whiffs and interruptions.
            fatigueOwed |= fatigue >= fatigueThreshold; // Recovery decay cannot erase an earned rest.
            BeginAnimation(a.trigger, a.fullStateName, a.watchdogSeconds);
            if (OnMesh && (a.useRootMotion || a.attackStepDistance > 0f)) if (OnMesh &&
                (a.jumpSlam || a.useRootMotion || a.attackStepDistance > 0f))
            {
                agent.updatePosition = false;
                agent.nextPosition = transform.position;
            }
        }
        private bool TickAttack()
        {
            bool animationReady =
                currentAttack != null &&
                expectedHash != 0 &&
                enteredHash == expectedHash &&
                !stateExited;

            if (animationReady && animator.speed > 0f)
            {
                bool casting = caster != null && caster.IsCasting;

                // Turn during preparation and while casting, then hold direction for the swing.
                if ((!committed || casting) && TargetAlive && Visible())
                {
                    Face(lastSeen, currentAttack.trackingDegreesPerSecond);
                }

                TickAttackStep();
            }

            return TickAction();
        }

        private Vector3 JumpPosition(float t)
        {
            Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, t);

            // Starts at ground level, reaches jumpHeight, then returns to ground.
            position.y +=
                4f * currentAttack.jumpHeight * t * (1f - t);

            return position;
        }

        private bool JumpPathClear()
        {
            float radius = Mathf.Max(0.05f, agent.radius * 0.9f);
            float height = Mathf.Max(agent.height, radius * 2f);

            Vector3 previous = JumpPosition(0f);

            // Check the boss's body along the planned jump.
            for (int i = 1; i <= 20; i++)
            {
                Vector3 next = JumpPosition(i / 20f);

                Vector3 bottom =
                    previous + Vector3.up * (radius + 0.06f);

                Vector3 top =
                    previous + Vector3.up * (height - radius + 0.06f);

                Vector3 delta = next - previous;

                if (Physics.CheckCapsule(
                        bottom, top, radius, environment,
                        QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                if (delta.sqrMagnitude > 0.000001f &&
                    Physics.CapsuleCast(
                        bottom, top, radius, delta.normalized,
                        out _, delta.magnitude, environment,
                        QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                previous = next;
            }

            Vector3 endBottom =
                jumpEnd + Vector3.up * (radius + 0.06f);

            Vector3 endTop =
                jumpEnd + Vector3.up * (height - radius + 0.06f);

            return !Physics.CheckCapsule(
                endBottom, endTop, radius, environment,
                QueryTriggerInteraction.Ignore);
        }

        public float GetJumpLandingCorrectionWeight(float blendWindow)
        {
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                !currentAttack.jumpSlam ||
                !jumpStarted ||
                stateExited)
            {
                return 0f;
            }

            AnimatorStateInfo info =
                animator.GetCurrentAnimatorStateInfo(0);

            if (info.fullPathHash != expectedHash &&
                animator.IsInTransition(0))
            {
                info = animator.GetNextAnimatorStateInfo(0);
            }

            if (info.fullPathHash != expectedHash)
                return 0f;

            float landingTime = currentAttack.jumpLandNormalized;

            float correctionStart = Mathf.Max(
                jumpStartNormalized,
                landingTime - Mathf.Max(0.01f, blendWindow));

            float progress = Mathf.InverseLerp(
                correctionStart,
                landingTime,
                info.normalizedTime);

            return Mathf.SmoothStep(0f, 1f, progress);
        }

        public void BeginJump(float normalizedTime)
        {
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                !currentAttack.jumpSlam ||
                jumpStarted ||
                !TargetAlive ||
                !OnMesh)
            {
                return;
            }

            if (currentAttack.jumpLandNormalized <= normalizedTime + 0.03f)
            {
                Debug.LogWarning(
                    "Jump landing time must be after takeoff.", this);
                return;
            }

            var filter = new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            };

            jumpStart = transform.position;

            Vector3 toPlayer =
                Flat(player.position - jumpStart);

            float distance = Mathf.Min(
                currentAttack.jumpMaxDistance,
                Mathf.Max(
                    0f,
                    toPlayer.magnitude - currentAttack.attackStopDistance));

            Vector3 desired =
                jumpStart + toPlayer.normalized * distance;

            // Keep this first version on connected, roughly level arena ground.
            if (!NavMesh.SamplePosition(
                    desired, out NavMeshHit landing, 0.4f, filter) ||
                Mathf.Abs(landing.position.y - jumpStart.y) > 0.6f ||
                NavMesh.Raycast(
                    jumpStart, landing.position, out _, filter))
            {
                Debug.LogWarning(
                    "Jump skipped: no safe landing route.", this);
                return;
            }

            jumpEnd = landing.position;

            if (!JumpPathClear())
            {
                Debug.LogWarning(
                    "Jump skipped: wall, ceiling, or obstacle in jump path.",
                    this);
                return;
            }
            // Aim from the planned landing point toward the player's
            // position at takeoff. Keep this direction during flight.
            Vector3 strikeDirection =
                Flat(player.position - jumpEnd);

            if (strikeDirection.sqrMagnitude < 0.0001f)
                strikeDirection = toPlayer;

            if (strikeDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(strikeDirection.normalized);

                Quaternion strikeOffset = Quaternion.AngleAxis(
                    currentAttack.jumpFacingOffsetDegrees,
                    Vector3.up);

                transform.rotation = strikeOffset * targetRotation;
            }

            CloseHitbox();
            attackStepOpen = false;
            parryWindow = false;

            jumpStartNormalized = normalizedTime;
            jumpStarted = jumpActive = true;
            Debug.Log(
            $"SCRIPTED JUMP STARTED | " +
            $"Asset: {currentAttack.name} | " +
            $"Height: {currentAttack.jumpHeight} | " +
            $"Start Y: {jumpStart.y:F2} | " +
            $"Landing Y: {jumpEnd.y:F2}",
            this);
            jumpLanded = false;

            Stop();

            // Keep the navigation position on the ground during flight.
            agent.updatePosition = false;
            agent.nextPosition = jumpStart;

            CommitAttack();
        }

        private void TickJumpMotion()
        {
            AnimatorStateInfo info =
                animator.GetCurrentAnimatorStateInfo(0);

            if (info.fullPathHash != expectedHash &&
                animator.IsInTransition(0))
            {
                info = animator.GetNextAnimatorStateInfo(0);
            }

            if (info.fullPathHash != expectedHash)
                return;

            float t = Mathf.InverseLerp(
                jumpStartNormalized,
                currentAttack.jumpLandNormalized,
                info.normalizedTime);

            transform.position = JumpPosition(t);
        }

        public void LandJump()
        {
            if (CurrentState != State.Attack || !jumpActive)
                return;

            jumpActive = false;

            transform.position = jumpEnd;
            agent.nextPosition = jumpEnd;
            agent.updatePosition = true;

            jumpLanded = true;

            // A stagger earned during flight happens after landing.
            if (poise <= 0f)
            {
                machine.Change(stagger);
                return;
            }

            if (currentAttack.useGroundBurst)
            {
                if (groundBurst != null)
                {
                    groundBurst.QueueImpact(
                        gameObject,
                        currentAttack,
                        jumpEnd);
                }
                else
                {
                    Debug.LogError(
                        "Assign BossGroundBurst on the boss.",
                        this);
                }
            }
            else
            {
                OpenHitbox();
            }
        }

        private void CancelJump()
        {
            if (jumpActive)
            {
                Vector3 position = transform.position;

                float horizontalLength =
                    Flat(jumpEnd - jumpStart).magnitude;

                float t = horizontalLength > 0.001f
                    ? Mathf.Clamp01(
                        Flat(position - jumpStart).magnitude /
                        horizontalLength)
                    : 0f;

                // Search near the ground below the jump, not its airborne height.
                position.y = Mathf.Lerp(jumpStart.y, jumpEnd.y, t);

                var filter = new NavMeshQueryFilter
                {
                    agentTypeID = agent.agentTypeID,
                    areaMask = agent.areaMask
                };

                transform.position = NavMesh.SamplePosition(
                    position, out NavMeshHit ground, 0.5f, filter)
                    ? ground.position
                    : jumpStart;
            }

            jumpActive = jumpStarted = jumpLanded = false;
        }

        private void TickAttackStep()
        {
            if (currentAttack != null && currentAttack.jumpSlam)
                return;

            if (!attackStepOpen ||
                currentAttack == null ||
                currentAttack.useRootMotion ||
                !OnMesh ||
                !TargetAlive ||
                attackStepRemaining <= 0f)
            {
                return;
            }

            float dt =
                Time.deltaTime *
                Mathf.Max(0f, animator.speed) *
                AttackPlayback;

            float step = Mathf.Min(
                currentAttack.attackStepSpeed * dt,
                attackStepRemaining);

            // Avoid stepping too close to, or through, the player.
            float playerDistance =
                Flat(player.position - transform.position).magnitude;

            float availableSpace = Mathf.Max(
                0f,
                playerDistance - currentAttack.attackStopDistance);

            step = Mathf.Min(step, availableSpace);

            if (step <= 0.0001f)
                return;

            Vector3 start = transform.position;
            Vector3 forward = Flat(transform.forward).normalized;
            Vector3 desired = start + forward * step;

            // Find nearby ground, then check that the path stays on the NavMesh.
            if (!NavMesh.SamplePosition(
                    desired, out NavMeshHit ground, 0.15f, agent.areaMask))
            {
                return;
            }

            desired = ground.position;

            if (NavMesh.Raycast(
                    start, desired, out NavMeshHit edge, agent.areaMask))
            {
                desired = edge.position;
            }

            float moved = Flat(desired - start).magnitude;

            if (moved <= 0.0001f)
                return;

            transform.position = desired;
            agent.nextPosition = desired;

            attackStepRemaining =
                Mathf.Max(0f, attackStepRemaining - moved);
        }

        private bool TickAction()
        {
            bool spellCasting = CurrentState == State.Attack &&
                caster != null && caster.IsCasting;

            // Advance by Animator speed so local animator hit-stop does not consume the budget.
            float playback = CurrentState == State.Attack
                ? AttackPlayback
                : 1f;

            actionAge +=
                Time.deltaTime *
                Mathf.Max(0f, animator.speed) *
                playback;

            if (stateExited && !spellCasting)
            {
                // Authored completion: animation finished normally via AnimationStateExited
                CompleteAction(true);
            }
            else if (!spellCasting && actionAge >= actionTimeout)
            {
                // Fault recovery: animation got stuck, state name mismatch, or relay failed
                Debug.LogWarning($"Boss action contract failed in {CurrentState}. Closed weapon and returned safely.", this);
                queuedChain = null;
                CompleteAction(false);
            }

            return true;
        }
        private void BeginAnimation(string trigger, string fullPath, float watchdog)
        {
            animator.SetFloat(AttackPlaybackHash, AttackPlayback);
            expectedHash = Animator.StringToHash(fullPath); enteredHash = 0;
            actionAge = 0f; actionTimeout = Mathf.Max(0.2f, watchdog); stateExited = false;
            Trigger(trigger);
        }
        private void Trigger(string parameter)
        {
            int hash = Animator.StringToHash(parameter ?? "");
            if (!triggers.Contains(hash)) { Debug.LogWarning($"Missing boss trigger '{parameter}'.", this); return; }
            // Clear stale requests before committing a new action (especially Hit vs Roar).
            foreach (int t in triggers) animator.ResetTrigger(t);
            animator.SetTrigger(hash);
        }
        public void AnimationStateEntered(int hash) { if (hash == expectedHash) enteredHash = hash; }
        public void AnimationStateExited(int hash)
        {
            if (hash == expectedHash && enteredHash == hash) { CloseHitbox(); stateExited = true; }
        }
        public bool AcceptAnimationEvent(int hash)
        {
            return isActiveAndEnabled && CurrentState != State.Dead && expectedHash != 0 &&
                hash == expectedHash && enteredHash == hash && !stateExited;
        }
        public void PrepareStrike()
        {
            if (jumpStarted)
                return;

            if (CurrentState != State.Attack || currentAttack == null)
                return;
            suppressSwordForGroundBurst = false;
            // The previous strike must stop dealing damage before aiming again.
            CloseHitbox();
            parryWindow = false;

            committed = false;
            attackStepOpen = false;
            attackStepRemaining = currentAttack.attackStepDistance;
        }

        public void SetAttackStep(bool open)
        {
            if (CurrentState != State.Attack || currentAttack == null)
                return;

            attackStepOpen = open && !currentAttack.useRootMotion;
        }

        public void CommitAttack()
        {
            if (CurrentState != State.Attack || committed) return;
            committed = true; onCommittedAttack.Invoke();
        }
        private Quaternion GetAirSlashRotation(Vector3 direction)
        {
            Vector3 bladeAxis = projectileSpawnPoint.up;

            if (airSlashBladeBase != null && airSlashBladeTip != null)
            {
                bladeAxis =
                    airSlashBladeTip.position -
                    airSlashBladeBase.position;
            }

            // Keep the blade's visible angle across the travel direction.
            Vector3 slashUp =
                Vector3.ProjectOnPlane(bladeAxis, direction);

            // If the blade points directly along the shot,
            // use the spawn point's orientation instead.
            if (slashUp.sqrMagnitude < 0.0001f)
            {
                slashUp = Vector3.ProjectOnPlane(
                    projectileSpawnPoint.up,
                    direction);
            }

            if (slashUp.sqrMagnitude < 0.0001f)
                slashUp = Vector3.ProjectOnPlane(Vector3.up, direction);

            if (slashUp.sqrMagnitude < 0.0001f)
                slashUp = Vector3.ProjectOnPlane(Vector3.right, direction);

            Quaternion rotation =
                Quaternion.LookRotation(direction, slashUp.normalized);

            return rotation * Quaternion.AngleAxis(
                currentAttack.projectileRollOffsetDegrees,
                Vector3.forward);
        }
        public void QueueAirSlash()
        {
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                !currentAttack.isProjectileAttack ||
                expectedHash == 0 ||
                enteredHash != expectedHash ||
                stateExited ||
                !TargetAlive)
            {
                return;
            }

            if (projectileSpawnPoint == null ||
                currentAttack.projectilePrefab == null)
            {
                Debug.LogWarning(
                    "Air slash needs a spawn point and projectile prefab.",
                    this);

                return;
            }

            // Each accepted animation event adds one shot.
            pendingAirSlashes.Enqueue(new PendingAirSlash
            {
                stateHash = expectedHash,

                aim = player.position +
                      Vector3.up * currentAttack.projectileAimHeight
            });

            CommitAttack();
        }

        private void LateUpdate()
        {
            if (pendingAirSlashes.Count == 0)
                return;

            // Cancel pending shots if this attack is no longer valid.
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                !currentAttack.isProjectileAttack ||
                projectileSpawnPoint == null ||
                currentAttack.projectilePrefab == null ||
                enteredHash != expectedHash ||
                stateExited ||
                !TargetAlive)
            {
                pendingAirSlashes.Clear();
                return;
            }

            // Release after animation has updated the sword pose.
            while (pendingAirSlashes.Count > 0)
            {
                PendingAirSlash shot = pendingAirSlashes.Dequeue();

                if (shot.stateHash != expectedHash)
                    continue;

                SpawnAirSlash(shot.aim);
            }
        }

        private void SpawnAirSlash(Vector3 aim)
        {
            Vector3 spawnPosition = projectileSpawnPoint.position;

            // Keep the existing wall check.
            Vector3 bodyPoint = new Vector3(
                transform.position.x,
                spawnPosition.y,
                transform.position.z);

            if (Physics.Linecast(
                bodyPoint,
                spawnPosition,
                environment,
                QueryTriggerInteraction.Ignore))
            {
                return;
            }

            Vector3 direction = aim - spawnPosition;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            direction.Normalize();

            Vector2 range = currentAttack.projectileSpeedRange;

            float minimum = Mathf.Max(
                0.1f,
                Mathf.Min(range.x, range.y));

            float maximum = Mathf.Max(
                minimum,
                Mathf.Max(range.x, range.y));

            // Pick a speed separately for each projectile.
            float speed = Random.Range(minimum, maximum);

            SwordAirSlashProjectile projectile = Instantiate(
                currentAttack.projectilePrefab,
                spawnPosition,
                GetAirSlashRotation(direction));

            projectile.Launch(
                gameObject,
                direction,
                speed,
                currentAttack.damage,
                currentAttack.hitReaction,
                currentAttack.hitReactionMode,
                currentAttack.projectileFlightSound,
                currentAttack.projectileFlightSoundVolume);
            onProjectileReleased.Invoke();
        }
        public void PrepareGroundBurst()
        {
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                currentAttack.jumpSlam ||
                !currentAttack.useGroundBurst)
            {
                return;
            }

            // This strike will use burst damage instead of sword damage.
            suppressSwordForGroundBurst = true;
            CloseHitbox();
            parryWindow = false;
        }
        public void RoarBurstFromAnimation()
        {
            if (!isActiveAndEnabled ||
                CurrentState != State.PhaseTransition ||
                roarBurstUsed)
            {
                return;
            }

            if (groundBurst == null ||
                !groundBurst.isActiveAndEnabled ||
                roarBurstSettings == null)
            {
                Debug.LogError(
                    "Assign BossGroundBurst and Roar Burst Settings.",
                    this);

                return;
            }

            roarBurstUsed = true;

            groundBurst.QueueImpact(
                gameObject,
                roarBurstSettings,
                transform.position,
                fromCombo: false,
                pointOverride: transform);
        }

        public void GroundBurstFromAnimation()
        {
            if (CurrentState != State.Attack ||
                currentAttack == null ||
                currentAttack.jumpSlam ||
                !currentAttack.useGroundBurst ||
                comboGroundBurstUsed)
            {
                return;
            }

            PrepareGroundBurst();
            CommitAttack();

            // Stop a scripted forward step at impact.
            attackStepOpen = false;
            attackStepRemaining = 0f;

            if (groundBurst == null || !groundBurst.isActiveAndEnabled)
            {
                Debug.LogError(
                    "Assign an enabled BossGroundBurst component on the boss.",
                    this);

                return;
            }

            // This combo supports one ground-burst impact per play.
            comboGroundBurstUsed = true;

            groundBurst.QueueImpact(
                gameObject,
                currentAttack,
                transform.position,
                fromCombo: true);
        }
        public void OpenHitbox(int hitProfile = 0)
        {
            if (suppressSwordForGroundBurst)
                return;
            if (currentAttack != null &&
            currentAttack.jumpSlam &&
            currentAttack.useGroundBurst)
            {
                return;
            }
            if (currentAttack != null && currentAttack.isProjectileAttack)
                return;
            if (currentAttack != null &&
            currentAttack.jumpSlam &&
            !jumpLanded)
            {
                return;
            }
            if (CurrentState != State.Attack || currentAttack == null) return;
            CommitAttack(); // Missing commit event still cannot enable perfect active-frame tracking.
            CaptainAttack.MeleeHitSettings hit = currentAttack.GetMeleeHit(hitProfile);
            if (hitProfile > 0 && hit == null)
                Debug.LogWarning("Missing melee hit override " + hitProfile + " on " + currentAttack.name + "; using defaults.", this);
            weapon.Open(
                gameObject,
                hit != null && hit.overrideDamage ? hit.damage : currentAttack.damage,
                hit != null && hit.overrideReaction ? hit.reaction : currentAttack.hitReaction,
                hit != null && hit.overrideReaction ? hit.reactionMode : currentAttack.hitReactionMode,
                hit != null ? hit.pushDistance : currentAttack.meleePushDistance,
                hit != null ? hit.pushSeconds : currentAttack.meleePushSeconds,
                hit != null ? hit.blockedPushDistance : currentAttack.blockedMeleePushDistance,
                hit != null ? hit.blockedPushSeconds : currentAttack.blockedMeleePushSeconds);
        }
        public void CloseHitbox() { if (weapon != null) weapon.Close(); }
        public void BeginSpell()
        {
            if (CurrentState != State.Attack || currentAttack == null ||
                currentAttack.spell == null || caster == null) return;
            CommitAttack();
            caster.StartCast(currentAttack.spell, player, gameObject, phaseIndex);
        }
        public void SetParryWindow(bool open)
        {
            parryWindow = open && CurrentState == State.Attack && currentAttack != null && currentAttack.parryable;
        }
        public void DecideChain()
        {
            if (CurrentState != State.Attack || chainDecided || currentAttack == null) return;
            chainDecided = true;
            CaptainAttack next = currentAttack.followup;
            float chance = phaseIndex == 0 ? currentAttack.phase1ChainChance : currentAttack.phase2ChainChance;
            if (requestedPhase == phaseIndex && extensions < CurrentPhase.maxExtensions &&
                fatigue < fatigueThreshold && next != null && Eligible(next) && Visible() && Random.value < chance)
                queuedChain = next;
        }
        public void CompleteAction() { CompleteAction(true); }
        private void CompleteAction(bool authored)
        {
            if (CompleteDuelAnimation(authored)) return;
            State finished = CurrentState;
            if (finished == State.Attack &&
            currentAttack.jumpSlam &&
            !jumpLanded)
            {
                authored = false;
            }
            if (finished == State.Attack)
            {
                float recovery = Mathf.Max(
                    CurrentPhase.minimumRecovery,
                    currentAttack.guaranteedRecovery *
                    CurrentPhase.recoveryMultiplier);
                CaptainAttack next = authored ? queuedChain : null;
                CloseHitbox(); parryWindow = false;
                // Recheck eligibility at launch: the player may have escaped after decision.
                if (next != null && requestedPhase == phaseIndex && Eligible(next) && Visible())
                { extensions++; StartAttack(next, true); return; }
                recoveryUntil = Time.time + recovery; machine.Change(recover);
            }
            else if (finished == State.PhaseTransition)
            {
                phaseIndex = requestedPhase; // Stats change at completion, never halfway through the roar.
                fatigue = 0f; fatigueOwed = false; poise = maxPoise;
                recoveryUntil = Time.time + 0.7f; machine.Change(recover);
                onPhaseTransitionFinished.Invoke();
            }
            else if (finished == State.Stagger || finished == State.Taunt)
            {
                if (finished == State.Stagger)
                    poise = maxPoise;

                // If a hit interrupted the walk home, continue afterward.
                if (returningHome)
                {
                    machine.Change(returnHome);
                    return;
                }

                recoveryUntil = Time.time + 0.4f;
                machine.Change(recover);
            }
        }
        public void OnParried()
        {
            if (!IsInParryWindow || health.IsDead) return;
            machine.Change(stagger); // Stagger's enter owns Hit trigger for every interruption source.
        }
        private void HandleDamaged(DamageInfo info)
        {
            if (!configured || health.IsDead || CurrentState == State.Dead) return;
            float ratio = health.CurrentHealth / Mathf.Max(0.01f, health.MaxHealth);
            // A large hit requests the deepest eligible phase; it cannot overwrite death.
            for (int i = phaseIndex + 1; i < phases.Length; i++)
                if (ratio <= phases[i].healthThreshold) requestedPhase = Mathf.Max(requestedPhase, i);
            if (info.WasBlocked) return; // The boss filter already processed guard poise.
            poiseDamagedAt = Time.time;
            bool hitByPlayer =
            player != null &&
            info.Attacker != null &&
            (info.Attacker.transform == player ||
            info.Attacker.transform.IsChildOf(player));

            if (returningHome && hitByPlayer)
                TryResumeCombat(true);
            // Transition is uninterruptible, but NOT invulnerable: Health still processes damage.
            if (CurrentState == State.PhaseTransition || CurrentState == State.Stagger) return;
            poise -= Mathf.Max(0f, info.Amount) * poiseDamageMultiplier;
            if (poise <= 0f && !jumpActive)
                machine.Change(stagger);
        }
        private void HandleDeath()
        {
            if (machine == null || CurrentState == State.Dead)
                return;

            returningHome = false;

            CancelAction();
            if (caster != null) caster.Stop(true);
            machine.Change(dead);
        }
        // Call ONLY when the player's visible healing animation starts, not on button press.
        public void NotifyPlayerHealStarted()
        {
            if (!configured || !machine.IsWithin(combat) || !TargetAlive || !Visible()) return;
            healObservedAt = Time.time; healReactAt = Time.time + Random.Range(reactionDelay.x, reactionDelay.y);
        }
        // Call after a player's completed attack hit no valid enemy; this is an explicit sensor input.
        public void NotifyPlayerWhiff()
        {
            if (!isActiveAndEnabled ||
                !configured ||
                health.IsDead ||
                !machine.IsWithin(combat) ||
                !TargetAlive ||
                Distance > noticeMissDistance ||
                !Visible())
            {
                return;
            }

            whiffObservedAt = Time.time;
            whiffReactAt = Time.time +
                Random.Range(reactionDelay.x, reactionDelay.y);
        }
        private bool Visible()
        {
            return player != null && !Physics.Linecast(transform.position + Vector3.up * 1.5f,
                player.position + Vector3.up, environment, QueryTriggerInteraction.Ignore);
        }
        private void Move(Vector3 destination, float speed)
        {
            if (!OnMesh) return;
            agent.isStopped = false; agent.speed = speed; agent.stoppingDistance = 0.15f;
            if (Time.time < pathAt) return;
            pathAt = Time.time + 0.15f;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 1f, agent.areaMask))
                agent.SetDestination(hit.position);
        }
        private void Stop() { if (OnMesh) { agent.isStopped = true; agent.ResetPath(); } }
        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        private void Face(Vector3 position, float degreesPerSecond)
        {
            Vector3 direction = Flat(position - transform.position);
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                degreesPerSecond * Time.deltaTime);
        }
        public void ApplyRootMotion(Vector3 delta)
        {
            if (jumpActive)
            {
                TickJumpMotion();
                return;
            }

            if (CurrentState == State.Attack &&
                currentAttack != null &&
                currentAttack.jumpSlam)
            {
                return;
            }
            if (!configured || CurrentState != State.Attack || currentAttack == null ||
                !currentAttack.useRootMotion || enteredHash != expectedHash || !OnMesh) return;
            // This ground-based captain never leaps via root motion. No teleport over NavMesh holes.
            Vector3 desired = transform.position + Flat(delta);
            if (NavMesh.Raycast(transform.position, desired, out NavMeshHit edge, agent.areaMask)) desired = edge.position;
            if (NavMesh.SamplePosition(desired, out NavMeshHit grounded, 0.3f, agent.areaMask))
            { transform.position = grounded.position; agent.nextPosition = grounded.position; }
        }
        private void CancelAction()
        {
            CancelDuelAction();
            comboGroundBurstUsed = false;
            suppressSwordForGroundBurst = false;
            pendingAirSlashes.Clear();
            CancelJump();
            attackStepOpen = false;
            attackStepRemaining = 0f;
            CloseHitbox(); parryWindow = false; expectedHash = enteredHash = 0;
            stateExited = false; queuedChain = null;
            if (OnMesh) { agent.nextPosition = transform.position; agent.updatePosition = true; }
            if (caster != null) caster.Stop(false);
        }
        private void ForceLocomotion()
        {
            if (animator == null) return;
            foreach (int t in triggers) animator.ResetTrigger(t);
            animator.CrossFadeInFixedTime(Animator.StringToHash(locomotionState), 0.15f, 0);
        }
    }
}

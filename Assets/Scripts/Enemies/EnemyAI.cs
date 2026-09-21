using UnityEngine;
using UnityEngine.AI;
using PirateGame.Combat;

namespace PirateGame.Enemies
{
    /// <summary>
    /// Finite-state-machine enemy AI for land pirates (FSM approach from the
    /// Jojik "Unity 3D RPG Combat System" tutorial, extended for group combat).
    ///
    /// States:
    ///   Idle    — stand at spawn (or wait between patrol points)
    ///   Patrol  — walk between patrol points (optional; idles if none set)
    ///   Chase   — run toward the player after spotting them
    ///   Circle  — engaged but waiting for an attack token: strafe on a ring
    ///             around the player (this is what makes groups feel coordinated)
    ///   Attack  — windup → hit → recovery, with a parry window during windup
    ///   Stagger — briefly stunned (after being parried or hit)
    ///   Return  — lost the player, walk back to spawn/patrol
    ///   Dead    — terminal
    ///
    /// PARRY SUPPORT (for the future parry system):
    ///   * During the attack WINDUP phase, <see cref="IsInParryWindow"/> is true.
    ///   * The player's parry code should call <see cref="OnParried"/> in that
    ///     window — the enemy cancels its attack and enters Stagger.
    ///
    /// ANIMATION SLOTS (all optional — nothing breaks if the Animator or a
    /// parameter is missing; hook these up when animations are ready):
    ///   float   "Speed"     — 0..1 normalized movement speed (blend tree idle/walk/run)
    ///   trigger "Attack"    — fired at the start of the attack windup
    ///   trigger "Hit"       — fired when this enemy takes damage
    ///   trigger "Parried"   — fired when the player parries this enemy
    ///   trigger "Die"       — fired on death
    ///   Optional animation events on the attack clip:
    ///     AnimEvent_AttackImpact() — the exact frame the sword should deal damage
    ///     AnimEvent_AttackEnd()    — the last frame of the attack animation
    ///   (until events are wired, timing falls back to the inspector durations)
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        public enum State { Idle, Patrol, Chase, Circle, Attack, Stagger, Return, Dead }

        private enum AttackPhase { None, Windup, Impact, Recovery }

        [Header("Detection")]
        [Tooltip("How far the enemy can see the player.")]
        [SerializeField] private float viewRadius = 12f;
        [Tooltip("Field of view in degrees (full cone).")]
        [SerializeField] private float viewAngle = 110f;
        [Tooltip("Inside this radius the player is noticed even behind the enemy.")]
        [SerializeField] private float instantDetectRadius = 2.5f;
        [Tooltip("Layers that block line of sight (walls, rocks...). Leave as Everything minus the characters.")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Tooltip("Seconds the player must stay unseen before the enemy gives up.")]
        [SerializeField] private float loseSightTime = 4f;

        [Header("Movement")]
        [Tooltip("Speed while patrolling / returning home.")]
        [SerializeField] private float walkSpeed = 2f;
        [Tooltip("Speed while CHASING the player. Keep this well above walk speed so the charge reads clearly.")]
        [SerializeField] private float runSpeed = 6f;
        [Tooltip("How fast the enemy gets up to speed. Higher = snappier lunge when it spots you.")]
        [SerializeField] private float acceleration = 30f;

        [Header("Patrol")]
        [Tooltip("Route to walk. Leave EMPTY to make this enemy a stationary guard that idles at its spawn point.")]
        [SerializeField] private PatrolRoute patrolRoute;
        [Tooltip("Alternative to a PatrolRoute: drop individual waypoint transforms here. Ignored if a Patrol Route is set.")]
        [SerializeField] private Transform[] patrolPoints;
        [Tooltip("Seconds to pause at each waypoint (plays the idle animation).")]
        [SerializeField] private float patrolWaitTime = 2f;
        [Tooltip("Random extra wait on top, so a group of enemies doesn't move in lockstep.")]
        [SerializeField] private float patrolWaitVariance = 1f;
        [Tooltip("Start at the nearest waypoint instead of the first one.")]
        [SerializeField] private bool startAtNearestWaypoint = true;

        [Header("Guard behaviour (stationary enemies)")]
        [Tooltip("Stationary guards slowly sweep their view around, so they feel alive and can still catch you.")]
        [SerializeField] private bool scanWhileGuarding = true;
        [Tooltip("How far to the left/right the guard sweeps, in degrees.")]
        [SerializeField] private float scanAngle = 60f;
        [Tooltip("Seconds for one full left-right-left sweep.")]
        [SerializeField] private float scanPeriod = 6f;

        [Header("Combat")]
        [SerializeField] private float attackRange = 1.8f;
        [Tooltip("Ring distance kept while circling / waiting for an attack token.")]
        [SerializeField] private float circleRadius = 3.5f;
        [Tooltip("Sideways strafe speed while circling the player.")]
        [SerializeField] private float circleSpeed = 1.5f;
        [SerializeField] private float attackDamage = 10f;
        [Tooltip("Seconds of telegraph before the hit lands. This is the PARRY WINDOW.")]
        [SerializeField] private float attackWindupTime = 0.6f;
        [Tooltip("Seconds after the hit before the enemy can act again.")]
        [SerializeField] private float attackRecoveryTime = 0.9f;
        [SerializeField] private float attackCooldown = 1.5f;
        [Tooltip("Seconds stunned after being parried.")]
        [SerializeField] private float parryStaggerTime = 2f;
        [Tooltip("Seconds stunned when taking a normal hit.")]
        [SerializeField] private float hitStaggerTime = 0.35f;
        [Tooltip("If an enemy holds an attack slot this long without landing a strike, it gives the slot to a crewmate.")]
        [SerializeField] private float maxTokenHoldTime = 3f;

        [Header("Ragdoll")]
        [SerializeField] private GameObject ragdollPrefab;
        [SerializeField] private Transform ragdollRootBone; // root bone reference on the RAGDOLL prefab, matching the animated skeleton's hierarchy

        [Header("Animation (optional)")]
        [SerializeField] private Animator animator;
        [Tooltip("How many attack clips sit in the Attack state's blend tree (thresholds 0,1,2...). " +
                 "One is picked at random per swing. Set to 1 if you only have a single attack clip.")]
        [SerializeField] private int attackVariantCount = 3;
        [Tooltip("If true, damage is applied by AnimEvent_AttackImpact() on the attack clip instead of the windup timer.")]
        [SerializeField] private bool useAnimationEvents = false;

        // ---- public state (read by other systems / debugging) ----
        public State CurrentState { get; private set; } = State.Idle;

        /// <summary>True while this enemy's attack can be parried (windup phase).</summary>
        public bool IsInParryWindow => attackPhase == AttackPhase.Windup;

        // ---- internals ----
        private NavMeshAgent agent;
        private Health health;
        private EnemyGroupCoordinator crew;   // cached in OnEnable — the squad's brain
        private Transform player;
        private Health playerHealth;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private int patrolIndex;
        private int patrolDirection = 1;   // for PingPong routes
        private float currentWaitTime;     // this stop's randomised pause
        private float stateTimer;          // generic per-state timer
        private float lastSeenTimer;       // time since player was last visible
        private Vector3 lastKnownPlayerPos;
        private float attackCooldownTimer;
        private AttackPhase attackPhase = AttackPhase.None;
        private float circleDirection = 1f; // 1 = clockwise, -1 = counter-clockwise
        private float circleDirTimer;
        private float repathTimer;
        private float tokenHoldTimer;

        // Animator parameter caching so missing parameters never spam warnings.
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int HitParam = Animator.StringToHash("Hit");
        private static readonly int ParriedParam = Animator.StringToHash("Parried");
        private static readonly int DieParam = Animator.StringToHash("Die");
        private static readonly int AttackVariantParam = Animator.StringToHash("AttackVariant");
        private System.Collections.Generic.HashSet<int> animatorParams;

        // ================================================================ lifecycle

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheAnimatorParams();

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            crew = EnemyGroupCoordinator.Instance;
            crew.Register(this);
            if (health != null)
            {
                health.OnDamaged += HandleDamaged;
                health.OnDeath += HandleDeath;
            }
        }

        private void OnDisable()
        {
            // Use Existing, never Instance — this runs during scene teardown, and
            // creating a GameObject there makes Unity log a cleanup error.
            if (EnemyGroupCoordinator.Existing != null)
                EnemyGroupCoordinator.Existing.Unregister(this);
            if (health != null)
            {
                health.OnDamaged -= HandleDamaged;
                health.OnDeath -= HandleDeath;
            }
        }

        private void Start()
        {
            FindPlayer();

            // Begin at whichever waypoint we're already standing near, so enemies
            // don't all walk back to waypoint 0 when the scene starts.
            if (startAtNearestWaypoint && patrolRoute != null && patrolRoute.Count > 0)
                patrolIndex = patrolRoute.GetClosestIndex(transform.position);

            EnterState(HasPatrolPath ? State.Patrol : State.Idle);
        }

        private void Update()
        {
            if (CurrentState == State.Dead) return;

            if (player == null)
            {
                // Player may spawn after us — keep looking occasionally.
                if (Time.frameCount % 60 == 0) FindPlayer();
            }

            attackCooldownTimer -= Time.deltaTime;

            switch (CurrentState)
            {
                case State.Idle:    TickIdle();    break;
                case State.Patrol:  TickPatrol();  break;
                case State.Chase:   TickChase();   break;
                case State.Circle:  TickCircle();  break;
                case State.Attack:  TickAttack();  break;
                case State.Stagger: TickStagger(); break;
                case State.Return:  TickReturn();  break;
            }

            UpdateAnimator();
        }

        // ================================================================ state machine core

        private void EnterState(State next)
        {
            // ----- exit current -----
            switch (CurrentState)
            {
                case State.Attack:
                    attackPhase = AttackPhase.None;
                    crew.ReleaseAttackToken(this);
                    break;
            }

            CurrentState = next;
            stateTimer = 0f;

            // ----- enter next -----
            switch (next)
            {
                case State.Idle:
                    SetAgentStopped(true);
                    agent.speed = walkSpeed;
                    SetAgentAutoRotate(true);
                    // Randomise each pause so a squad on the same route desynchronises.
                    currentWaitTime = patrolWaitTime + Random.Range(0f, patrolWaitVariance);
                    break;

                case State.Patrol:
                    SetAgentStopped(false);
                    agent.speed = walkSpeed;
                    agent.acceleration = acceleration;
                    agent.stoppingDistance = 0.2f;
                    SetAgentAutoRotate(true);
                    if (HasPatrolPath) SetDestinationSafe(CurrentPatrolPoint());
                    break;

                case State.Chase:
                    SetAgentStopped(false);
                    // Charge — noticeably faster than the patrol walk.
                    agent.speed = runSpeed;
                    agent.acceleration = acceleration;
                    agent.stoppingDistance = attackRange * 0.8f;
                    SetAgentAutoRotate(true);
                    crew.ReportEngaged(this);
                    break;

                case State.Circle:
                    SetAgentStopped(false);
                    agent.speed = circleSpeed;
                    agent.stoppingDistance = 0.1f;
                    // We face the player ourselves while strafing, so the agent must
                    // not also rotate us toward its movement direction (they fight).
                    SetAgentAutoRotate(false);
                    circleDirTimer = Random.Range(2f, 4f);
                    break;

                case State.Attack:
                    SetAgentStopped(true);
                    SetAgentAutoRotate(false);
                    attackPhase = AttackPhase.Windup;
                    // Pick one of the slash variants so a group doesn't swing in unison.
                    SetFloat(AttackVariantParam, Random.Range(0, attackVariantCount));
                    SetTrigger(AttackParam);
                    break;

                case State.Stagger:
                    SetAgentStopped(true);
                    SetAgentAutoRotate(false);
                    break;

                case State.Return:
                    SetAgentStopped(false);
                    agent.speed = walkSpeed;
                    agent.acceleration = acceleration;
                    agent.stoppingDistance = 0.2f;
                    SetAgentAutoRotate(true);
                    // Patrollers rejoin their route at the nearest waypoint rather
                    // than trekking all the way back to where they spawned.
                    if (patrolRoute != null && patrolRoute.Count > 0)
                    {
                        patrolIndex = patrolRoute.GetClosestIndex(transform.position);
                        SetDestinationSafe(patrolRoute.GetPosition(patrolIndex));
                    }
                    else SetDestinationSafe(spawnPosition);
                    crew.ReportDisengaged(this);
                    break;

                case State.Dead:
                    SetAgentStopped(true);
                    agent.enabled = false;
                    crew.ReportDisengaged(this);
                    foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
                    // Hide the animated mesh renderer(s) immediately since the ragdoll replaces it
                    foreach (var renderer in GetComponentsInChildren<Renderer>())
                        renderer.enabled = false;
                    break;
            }
        }

        // ---- NavMeshAgent guards: calling these off-navmesh logs errors, so check first ----

        private void SetAgentStopped(bool stopped)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = stopped;
        }

        private void SetAgentAutoRotate(bool enabled)
        {
            if (agent != null) agent.updateRotation = enabled;
        }

        private void SetDestinationSafe(Vector3 destination)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.SetDestination(destination);
        }

        // ================================================================ states

        private void TickIdle()
        {
            if (TryDetectPlayer()) return;

            if (HasPatrolPath)
            {
                // Pausing at a waypoint — the animator sits at Speed 0, so whatever
                // clip is in the blend tree's Idle slot plays here.
                stateTimer += Time.deltaTime;
                if (stateTimer >= currentWaitTime)
                {
                    AdvancePatrolIndex();
                    EnterState(State.Patrol);
                }
                return;
            }

            // No route at all — this is a stationary guard. Sweep its gaze so it
            // isn't a statue and can still notice someone sneaking past.
            if (scanWhileGuarding)
            {
                stateTimer += Time.deltaTime;
                float t = Mathf.Sin(stateTimer * Mathf.PI * 2f / Mathf.Max(0.01f, scanPeriod));
                transform.rotation = spawnRotation * Quaternion.Euler(0f, t * scanAngle * 0.5f, 0f);
            }
        }

        private void TickPatrol()
        {
            if (TryDetectPlayer()) return;

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                EnterState(State.Idle);
        }

        // ---- patrol path helpers (a PatrolRoute wins over the loose points array) ----

        private bool HasPatrolPath =>
            (patrolRoute != null && patrolRoute.Count > 0) ||
            (patrolPoints != null && patrolPoints.Length > 0);

        private int PatrolPointCount =>
            patrolRoute != null && patrolRoute.Count > 0 ? patrolRoute.Count
            : (patrolPoints != null ? patrolPoints.Length : 0);

        private Vector3 CurrentPatrolPoint()
        {
            if (patrolRoute != null && patrolRoute.Count > 0)
                return patrolRoute.GetPosition(patrolIndex);

            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                int i = Mathf.Clamp(patrolIndex, 0, patrolPoints.Length - 1);
                if (patrolPoints[i] != null) return patrolPoints[i].position;
            }
            return spawnPosition;
        }

        private void AdvancePatrolIndex()
        {
            if (patrolRoute != null && patrolRoute.Count > 0)
                patrolIndex = patrolRoute.GetNextIndex(patrolIndex, ref patrolDirection);
            else if (PatrolPointCount > 0)
                patrolIndex = (patrolIndex + 1) % PatrolPointCount;
        }

        private void TickChase()
        {
            if (!UpdatePlayerVisibility()) return;

            float dist = DistanceToPlayer();

            // Close enough to fight — either take an attack slot or circle and wait.
            if (dist <= circleRadius + 0.5f)
            {
                if (attackCooldownTimer <= 0f &&
                    dist <= attackRange &&
                    crew.RequestAttackToken(this))
                {
                    EnterState(State.Attack);
                    return;
                }

                if (dist <= circleRadius)
                {
                    EnterState(State.Circle);
                    return;
                }
            }

            // Keep running at the player's last known position (repath a few times a second).
            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                SetDestinationSafe(lastKnownPlayerPos);
                repathTimer = 0.2f;
            }
        }

        private void TickCircle()
        {
            if (!UpdatePlayerVisibility()) return;

            float dist = DistanceToPlayer();

            // Our turn to attack?
            if (attackCooldownTimer <= 0f &&
                crew.RequestAttackToken(this))
            {
                if (dist <= attackRange)
                {
                    tokenHoldTimer = 0f;
                    EnterState(State.Attack);
                }
                else
                {
                    // Token acquired but out of range — rush in for the strike.
                    agent.speed = runSpeed;
                    SetDestinationSafe(player.position);

                    // Don't hog the slot forever if we can't close the gap (player
                    // kiting, blocked path...) — hand it to a crewmate who can.
                    tokenHoldTimer += Time.deltaTime;
                    if (tokenHoldTimer >= maxTokenHoldTime)
                    {
                        tokenHoldTimer = 0f;
                        attackCooldownTimer = attackCooldown * 0.5f;
                        crew.ReleaseAttackToken(this);
                    }
                }
                FaceTowards(player.position);
                return;
            }
            tokenHoldTimer = 0f;

            // Player broke away — chase again.
            if (dist > circleRadius * 2f)
            {
                EnterState(State.Chase);
                return;
            }

            // Swap strafe direction now and then so it looks alive.
            circleDirTimer -= Time.deltaTime;
            if (circleDirTimer <= 0f)
            {
                circleDirection = -circleDirection;
                circleDirTimer = Random.Range(2f, 4f);
            }

            // Hold formation: move toward my personal slot on the ring around the
            // player, nudged sideways so the whole group slowly orbits.
            Vector3 slot = crew
                .GetSurroundPosition(this, player.position, circleRadius);
            Vector3 tangent = Vector3.Cross(Vector3.up, (transform.position - player.position).normalized);
            Vector3 target = slot + tangent * circleDirection * 1.5f;

            agent.speed = circleSpeed;
            SetDestinationSafe(target);
            FaceTowards(player.position);
        }

        private void TickAttack()
        {
            stateTimer += Time.deltaTime;
            if (player != null) FaceTowards(player.position);

            switch (attackPhase)
            {
                case AttackPhase.Windup:
                    // <<< PARRY WINDOW OPEN — player parry should call OnParried() now >>>
                    if (!useAnimationEvents && stateTimer >= attackWindupTime)
                    {
                        DoAttackImpact();
                    }
                    break;

                case AttackPhase.Impact:
                    attackPhase = AttackPhase.Recovery;
                    break;

                case AttackPhase.Recovery:
                    if (!useAnimationEvents && stateTimer >= attackWindupTime + attackRecoveryTime)
                    {
                        FinishAttack();
                    }
                    break;
            }
        }

        private void TickStagger()
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= (wasParried ? parryStaggerTime : hitStaggerTime))
            {
                wasParried = false;
                EnterState(player != null && !PlayerIsDead ? State.Chase : State.Return);
            }
        }

        private void TickReturn()
        {
            if (TryDetectPlayer()) return;

            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                transform.rotation = spawnRotation;
                EnterState(HasPatrolPath ? State.Patrol : State.Idle);
            }
        }

        // ================================================================ attack helpers

        private bool wasParried;

        /// <summary>
        /// Live check rather than a cached flag — so if the player respawns or is
        /// healed, the enemies notice and re-engage instead of ignoring them forever.
        /// </summary>
        private bool PlayerIsDead => playerHealth != null && playerHealth.IsDead;

        private void DoAttackImpact()
        {
            attackPhase = AttackPhase.Impact;

            if (player != null)
            {
                float dist = DistanceToPlayer();
                Vector3 toPlayer = (player.position - transform.position).normalized;
                bool inFront = Vector3.Angle(transform.forward, toPlayer) < 75f;

                if (dist <= attackRange * 1.25f && inFront && playerHealth != null)
                {
                    Vector3 approxHitPoint = player.position + Vector3.up * 1.2f; // roughly torso height
                    playerHealth.TakeDamage(new DamageInfo(attackDamage, gameObject, approxHitPoint));
                }
            }
        }

        private void FinishAttack()
        {
            attackCooldownTimer = attackCooldown;
            crew.ReleaseAttackToken(this);
            attackPhase = AttackPhase.None;

            if (PlayerIsDead)
            {
                EnterState(State.Return);
                return;
            }
            EnterState(State.Circle);
        }

        /// <summary>
        /// Call from the player's parry system while <see cref="IsInParryWindow"/> is
        /// true. Cancels the attack and staggers this enemy, leaving it punishable.
        /// </summary>
        public void OnParried()
        {
            if (CurrentState != State.Attack || !IsInParryWindow) return;

            wasParried = true;
            SetTrigger(ParriedParam);
            attackCooldownTimer = attackCooldown;
            EnterState(State.Stagger);
        }

        // ---- Animation event hooks (add these events on the attack clip later) ----

        /// <summary>Animation event: the frame the weapon should connect.</summary>
        public void AnimEvent_AttackImpact()
        {
            if (CurrentState == State.Attack && attackPhase == AttackPhase.Windup)
                DoAttackImpact();
        }

        /// <summary>Animation event: the attack animation finished.</summary>
        public void AnimEvent_AttackEnd()
        {
            if (CurrentState == State.Attack)
                FinishAttack();
        }

        // ================================================================ perception

        private void FindPlayer()
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
            {
                player = go.transform;
                playerHealth = go.GetComponent<Health>();
            }
        }

        private float DistanceToPlayer()
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 b = player.position;    b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private bool CanSeePlayer()
        {
            if (player == null || PlayerIsDead) return false;

            float dist = Vector3.Distance(transform.position, player.position);
            if (dist > viewRadius) return false;

            // Very close = noticed regardless of facing (footsteps, presence).
            if (dist > instantDetectRadius)
            {
                Vector3 dirToPlayer = (player.position - transform.position).normalized;
                if (Vector3.Angle(transform.forward, dirToPlayer) > viewAngle * 0.5f)
                    return false;
            }

            // Line of sight — blocked by obstacles?
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 targetPoint = player.position + Vector3.up * 1.2f;
            if (Physics.Linecast(eye, targetPoint, out RaycastHit hit, obstacleMask,
                                 QueryTriggerInteraction.Ignore))
            {
                if (hit.transform != player && !hit.transform.IsChildOf(player))
                    return false;
            }
            return true;
        }

        /// <summary>Used by passive states — start the fight if the player is spotted.</summary>
        private bool TryDetectPlayer()
        {
            if (!CanSeePlayer()) return false;

            lastKnownPlayerPos = player.position;
            lastSeenTimer = 0f;

            // Tell the crew — nearby pirates join the attack together.
            crew.AlertNearbyEnemies(this, player.position);

            EnterState(State.Chase);
            return true;
        }

        /// <summary>
        /// Used by engaged states. Tracks the player while visible, follows the last
        /// known position for a while when not, gives up after loseSightTime.
        /// Returns false if the state changed (gave up / player died).
        /// </summary>
        private bool UpdatePlayerVisibility()
        {
            if (player == null || PlayerIsDead)
            {
                EnterState(State.Return);
                return false;
            }

            if (CanSeePlayer())
            {
                lastSeenTimer = 0f;
                lastKnownPlayerPos = player.position;
            }
            else
            {
                lastSeenTimer += Time.deltaTime;
                if (lastSeenTimer >= loseSightTime)
                {
                    EnterState(State.Return);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Called by the coordinator when a crewmate spots the player.</summary>
        public void OnGroupAlert(Vector3 playerPosition)
        {
            if (CurrentState == State.Idle || CurrentState == State.Patrol || CurrentState == State.Return)
            {
                lastKnownPlayerPos = playerPosition;
                lastSeenTimer = 0f;
                EnterState(State.Chase);
            }
        }

        // ================================================================ damage / death

        private void HandleDamaged(DamageInfo info)
        {
            if (CurrentState == State.Dead) return;

            SetTrigger(HitParam);

            // Getting hit reveals the attacker even from behind.
            if (player != null)
            {
                lastKnownPlayerPos = player.position;
                lastSeenTimer = 0f;
            }

            if (CurrentState == State.Idle || CurrentState == State.Patrol || CurrentState == State.Return)
            {
                crew.ReportEngaged(this);
                crew.AlertNearbyEnemies(this,
                    player != null ? player.position : transform.position);
            }

            // Light hit-stun (interrupts attacks that are still in windup).
            if (CurrentState != State.Attack || IsInParryWindow)
                EnterState(State.Stagger);
        }

        private void HandleDeath()
        {
            EnterState(State.Dead);

            if (ragdollPrefab != null)
            {
                GameObject ragdollInstance = Instantiate(ragdollPrefab, transform.position, transform.rotation);
                // Optional: match the ragdoll bones' current pose to the animated corpse's last pose,
                // otherwise it'll snap to whatever pose the ragdoll prefab was saved in (usually T-pose or idle).

                MatchRagdollPose(ragdollInstance);

                // Keep the body around for a few seconds, then clean up.
                Destroy(gameObject);
                Destroy(ragdollInstance, 6f);
            }
        }

        void MatchRagdollPose(GameObject ragdollInstance)
        {
            Transform[] animatedBones = GetComponentsInChildren<Transform>();
            Transform[] ragdollBones = ragdollInstance.GetComponentsInChildren<Transform>();

            foreach (Transform ragdollBone in ragdollBones)
            {
                foreach (Transform animatedBone in animatedBones)
                {
                    if (animatedBone.name == ragdollBone.name)
                    {
                        ragdollBone.position = animatedBone.position;
                        ragdollBone.rotation = animatedBone.rotation;
                        break;
                    }
                }
            }
        }

        // ================================================================ misc helpers

        private void FaceTowards(Vector3 worldPos)
        {
            Vector3 dir = worldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * Time.deltaTime);
        }

        // ---- animator (all calls safe when no Animator/controller/parameter exists) ----

        /// <summary>
        /// Builds the set of parameters the controller actually has, so a missing one
        /// is a no-op instead of a warning every frame.
        ///
        /// Called lazily rather than once in Awake: an Animator often has not
        /// initialised that early and returns an EMPTY parameter array, which left the
        /// cache permanently empty and silently stopped Speed / Attack / Hit from ever
        /// being written. Retrying until it reports parameters fixes that.
        /// </summary>
        private void CacheAnimatorParams()
        {
            animatorParams = new System.Collections.Generic.HashSet<int>();
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters) animatorParams.Add(p.nameHash);
        }

        private bool HasParam(int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            if (animatorParams == null || animatorParams.Count == 0) CacheAnimatorParams();
            return animatorParams.Contains(hash);
        }

        private void SetTrigger(int hash)
        {
            if (HasParam(hash)) animator.SetTrigger(hash);
        }

        private void SetFloat(int hash, float value)
        {
            if (HasParam(hash)) animator.SetFloat(hash, value);
        }

        private void UpdateAnimator()
        {
            if (!HasParam(SpeedParam)) return;

            float v = agent.enabled ? agent.velocity.magnitude : 0f;

            // Map actual speed onto the blend tree's thresholds:
            //   0        -> 0.0  (Idle clip, pure)
            //   walkSpeed-> 0.5  (Walk clip, pure)
            //   runSpeed -> 1.0  (Run clip, pure)
            // A plain velocity/runSpeed would land patrol walking at ~0.33 and blend
            // a third of the idle pose into the walk, which looks like wading.
            float t;
            if (v <= walkSpeed)
                t = Mathf.Lerp(0f, 0.5f, walkSpeed <= 0.01f ? 0f : v / walkSpeed);
            else
                t = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((v - walkSpeed) / Mathf.Max(0.01f, runSpeed - walkSpeed)));

            animator.SetFloat(SpeedParam, t, 0.1f, Time.deltaTime);
        }

        // ================================================================ editor gizmos

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, circleRadius);

            Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, left * viewRadius);
            Gizmos.DrawRay(transform.position, right * viewRadius);
        }
    }
}

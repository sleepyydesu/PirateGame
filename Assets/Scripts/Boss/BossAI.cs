using UnityEngine;
using UnityEngine.AI;
using PirateGame.Combat;
using System;
using Random = UnityEngine.Random;

namespace PirateGame.Enemies
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class BossAI : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Chase,
            Attack,
            PhaseTransition,
            Stagger,
            Dead
        }

        private enum AttackSubPhase
        {
            None,
            Windup,
            Impact,
            Recovery
        }

        [System.Serializable]
        public class BossAttack
        {
            public string name;

            [Tooltip("Animator Trigger parameter. Example: attack_slam")]
            public string animatorTrigger;

            public float windupTime = 0.6f;
            public float recoveryTime = 0.8f;

            public float range = 2.2f;
            public float damage = 20f;

            [Range(0f, 1f)]
            public float weight = 1f;
        }

        [System.Serializable]
        public class BossPhase
        {
            public string phaseName;

            [Range(0f, 1f)]
            [Tooltip("Phase activates when boss HP percentage reaches this value.")]
            public float healthThreshold = 1f;

            public BossAttack[] attacks;

            public float moveSpeedMultiplier = 1f;
            public float attackCooldown = 1.2f;

            [Tooltip("Animator Trigger for phase transition. Example: roar_phase2")]
            public string phaseTransitionTrigger;
        }

        // ================================================================
        // Detection & Movement
        // ================================================================

        [Header("Detection & Movement")]

        [SerializeField]
        private float detectRadius = 20f;

        [SerializeField]
        private float baseWalkSpeed = 3.5f;

        [SerializeField]
        private float baseChaseSpeed = 5.5f;

        [SerializeField]
        private float acceleration = 20f;

        // ================================================================
        // Combat
        // ================================================================

        [Header("Combat")]

        [SerializeField]
        private float hitStaggerTime = 0.4f;

        [SerializeField]
        private float phaseTransitionDuration = 2f;

        // ================================================================
        // Phases
        // ================================================================

        [Header("Phases (ordered highest HP threshold first)")]

        [SerializeField]
        private BossPhase[] phases;

        // ================================================================
        // Animation
        // ================================================================

        [Header("Animation")]

        [SerializeField]
        private Animator animator;

        [Header("Poise/Stagger Resistance")]
        [SerializeField] private float maxPoise = 50f;
        [SerializeField] private float poiseRegenPerSecond = 15f;
        [SerializeField] private float poiseRegenDelay = 1.5f;

        private float currentPoise;
        private float poiseRegenTimer;

        // ================================================================
        // Public properties
        // ================================================================

        public State CurrentState { get; private set; } = State.Idle;

        public bool IsInParryWindow
        {
            get
            {
                return attackSubPhase == AttackSubPhase.Windup;
            }
        }

        // ================================================================
        // Private variables
        // ================================================================

        private NavMeshAgent agent;
        private Health health;

        private Transform player;
        private Health playerHealth;

        private int currentPhaseIndex;

        private BossAttack currentAttack;

        private AttackSubPhase attackSubPhase =
            AttackSubPhase.None;

        private float stateTimer;
        private float attackCooldownTimer;

        // ================================================================
        // Animator hashes
        // ================================================================

        private static readonly int SpeedParam =
            Animator.StringToHash("Speed");

        private static readonly int HitParam =
            Animator.StringToHash("Hit");

        private static readonly int DieParam =
            Animator.StringToHash("Die");

        // ================================================================
        // Current Phase
        // ================================================================

        private BossPhase CurrentPhase
        {
            get
            {
                if (phases == null || phases.Length == 0)
                    return null;

                currentPhaseIndex =
                    Mathf.Clamp(
                        currentPhaseIndex,
                        0,
                        phases.Length - 1
                    );

                return phases[currentPhaseIndex];
            }
        }

        // ================================================================
        // Unity
        // ================================================================

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            currentPoise = maxPoise;
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.OnDamaged += HandleDamaged;
                health.OnDeath += HandleDeath;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamaged -= HandleDamaged;
                health.OnDeath -= HandleDeath;
            }
        }

        private void Start()
        {
            FindPlayer();

            currentPhaseIndex = 0;

            if (phases == null || phases.Length == 0)
            {
                Debug.LogError(
                    $"{name}: BossAI has no phases configured.",
                    this
                );

                return;
            }

            EnterState(State.Idle);
        }

        private void Update()
        {   
            attackCooldownTimer -= Time.deltaTime;

            if (poiseRegenTimer > 0f)
            {
                poiseRegenTimer -= Time.deltaTime;
            }
            else if (currentPoise < maxPoise)
            {
                currentPoise = Math.Min(maxPoise, currentPoise + poiseRegenPerSecond * Time.deltaTime);
            }

            if (CurrentState == State.Dead)
                return;

            if (phases == null || phases.Length == 0)
                return;

            if (player == null &&
                Time.frameCount % 60 == 0)
            {
                FindPlayer();
            }

            switch (CurrentState)
            {
                case State.Idle:
                    TickIdle();
                    break;

                case State.Chase:
                    TickChase();
                    break;

                case State.Attack:
                    TickAttack();
                    break;

                case State.PhaseTransition:
                    TickPhaseTransition();
                    break;

                case State.Stagger:
                    TickStagger();
                    break;
            }

            UpdateAnimator();
        }

        // ================================================================
        // State Machine
        // ================================================================

        private void EnterState(State next)
        {
            CurrentState = next;
            stateTimer = 0f;

            switch (next)
            {
                case State.Idle:

                    if (agent.enabled)
                        agent.isStopped = true;

                    attackSubPhase = AttackSubPhase.None;

                    break;

                case State.Chase:

                    if (agent.enabled)
                    {
                        agent.isStopped = false;

                        float speedMultiplier =
                            CurrentPhase != null
                                ? CurrentPhase.moveSpeedMultiplier
                                : 1f;

                        agent.speed =
                            baseChaseSpeed * speedMultiplier;

                        agent.acceleration = acceleration;
                        agent.stoppingDistance = 1.5f;
                    }

                    attackSubPhase = AttackSubPhase.None;

                    break;

                case State.Attack:

                    if (CurrentPhase == null ||
                        CurrentPhase.attacks == null ||
                        CurrentPhase.attacks.Length == 0)
                    {
                        EnterState(State.Chase);
                        return;
                    }

                    if (agent.enabled)
                        agent.isStopped = true;

                    attackSubPhase =
                        AttackSubPhase.Windup;

                    currentAttack = PickAttack();

                    if (currentAttack != null &&
                        !string.IsNullOrEmpty(
                            currentAttack.animatorTrigger))
                    {
                        SetTrigger(
                            Animator.StringToHash(
                                currentAttack.animatorTrigger
                            )
                        );
                    }

                    break;

                case State.PhaseTransition:

                    if (agent.enabled)
                        agent.isStopped = true;

                    attackSubPhase =
                        AttackSubPhase.None;

                    if (CurrentPhase != null &&
                        !string.IsNullOrEmpty(
                            CurrentPhase.phaseTransitionTrigger))
                    {
                        SetTrigger(
                            Animator.StringToHash(
                                CurrentPhase.phaseTransitionTrigger
                            )
                        );
                    }

                    break;

                case State.Stagger:

                    if (agent.enabled)
                        agent.isStopped = true;

                    attackSubPhase =
                        AttackSubPhase.None;

                    break;

                case State.Dead:

                    if (agent.enabled)
                    {
                        agent.isStopped = true;
                        agent.enabled = false;
                    }

                    attackSubPhase =
                        AttackSubPhase.None;

                    SetTrigger(DieParam);

                    foreach (
                        Collider col
                        in GetComponentsInChildren<Collider>()
                    )
                    {
                        col.enabled = false;
                    }

                    break;
            }
        }

        // ================================================================
        // State Ticks
        // ================================================================

        private void TickIdle()
        {
            if (player == null)
                return;

            float distance =
                Vector3.Distance(
                    transform.position,
                    player.position
                );

            if (distance <= detectRadius)
            {
                EnterState(State.Chase);
            }
        }

        private void TickChase()
        {
            if (player == null)
            {
                EnterState(State.Idle);
                return;
            }

            float distance =
                Vector3.Distance(
                    transform.position,
                    player.position
                );

            FaceTowards(player.position);

            float attackRange = 2f;

            if (CurrentPhase != null &&
                CurrentPhase.attacks != null &&
                CurrentPhase.attacks.Length > 0)
            {
                attackRange = GetMaxAttackRange();
            }

            if (distance <= attackRange)
            {
                if (attackCooldownTimer <= 0f)
                {
                    EnterState(State.Attack);
                    return;
                }
            }

            if (agent.enabled)
            {
                agent.SetDestination(player.position);
            }
        }

        private void TickAttack()
        {
            if (currentAttack == null)
            {
                EnterState(State.Chase);
                return;
            }

            stateTimer += Time.deltaTime;

            if (player != null)
            {
                FaceTowards(player.position);
            }

            switch (attackSubPhase)
            {
                case AttackSubPhase.Windup:

                    if (stateTimer >=
                        currentAttack.windupTime)
                    {
                        DoAttackImpact();
                    }

                    break;

                case AttackSubPhase.Impact:

                    attackSubPhase =
                        AttackSubPhase.Recovery;

                    break;

                case AttackSubPhase.Recovery:

                    if (stateTimer >=
                        currentAttack.windupTime +
                        currentAttack.recoveryTime)
                    {
                        FinishAttack();
                    }

                    break;
            }
        }

        private void TickPhaseTransition()
        {
            stateTimer += Time.deltaTime;

            if (stateTimer >= phaseTransitionDuration)
            {
                EnterState(State.Chase);
            }
        }

        private void TickStagger()
        {
            stateTimer += Time.deltaTime;

            if (stateTimer >= hitStaggerTime)
            {
                if (player != null)
                    EnterState(State.Chase);
                else
                    EnterState(State.Idle);
            }
        }

        // ================================================================
        // Attacks
        // ================================================================

        private BossAttack PickAttack()
        {
            if (CurrentPhase == null ||
                CurrentPhase.attacks == null ||
                CurrentPhase.attacks.Length == 0)
            {
                return null;
            }

            BossAttack[] attacks =
                CurrentPhase.attacks;

            float totalWeight = 0f;

            foreach (BossAttack attack in attacks)
            {
                if (attack != null)
                    totalWeight += Mathf.Max(
                        0f,
                        attack.weight
                    );
            }

            if (totalWeight <= 0f)
            {
                return attacks[0];
            }

            float roll =
                Random.Range(0f, totalWeight);

            float cumulative = 0f;

            foreach (BossAttack attack in attacks)
            {
                if (attack == null)
                    continue;

                cumulative +=
                    Mathf.Max(0f, attack.weight);

                if (roll <= cumulative)
                    return attack;
            }

            return attacks[attacks.Length - 1];
        }

        private float GetMaxAttackRange()
        {
            if (CurrentPhase == null ||
                CurrentPhase.attacks == null ||
                CurrentPhase.attacks.Length == 0)
            {
                return 2f;
            }

            float max = 0f;

            foreach (BossAttack attack
                in CurrentPhase.attacks)
            {
                if (attack != null &&
                    attack.range > max)
                {
                    max = attack.range;
                }
            }

            return max > 0f ? max : 2f;
        }

        private void DoAttackImpact()
        {
            attackSubPhase =
                AttackSubPhase.Impact;

            if (player == null ||
                playerHealth == null ||
                currentAttack == null)
            {
                return;
            }

            float distance =
                Vector3.Distance(
                    transform.position,
                    player.position
                );

            Vector3 toPlayer =
                player.position -
                transform.position;

            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude <= 0.001f)
                return;

            toPlayer.Normalize();

            bool inFront =
                Vector3.Angle(
                    transform.forward,
                    toPlayer
                ) < 80f;

            if (distance <=
                    currentAttack.range * 1.2f &&
                inFront)
            {
                playerHealth.TakeDamage(
                    new DamageInfo(
                        currentAttack.damage,
                        gameObject,
                        player.position
                    )
                );
            }
        }

        private void FinishAttack()
        {
            if (CurrentPhase != null)
            {
                attackCooldownTimer =
                    CurrentPhase.attackCooldown;
            }

            attackSubPhase =
                AttackSubPhase.None;

            EnterState(State.Chase);
        }

        // ================================================================
        // Parry
        // ================================================================

        /// <summary>
        /// Called by the player's parry system.
        /// </summary>
        public void OnParried()
        {
            if (CurrentState != State.Attack)
                return;

            if (!IsInParryWindow)
                return;

            if (CurrentPhase != null)
            {
                attackCooldownTimer =
                    CurrentPhase.attackCooldown;
            }

            EnterState(State.Stagger);
        }

        // ================================================================
        // Damage / Phases
        // ================================================================

        private void HandleDamaged(DamageInfo info)
        {
            if (CurrentState == State.Dead)
                return;

            CheckPhaseTransition();

            poiseRegenTimer = poiseRegenDelay;

            if (CurrentState == State.Attack && IsInParryWindow)
            {
                SetTrigger(HitParam);
                EnterState(State.Stagger);
                currentPoise = maxPoise;
                return;
            }

            currentPoise -= info.Amount;
            if (currentPoise <= 0f)
            {
                SetTrigger(HitParam);
                EnterState(State.Stagger);
                currentPoise = maxPoise;
            }
        }

        private void CheckPhaseTransition()
        {
            if (health == null ||
                phases == null ||
                phases.Length == 0)
            {
                return;
            }

            float healthPercentage =
                health.CurrentHealth /
                Mathf.Max(
                    health.MaxHealth,
                    0.01f
                );

            // Check phases from the next phase onward.
            for (
                int i = currentPhaseIndex + 1;
                i < phases.Length;
                i++
            )
            {
                if (phases[i] == null)
                    continue;

                if (healthPercentage <=
                    phases[i].healthThreshold)
                {
                    currentPhaseIndex = i;

                    EnterState(
                        State.PhaseTransition
                    );

                    return;
                }
            }
        }

        private void HandleDeath()
        {
            if (CurrentState == State.Dead)
                return;

            EnterState(State.Dead);

            Destroy(gameObject, 6f);
        }

        // ================================================================
        // Perception
        // ================================================================

        private void FindPlayer()
        {
            GameObject go =
                GameObject.FindGameObjectWithTag(
                    "Player"
                );

            if (go != null)
            {
                player = go.transform;
                playerHealth =
                    go.GetComponent<Health>();
            }
        }

        // ================================================================
        // Helpers
        // ================================================================

        private void FaceTowards(Vector3 worldPos)
        {
            Vector3 direction =
                worldPos - transform.position;

            direction.y = 0f;

            if (direction.sqrMagnitude < 0.001f)
                return;

            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    8f * Time.deltaTime
                );
        }

        private void SetTrigger(int hash)
        {
            if (animator != null)
            {
                animator.SetTrigger(hash);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null)
                return;

            float velocity = 0f;

            if (agent != null &&
                agent.enabled)
            {
                velocity =
                    agent.velocity.magnitude;
            }

            float normalizedSpeed =
                velocity /
                Mathf.Max(
                    baseChaseSpeed,
                    0.01f
                );

            animator.SetFloat(
                SpeedParam,
                normalizedSpeed,
                0.1f,
                Time.deltaTime
            );
        }

        // ================================================================
        // Gizmos
        // ================================================================

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;

            Gizmos.DrawWireSphere(
                transform.position,
                detectRadius
            );
        }
    }
}
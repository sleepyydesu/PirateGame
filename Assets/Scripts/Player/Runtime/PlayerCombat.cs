using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using PirateGame.Combat;
namespace PirateGame.PlayerV2
{
    public enum PlayerActionKind
    {
        Free,
        Attack,
        Dodge,
        Block,
        Equip,
        Stagger,
        Dead,
        Heal
    }
    [RequireComponent(typeof(Health))]
    public sealed class PlayerCombat : MonoBehaviour, IIncomingDamageFilter
    {
        public PlayerAttackDefinition lightAttack, specialAttack, thrustAttack, specialAttack2;
        [Min(0.05f)] public float dodgeDuration = 0.65f;
        [Min(0f)] public float dodgeDistance = 3.5f;
        [Min(0.1f)] public float dodgeWatchdog = 1.3f;
        [Min(0f)] public float dodgeCooldown = 0.15f;
        [Min(0.01f)] public float maximumInvulnerableSeconds = 0.30f;
        [Range(0f, 1f)] public float blockedDamageFraction = 0.25f;
        [Range(0f, 180f)] public float blockHalfAngle = 60f;
        public float staggerWatchdog = 2f, equipWatchdogPerPart = 3f;
        public UnityEvent onBlocked = new UnityEvent(), onDodged = new UnityEvent();
        [Header("Incoming hurt animations")]
        public HitReactionAnimation[] hurtAnimations = new HitReactionAnimation[0];

        private string selectedHurtPath = "Damage Layer.Player_Damage";

        private float selectedHurtWatchdog = 2f;
        private int selectedHurtLayer = 3;
        private enum KnockdownStage
        {
            Falling,
            Waiting,
            GettingUp
        }

        private HitReactionAnimation selectedHurt;
        [Header("Knockdown protection")]
        public bool invulnerableWhileFalling = true;
        public bool invulnerableWhileDown = true;

        [Min(0f)]
        public float getUpInvulnerableSeconds = 0f;

        private float getUpInvulnerableUntil = float.NegativeInfinity;
        private bool knockdownActive;
        private KnockdownStage knockdownStage;
        [Header("Attack results")]
        public UnityEvent onAttackMissed = new UnityEvent();
        public UnityEvent onConfirmedDamage = new UnityEvent(), onDied = new UnityEvent(), onRevived = new UnityEvent();
        public PlayerInputReader Input { get; private set; }
        public PlayerMotor Motor { get; private set; }
        public PlayerAnimationDriver Animation { get; private set; }
        public PlayerWeaponBridge Weapon { get; private set; }
        public PlayerStateMachine Machine { get; private set; }
        public PlayerActionKind Kind { get; private set; }
        public bool Ready { get; private set; }
        public PlayerFreeState Free { get; private set; }
        public PlayerDodgeState Dodge { get; private set; }
        public PlayerBlockState Block { get; private set; }
        public PlayerEquipState Equip { get; private set; }
        public PlayerStaggerState Stagger { get; private set; }
        public PlayerDeadState Dead { get; private set; }
        public PlayerHealState Healing { get; private set; }
        public PlayerPotion Potion { get; private set; }
        private Health health;
        private PlayerAttackAim attackAim;
        private PlayerAttackState attack;
        private PlayerAttackDefinition queuedFollowup;

        private readonly Dictionary<PlayerAttackDefinition, PlayerAttackDefinition>
            lastFollowups =
                new Dictionary<PlayerAttackDefinition, PlayerAttackDefinition>();
        private bool completion, comboOpen, comboQueued, branch, cancelOpen, committed, recovering;
        private bool invulnerable;
        private float dodgeRemaining;
        private float age, recoveryAge, invulnerableStarted, nextDodgeTime;
        private int equipPart;
        private bool drawTarget;
        private Vector3 dodgeDirection, attackDirection;
        public bool IsFree => Kind == PlayerActionKind.Free;
        public uint AttackSerial { get; private set; }
        public bool IsAttackAnimationVisible => Ready &&
            Kind == PlayerActionKind.Attack && !recovering && !completion &&
            Animation != null && Animation.ActionEntered && !Animation.Faulted;
        public bool IsInAttackWindup => IsAttackAnimationVisible && !committed;
        public bool IsInAttackRecovery => Ready &&
            Kind == PlayerActionKind.Attack && recovering;
        private bool UsesAttackRootTranslation =>
            Kind == PlayerActionKind.Attack &&
            attack != null &&
            attack.Definition.layer == 1 &&
            attack.Definition.useRootTranslation;

        public bool RootTranslation =>
            UsesAttackRootTranslation &&
            !recovering &&
            !completion &&
            Animation.ActionEntered;
        public bool ScriptedMotion => Kind == PlayerActionKind.Dodge;
        public Vector3 ConsumeActionVelocity(float dt)
        {
            if (Kind != PlayerActionKind.Dodge || dt <= 0f) return Vector3.zero;
            float distance = Mathf.Min(dodgeRemaining, (dodgeDistance / Mathf.Max(0.05f, dodgeDuration)) * dt);
            dodgeRemaining -= distance;
            return dodgeDirection * (distance / dt);
        }
        public bool RotateWithMovement =>
            Kind == PlayerActionKind.Free ||
            Kind == PlayerActionKind.Equip ||
            Kind == PlayerActionKind.Heal ||
            (UsesAttackRootTranslation && recovering);
        public float MovementScale =>
            Kind == PlayerActionKind.Heal ? Potion.movementScale :

            Kind == PlayerActionKind.Attack
                ? (UsesAttackRootTranslation
                    ? (recovering ? 1f : 0f)
                    : attack.Definition.movementScale) :

            Kind == PlayerActionKind.Block ? 0.3f :

            Kind == PlayerActionKind.Stagger
                ? (selectedHurtLayer == 0 ? 0f : 0.15f) :

            Kind == PlayerActionKind.Dead ? 0f :

            1f;
        public int VisualLayer =>
            Kind == PlayerActionKind.Heal ? 2 :
            recovering ? -1 :
            Kind == PlayerActionKind.Attack ? attack.Definition.layer :
            Kind == PlayerActionKind.Block ? 2 :
            Kind == PlayerActionKind.Equip ? 2 :
            Kind == PlayerActionKind.Stagger ? selectedHurtLayer :
            Kind == PlayerActionKind.Dodge ? 0 :
            -1;
        public bool Initialize()
        {
            health = GetComponent<Health>(); Input = GetComponent<PlayerInputReader>(); Motor = GetComponent<PlayerMotor>();
            Animation = GetComponent<PlayerAnimationDriver>(); Weapon = GetComponent<PlayerWeaponBridge>();
            attackAim = GetComponent<PlayerAttackAim>();
            Potion = GetComponent<PlayerPotion>();

            if (Potion == null ||
                !Animation.HasState(2, Potion.fullStatePath))
            {
                Debug.LogError(
                    "Add PlayerPotion and its Arms Layer drinking state.",
                    this);

                return false;
            }
            if (lightAttack == null || !ValidateAttack(lightAttack) || !ValidateAttack(specialAttack) ||
                !ValidateAttack(thrustAttack) || !ValidateAttack(specialAttack2)) return false;
            string[] paths = { "Base Layer.Combat_Roll", "Arms Layer.Combat_Defense", "Damage Layer.Player_Damage",
                "Damage Layer.Player_BlockHit", "Arms Layer.PlayerDrawSword1", "Arms Layer.PlayerDrawSword2",
                "Arms Layer.PlayerSheath1", "Arms Layer.PlayerSheath2" };
            foreach (string path in paths)
            {
                int layer = path.StartsWith("Base") ? 0 : path.StartsWith("Combat") ? 1 : path.StartsWith("Arms") ? 2 : 3;
                if (!Animation.HasState(layer, path)) { Debug.LogError("Missing action state: " + path, this); return false; }
            }
            Machine = new PlayerStateMachine();
            if (hurtAnimations != null)
            {
                foreach (HitReactionAnimation entry in hurtAnimations)
                {
                    if (entry == null || !entry.knockdown)
                        continue;

                    if (!entry.fullBody ||
                        string.IsNullOrWhiteSpace(entry.fullStatePath) ||
                        string.IsNullOrWhiteSpace(entry.getUpStatePath) ||
                        !Animation.HasState(0, entry.fullStatePath) ||
                        !Animation.HasState(0, entry.getUpStatePath))
                    {
                        Debug.LogError(
                            "Knockdown setup is incomplete. Enable Full Body " +
                            "and check both Base Layer state paths.",
                            this);

                        return false;
                    }
                }
            }
            Free = new PlayerFreeState(this); Dodge = new PlayerDodgeState(this);
            Block = new PlayerBlockState(this); Equip = new PlayerEquipState(this); Stagger = new PlayerStaggerState(this); Dead = new PlayerDeadState(this);
            Healing = new PlayerHealState(this);
            Ready = true; health.InstallDamageFilter(this);
            health.OnDamaged += Damaged; health.OnDeath += Died; health.OnRevived += Revived;
            Machine.Initialize(health.IsDead ? (PlayerState)Dead : Free); return true;
        }
        private bool ValidateAttack(PlayerAttackDefinition first)
        {
            if (first == null)
                return true;

            var visited = new HashSet<PlayerAttackDefinition>();
            var pending = new Stack<PlayerAttackDefinition>();

            pending.Push(first);

            bool AddLink(
                PlayerAttackDefinition from,
                PlayerAttackDefinition next)
            {
                if (next == null ||
                    next == from ||
                    next.fullStatePath == from.fullStatePath)
                {
                    Debug.LogError(
                        "Invalid follow-up on: " + from.name,
                        this);

                    return false;
                }

                pending.Push(next);
                return true;
            }

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                if (!visited.Add(current))
                    continue;

                if (current.layer < 1 ||
                    current.layer > 2 ||
                    string.IsNullOrWhiteSpace(current.fullStatePath) ||
                    !Animation.HasState(current.layer, current.fullStatePath) ||
                    (current.useRootTranslation && current.layer != 1))
                {
                    Debug.LogError(
                        "Invalid attack state/layer: " + current.name,
                        this);

                    return false;
                }

                if (!current.UsesWeightedFollowups)
                {
                    if (current.followup != null &&
                        !AddLink(current, current.followup))
                    {
                        return false;
                    }

                    continue;
                }

                var targets = new HashSet<PlayerAttackDefinition>();

                foreach (var option in current.weightedFollowups)
                {
                    if (option == null)
                        continue;

                    if (float.IsNaN(option.weight) ||
                        float.IsInfinity(option.weight) ||
                        option.weight < 0f)
                    {
                        Debug.LogError(
                            "Invalid follow-up weight on: " + current.name,
                            this);

                        return false;
                    }

                    if (option.weight == 0f)
                        continue;

                    if (!AddLink(current, option.attack))
                        return false;

                    if (!targets.Add(option.attack))
                    {
                        Debug.LogError(
                            "Duplicate follow-up on: " + current.name,
                            this);

                        return false;
                    }
                }
            }

            return true;
        }
        public void Tick(float dt)
        {
            if (!Ready) return;
            age += dt * Animation.PlaybackScale;
            if (Kind != PlayerActionKind.Dead && !Motor.Grounded &&
                (Kind == PlayerActionKind.Attack ||
                Kind == PlayerActionKind.Block ||
                Kind == PlayerActionKind.Equip ||
                Kind == PlayerActionKind.Heal))
                Machine.Force(Free); // Ground-only actions do not keep dealing damage while falling.
            if (invulnerable && age - invulnerableStarted >= maximumInvulnerableSeconds) invulnerable = false;
            Machine.Tick(dt);
        }
        public void Enter(PlayerActionKind kind)
        {
            if (kind != PlayerActionKind.Stagger)
                knockdownActive = false;
            queuedFollowup = null;
            Kind = kind; age = 0f; completion = comboOpen = comboQueued = branch = cancelOpen = committed = recovering = invulnerable = false;
        }
        public void Cleanup()
        {
            queuedFollowup = null;
            comboQueued = false;
            if (Potion != null)
                Potion.EndDrink();
            Weapon.CancelAttackTracking();

            if (attackAim != null)
                attackAim.EndAttack();

            Weapon.Close();
            invulnerable = comboOpen = cancelOpen = false;
            Animation.Release();
        }
        public void SelectFreeAction()
        {
            if (!Motor.CanUseGroundAction) return;
            if (Input.Has(PlayerCommand.Roll) && Time.time >= nextDodgeTime)
            { Input.Consume(PlayerCommand.Roll); Machine.Request(Dodge, 70); return; }
            if (Input.Consume(PlayerCommand.Heal))
            {
                if (!Motor.Crouched && Potion.CanUse)
                    Machine.Request(Healing, 45);

                return;
            }
            if (Weapon.Drawn)
            {
                if (TryAttack(PlayerCommand.Special2, specialAttack2) || TryAttack(PlayerCommand.Thrust, thrustAttack) ||
                    TryAttack(PlayerCommand.Special, specialAttack) || TryAttack(PlayerCommand.Light, lightAttack)) return;
            }
            if (Input.Consume(PlayerCommand.Equip)) { Machine.Request(Equip, 40); return; }
            if (Weapon.Drawn && Input.Block) Machine.Request(Block, 30);
        }
        private bool TryAttack(PlayerCommand command, PlayerAttackDefinition definition)
        {
            if (definition == null || !Input.Consume(command)) return false;
            Input.ClearAttacks(); RequestAttack(definition); return true;
        }
        private void RequestAttack(PlayerAttackDefinition definition)
        {
            Machine.Request(new PlayerAttackState(this, definition), 50);
        }
        public void ApplyAttackRootMotion(Vector3 delta)
        {
            if (!RootTranslation ||
                attack == null ||
                Motor.HasKnockback)
            {
                return;
            }

            // The motor handles gravity separately.
            delta.y = 0f;

            if (attack.Definition.forceForwardRootMotion)
            {
                Vector3 forward = transform.forward;
                forward.y = 0f;

                if (forward.sqrMagnitude < 0.0001f)
                    return;

                // Keep this frame's movement amount,
                // but send it along the player's forward direction.
                delta = forward.normalized * delta.magnitude;
            }

            Motor.ApplyRootTranslation(delta);
        }
        public void BeginAttack(PlayerAttackState state)
        {
            attack = state;
            unchecked
            {
                AttackSerial++;
            }

            if (AttackSerial == 0)
            {
                AttackSerial = 1;
            }
            if (UsesAttackRootTranslation)
                Motor.StopHorizontalMotion();
            Weapon.BeginAttackTracking();
            //         Debug.Log(
            // $"PLAYER ATTACK START | Serial={AttackSerial}",
            // this);
            attackDirection = Motor.CameraDirection(Vector2.up).normalized;
            if (attackAim != null && attackAim.isActiveAndEnabled)
                attackDirection = attackAim.BeginAttack();
            else
                Motor.Face(attackDirection, 360f, 1f);
            Animation.Begin(state.Definition.layer, state.Definition.fullStatePath);
            Vector3 rayStart = transform.position + Vector3.up;

            // Green: camera direction.
            Debug.DrawRay(
                rayStart,
                Motor.CameraDirection(Vector2.up).normalized * 2f,
                Color.green,
                2f);

            // Blue: player root direction.
            Debug.DrawRay(
                rayStart,
                transform.forward * 1.5f,
                Color.blue,
                2f);
        }
        public void TickAttack(float dt)
        {
            if (cancelOpen && Input.Has(PlayerCommand.Roll) && Time.time >= nextDodgeTime)
            { Input.Consume(PlayerCommand.Roll); Machine.Request(Dodge, 70); return; }
            if (cancelOpen && Input.Block) { Machine.Request(Block, 60); return; }
            if (!committed && !recovering)
            {
                if (attackAim != null && attackAim.isActiveAndEnabled)
                    attackDirection = attackAim.DirectionBeforeCommit();
                Motor.Face(attackDirection, attack.Definition.anticipationTurnRate, dt);
            }
            if (comboOpen) CaptureCombo();
            if (branch)
            {
                branch = false;

                if (!recovering &&
                    !completion &&
                    !Animation.Faulted &&
                    comboQueued &&
                    queuedFollowup != null)
                {
                    var next = queuedFollowup;

                    lastFollowups[attack.Definition] = next;

                    RequestAttack(next);
                    return;
                }
            }
            if (!completion && Animation.ActionFinished)
                EventComplete();
            if (!recovering &&
                (completion ||
                Animation.Faulted ||
                age > attack.Definition.watchdogSeconds))
            {
                // A missing end event or animation fault is not a real miss.
                bool completedNormally = completion && !Animation.Faulted;

                bool missed = completedNormally &&
                              Weapon.FinishAttackTracking();

                if (!completion)
                {
                    Debug.LogWarning(
                        "Attack event/state contract failed: " +
                        attack.Definition.name + "\n" +
                        Animation.DescribeActionState(),
                        this);
                }

                recovering = true;
                recoveryAge = 0f;
                Cleanup();

                if (missed)
                    onAttackMissed.Invoke();
            }
            if (recovering)
            {
                recoveryAge += dt;
                if (recoveryAge >= attack.Definition.recoverySeconds) Machine.Request(Free, 10);
            }
        }
        public void BeginDodge()
        {
            dodgeRemaining = dodgeDistance; Input.ClearAttacks(); nextDodgeTime = Time.time + dodgeDuration + dodgeCooldown;
            dodgeDirection = Motor.WishDirection.sqrMagnitude > 0.001f ? Motor.WishDirection.normalized :
                Motor.CameraDirection(Vector2.up).normalized;
            Motor.Face(dodgeDirection, 100000f, 1f);
            Animation.Begin(0, "Base Layer.Combat_Roll");
        }
        public void TickDodge()
        {
            if (completion || Animation.Faulted || age >= dodgeWatchdog)
            {
                if (!completion) Debug.LogWarning("Dodge requires AE_ActionComplete.", this);
                Machine.Request(Free, 10);
            }
        }
        public void BeginBlock() { Animation.Begin(2, "Arms Layer.Combat_Defense"); }
        public void TickBlock()
        {
            if (Input.Has(PlayerCommand.Roll) && Time.time >= nextDodgeTime)
            { Input.Consume(PlayerCommand.Roll); Machine.Request(Dodge, 70); return; }
            if (Motor.CanUseGroundAction && Weapon.Drawn)
            {
                if (TryAttack(PlayerCommand.Special2, specialAttack2) ||
                    TryAttack(PlayerCommand.Thrust, thrustAttack) ||
                    TryAttack(PlayerCommand.Special, specialAttack) ||
                    TryAttack(PlayerCommand.Light, lightAttack))
                {
                    return;
                }
            }
            if (!Input.Block) Machine.Request(Free, 10);
        }
        public void BeginEquip()
        {
            drawTarget = !Weapon.Drawn; equipPart = 1; PlayEquipPart();
        }
        private void PlayEquipPart()
        {
            completion = false; age = 0f;
            Animation.Begin(2, "Arms Layer." + (drawTarget ? "PlayerDrawSword" : "PlayerSheath") + equipPart);
        }
        public void TickEquip()
        {
            if (Animation.Faulted || age >= equipWatchdogPerPart)
            { Debug.LogWarning("Equip event contract failed; keeping actual attachment state.", this); Machine.Request(Free, 10); return; }
            if (!completion) return;
            if (equipPart == 1) { equipPart = 2; PlayEquipPart(); }
            else
            {
                if (Weapon.Drawn != drawTarget) Debug.LogWarning("Equip completed without AE_Equip handoff.", this);
                Machine.Request(Free, 10);
            }
        }
        private HitReactionAnimation FindHurtAnimation(
            HitReactionType requested)
        {
            HitReactionAnimation fallback = null;

            if (hurtAnimations == null)
                return null;

            foreach (HitReactionAnimation entry in hurtAnimations)
            {
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.fullStatePath) ||
                    !Animation.HasState(
                    entry.fullBody ? 0 : 3,
                    entry.fullStatePath))
                {
                    continue;
                }

                if (entry.type == requested)
                    return entry;

                if (entry.type == HitReactionType.Default)
                    fallback = entry;
            }

            return fallback;
        }
        public void BeginStagger()
        {
            Input.Clear();

            if (selectedHurtLayer == 0)
                Motor.StopHorizontalMotion();

            completion = false;
            age = 0f;

            knockdownStage = KnockdownStage.Falling;
            getUpInvulnerableUntil = float.NegativeInfinity;

            Animation.Begin(selectedHurtLayer, selectedHurtPath);
        }

        public void TickStagger()
        {
            // Keep the normal hurt-animation behaviour.
            if (!knockdownActive)
            {
                if (completion ||
                    Animation.Faulted ||
                    age >= selectedHurtWatchdog)
                {
                    Machine.Request(Free, 10);
                }

                return;
            }

            // Do not save attack, roll, or jump presses while knocked down.
            Input.Clear();

            switch (knockdownStage)
            {
                case KnockdownStage.Falling:
                    {
                        bool finished = Animation.ActionFinished;

                        bool failed =
                            Animation.Faulted ||
                            age >= selectedHurtWatchdog;

                        if (!finished && !failed)
                            return;

                        if (failed && !finished)
                        {
                            Debug.LogWarning(
                                "Knockdown animation timed out or exited early. " +
                                "Check its state, behaviour, and timeout.",
                                this);
                        }

                        // A non-looping clip holds its last lying pose.
                        knockdownStage = KnockdownStage.Waiting;
                        completion = false;
                        age = 0f;
                        return;
                    }

                case KnockdownStage.Waiting:
                    {
                        if (age < Mathf.Max(0f, selectedHurt.downSeconds))
                            return;

                        knockdownStage = KnockdownStage.GettingUp;

                        getUpInvulnerableUntil = Time.time +
                            Mathf.Max(0f, getUpInvulnerableSeconds);

                        completion = false;
                        age = 0f;

                        Animation.Begin(0, selectedHurt.getUpStatePath);
                        return;
                    }

                case KnockdownStage.GettingUp:
                    {
                        bool finished = Animation.ActionFinished;

                        bool failed =
                            Animation.Faulted ||
                            age >= Mathf.Max(
                                0.2f,
                                selectedHurt.getUpWatchdogSeconds);

                        if (!finished && !failed)
                            return;

                        if (failed && !finished)
                        {
                            Debug.LogWarning(
                                "Getting-up animation timed out or exited early. " +
                                "Check its state, behaviour, and timeout.",
                                this);
                        }

                        Input.Clear();
                        Machine.Request(Free, 10);
                        return;
                    }
            }
        }
        public void BeginHeal()
        {
            Input.Clear();

            if (Motor.Crouched ||
                !Motor.CanUseGroundAction ||
                !Potion.BeginDrink())
            {
                Machine.Request(Free, 10);
                return;
            }

            Animation.Begin(2, Potion.fullStatePath);
        }

        public void TickHeal()
        {
            if (Potion == null || !Potion.IsDrinking)
            {
                Machine.Request(Free, 10);
                return;
            }

            if (Animation.ActionEntered)
                Potion.ShowWhenAnimationEntered();

            // Rolling cancels drinking.
            if (Input.Has(PlayerCommand.Roll) &&
                Time.time >= nextDodgeTime)
            {
                Input.Consume(PlayerCommand.Roll);
                Machine.Request(Dodge, 70);
                return;
            }

            Input.ClearAttacks();

            if (completion ||
                Animation.Faulted ||
                age >= Potion.watchdogSeconds)
            {
                if (!completion)
                {
                    Debug.LogWarning(
                        "Potion action ended through its safety check. " +
                        "Check AE_ActionComplete and the Animator state.",
                        this);
                }

                Machine.Request(Free, 10);
            }
        }
        public void EventPotionGrab()
        {
            if (Kind == PlayerActionKind.Heal &&
                !completion &&
                Potion != null)
            {
                Potion.GrabBottle();
            }
        }

        public void EventPotionSip()
        {
            if (Kind == PlayerActionKind.Heal &&
                !completion &&
                Potion != null)
            {
                Potion.TrySip();
            }
        }
        public void BeginDead() { Input.SetGameplayEnabled(false); Input.Clear(); Motor.ResetMotion(); onDied.Invoke(); }
        public void EventCommit()
        {
            if (Kind != PlayerActionKind.Attack) return;
            committed = true;
            if (attackAim != null) attackAim.Commit();
        }
        public void EventHitOn()
        {
            if (Kind != PlayerActionKind.Attack || recovering || completion) return;
            EventCommit();
            Weapon.Open(
            attack.Definition.damage,
            attack.Definition.hitReaction,
            attack.Definition.hitReactionMode);
        }
        public void EventHitOff() { Weapon.Close(); }
        public void EventComplete() { Weapon.Close(); invulnerable = false; completion = true; }
        public void EventCombo(bool open)
        {
            if (Kind != PlayerActionKind.Attack || recovering) return;
            comboOpen = open; if (open) CaptureCombo();
        }
        private void CaptureCombo()
        {
            if (Kind != PlayerActionKind.Attack ||
                attack == null ||
                recovering ||
                completion ||
                comboQueued ||
                !attack.Definition.HasFollowup)
            {
                return;
            }

            if (!Input.Consume(PlayerCommand.Light))
                return;

            lastFollowups.TryGetValue(
                attack.Definition,
                out var previous);

            queuedFollowup =
                attack.Definition.PickFollowup(previous);

            comboQueued = queuedFollowup != null;
        }
        public void EventBranch() { if (Kind == PlayerActionKind.Attack && !recovering) branch = true; }
        public void EventCancel(bool open) { if (Kind == PlayerActionKind.Attack && !recovering) cancelOpen = open; }
        public void EventEquip() { if (Kind == PlayerActionKind.Equip) Weapon.Attach(drawTarget); }
        public void EventInvulnerable(bool open)
        {
            if (Kind != PlayerActionKind.Dodge) return;
            invulnerable = open; if (open) invulnerableStarted = age;
        }
        public bool FilterIncoming(ref DamageInfo info)
        {
            if (!Ready || !isActiveAndEnabled) return true;
            if (Kind == PlayerActionKind.Dead) return false;
            // Protect the player throughout falling, waiting, and getting up.
            if (knockdownActive && Kind == PlayerActionKind.Stagger)
            {
                if (knockdownStage == KnockdownStage.Falling &&
                    invulnerableWhileFalling)
                {
                    return false;
                }

                if (knockdownStage == KnockdownStage.Waiting &&
                    invulnerableWhileDown)
                {
                    return false;
                }

                if (knockdownStage == KnockdownStage.GettingUp &&
                    Time.time < getUpInvulnerableUntil)
                {
                    return false;
                }
            }
            if (Kind == PlayerActionKind.Dodge && invulnerable) { onDodged.Invoke(); return false; }
            if (Kind == PlayerActionKind.Block &&
                (info.HasImpactOrigin || info.Attacker != null))
            {
                Vector3 source = info.HasImpactOrigin
                ? info.ImpactOrigin
                : info.Attacker.transform.position;

                Vector3 direction = source - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, direction) <= blockHalfAngle)
                {
                    float push = info.HasBlockedKnockback
                        ? info.BlockedKnockbackDistance
                        : info.KnockbackDistance * 0.25f;
                    float seconds = info.HasBlockedKnockback
                        ? info.BlockedKnockbackSeconds
                        : info.KnockbackSeconds;
                    if (push > 0f)
                    {
                        Vector3 away = info.KnockbackDirection;
                        if (away.sqrMagnitude < 0.0001f) away = -direction;
                        Motor.ApplyKnockback(away, push, Mathf.Max(0.05f, seconds));
                    }
                    info.Amount *= blockedDamageFraction;
                    info.WasBlocked = true;
                    Animation.BlockImpact();
                    onBlocked.Invoke();
                    return info.Amount > 0f;
                }
            }
            return true;
        }
        private void Damaged(DamageInfo info)
        {
            if (!Ready || info.WasBlocked)
                return;

            onConfirmedDamage.Invoke();

            if (health.IsDead || Kind == PlayerActionKind.Dead)
                return;

            if (info.KnockbackDistance > 0f)
            {
                Motor.ApplyKnockback(
                    info.KnockbackDirection,
                    info.KnockbackDistance,
                    info.KnockbackSeconds);
            }

            HitReactionAnimation reaction =
                FindHurtAnimation(info.Reaction);

            int layer =
                reaction != null && reaction.fullBody ? 0 : 3;

            string path = reaction != null
                ? reaction.fullStatePath
                : "Damage Layer.Player_Damage";

            // Avoid repeatedly restarting an existing hurt animation.
            // A full-body hit can upgrade an upper-body reaction.
            if (Kind == PlayerActionKind.Stagger &&
                (layer != 0 || selectedHurtLayer == 0))
            {
                return;
            }

            if (info.ReactionMode == HitReactionMode.Flinch &&
                layer == 3 &&
                Kind != PlayerActionKind.Stagger &&
                Kind != PlayerActionKind.Heal)
            {
                Animation.HurtImpact(
                    path,
                    reaction != null ? reaction.flinchSeconds : 0.3f);

                return;
            }

            selectedHurtLayer = layer;
            selectedHurtPath = path;
            selectedHurt = reaction;
            knockdownActive = reaction != null && reaction.knockdown;

            selectedHurtWatchdog = reaction != null
                ? Mathf.Max(0.2f, reaction.watchdogSeconds)
                : Mathf.Max(0.2f, staggerWatchdog);

            Cleanup();

            if (Kind == PlayerActionKind.Stagger)
            {
                age = 0f;
                completion = false;
                BeginStagger();
            }
            else
            {
                Machine.Request(Stagger, 90);
            }
        }
        private void Died()
        {
            if (!Ready || Kind == PlayerActionKind.Dead) return;
            Cleanup(); Machine.Force(Dead);
        }
        private void Revived()
        {
            if (!Ready) return;
            Cleanup(); Animation.ResetPresentation(); Motor.ResetMotion(); Input.SetGameplayEnabled(true); Machine.Force(Free); onRevived.Invoke();
        }
        private void OnDisable() { Shutdown(); }
        public void Shutdown()
        {
            if (!Ready) return;
            Cleanup(); Machine.Force(Free); Input.SetGameplayEnabled(false); Ready = false;
            health.OnDamaged -= Damaged; health.OnDeath -= Died; health.OnRevived -= Revived;
            health.RemoveDamageFilter(this);
        }
    }
}

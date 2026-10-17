using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using PirateGame.Combat;
namespace PirateGame.PlayerV2
{
    public enum PlayerActionKind { Free, Attack, Dodge, Block, Equip, Stagger, Dead }
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
        private Health health;
        private PlayerAttackAim attackAim;
        private PlayerAttackState attack;
        private bool completion, comboOpen, comboQueued, branch, cancelOpen, committed, recovering;
        private bool invulnerable;
        private float dodgeRemaining;
        private float age, recoveryAge, invulnerableStarted, nextDodgeTime;
        private int equipPart;
        private bool drawTarget;
        private Vector3 dodgeDirection, attackDirection;
        public bool IsFree => Kind == PlayerActionKind.Free;
        public bool RootTranslation => Kind == PlayerActionKind.Attack && !recovering &&
            attack.Definition.layer == 1 && attack.Definition.useRootTranslation && Animation.ActionEntered;
        public bool ScriptedMotion => Kind == PlayerActionKind.Dodge;
        public Vector3 ConsumeActionVelocity(float dt)
        {
            if (Kind != PlayerActionKind.Dodge || dt <= 0f) return Vector3.zero;
            float distance = Mathf.Min(dodgeRemaining, (dodgeDistance / Mathf.Max(0.05f, dodgeDuration)) * dt);
            dodgeRemaining -= distance;
            return dodgeDirection * (distance / dt);
        }
        public bool RotateWithMovement => Kind == PlayerActionKind.Free || Kind == PlayerActionKind.Equip;
        public float MovementScale => Kind == PlayerActionKind.Attack ? attack.Definition.movementScale :
            Kind == PlayerActionKind.Block ? 0.3f : Kind == PlayerActionKind.Stagger ? 0.15f :
            Kind == PlayerActionKind.Dead ? 0f : 1f;
        public int VisualLayer => recovering ? -1 : Kind == PlayerActionKind.Attack ? attack.Definition.layer :
            Kind == PlayerActionKind.Block ? 2 : Kind == PlayerActionKind.Equip ? 2 :
            Kind == PlayerActionKind.Stagger ? 3 : Kind == PlayerActionKind.Dodge ? 0 : -1;
        public bool Initialize()
        {
            health = GetComponent<Health>(); Input = GetComponent<PlayerInputReader>(); Motor = GetComponent<PlayerMotor>();
            Animation = GetComponent<PlayerAnimationDriver>(); Weapon = GetComponent<PlayerWeaponBridge>();
            attackAim = GetComponent<PlayerAttackAim>();
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
            Machine = new PlayerStateMachine(); Free = new PlayerFreeState(this); Dodge = new PlayerDodgeState(this);
            Block = new PlayerBlockState(this); Equip = new PlayerEquipState(this); Stagger = new PlayerStaggerState(this); Dead = new PlayerDeadState(this);
            Ready = true; health.InstallDamageFilter(this);
            health.OnDamaged += Damaged; health.OnDeath += Died; health.OnRevived += Revived;
            Machine.Initialize(health.IsDead ? (PlayerState)Dead : Free); return true;
        }
        private bool ValidateAttack(PlayerAttackDefinition first)
        {
            var visited = new HashSet<PlayerAttackDefinition>();
            for (var a = first; a != null && visited.Add(a); a = a.followup)
            {
                if (a.layer < 1 || a.layer > 2 || !Animation.HasState(a.layer, a.fullStatePath) ||
                    a.followup == a || (a.followup != null && a.followup.fullStatePath == a.fullStatePath))
                { Debug.LogError("Invalid attack/followup contract: " + a.name, this); return false; }
            }
            return true;
        }
        public void Tick(float dt)
        {
            if (!Ready) return;
            age += dt * Animation.PlaybackScale;
            if (Kind != PlayerActionKind.Dead && !Motor.Grounded &&
                (Kind == PlayerActionKind.Attack || Kind == PlayerActionKind.Block || Kind == PlayerActionKind.Equip))
                Machine.Force(Free); // Ground-only actions do not keep dealing damage while falling.
            if (invulnerable && age - invulnerableStarted >= maximumInvulnerableSeconds) invulnerable = false;
            Machine.Tick(dt);
        }
        public void Enter(PlayerActionKind kind)
        {
            Kind = kind; age = 0f; completion = comboOpen = comboQueued = branch = cancelOpen = committed = recovering = invulnerable = false;
        }
        public void Cleanup()
        {
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
        public void BeginAttack(PlayerAttackState state)
        {
            attack = state;
            Weapon.BeginAttackTracking();
            attackDirection = Motor.CameraDirection(Vector2.up).normalized;
            if (attackAim != null && attackAim.isActiveAndEnabled)
                attackDirection = attackAim.BeginAttack();
            else
                Motor.Face(attackDirection, 360f, 1f);
            Animation.Begin(state.Definition.layer, state.Definition.fullStatePath);
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
                if (comboQueued && attack.Definition.followup != null)
                { RequestAttack(attack.Definition.followup); return; }
            }
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
                        attack.Definition.name,
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
                    !Animation.HasState(3, entry.fullStatePath))
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
            Animation.Begin(3, selectedHurtPath);
        }
        public void TickStagger()
        {
            if (completion ||
                Animation.Faulted ||
                age >= selectedHurtWatchdog)
            {
                Machine.Request(Free, 10);
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
            if (attack.Definition.followup != null && !comboQueued && Input.Consume(PlayerCommand.Light)) comboQueued = true;
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
            if (Kind == PlayerActionKind.Dodge && invulnerable) { onDodged.Invoke(); return false; }
            if (Kind == PlayerActionKind.Block && info.Attacker != null)
            {
                Vector3 direction = info.Attacker.transform.position - transform.position; direction.y = 0f;
                if (direction.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, direction) <= blockHalfAngle)
                {
                    info.Amount *= blockedDamageFraction; info.WasBlocked = true;
                    Animation.BlockImpact(); onBlocked.Invoke(); return info.Amount > 0f;
                }
            }
            return true;
        }
        private void Damaged(DamageInfo info)
        {
            if (!Ready || info.WasBlocked)
                return;

            onConfirmedDamage.Invoke();

            if (health.IsDead ||
                Kind == PlayerActionKind.Dead ||
                Kind == PlayerActionKind.Stagger)
            {
                return;
            }

            HitReactionAnimation reaction =
                FindHurtAnimation(info.Reaction);

            string path = reaction != null
                ? reaction.fullStatePath
                : "Damage Layer.Player_Damage";

            if (info.ReactionMode == HitReactionMode.Flinch)
            {
                float duration = reaction != null
                    ? reaction.flinchSeconds
                    : 0.3f;

                Animation.HurtImpact(path, duration);
                return;
            }

            selectedHurtPath = path;

            selectedHurtWatchdog = reaction != null
                ? Mathf.Max(0.2f, reaction.watchdogSeconds)
                : Mathf.Max(0.2f, staggerWatchdog);

            Cleanup();
            Machine.Request(Stagger, 90);
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

using UnityEngine;
using UnityEngine.InputSystem;
using PirateGame.Combat;
using PirateGame.Enemies;

namespace PirateGame.Player
{
    /// <summary>
    /// TEMPORARY test player used to develop and test the enemy AI.
    /// Replace with the real third-person controller later — the enemies only care
    /// about the "Player" tag and the Health component, so nothing in the AI needs
    /// to change when this is swapped out.
    ///
    /// Controls (Input System):
    ///   WASD / arrows — move (camera-relative)
    ///   Left Shift    — run
    ///   Left Mouse    — test attack (damages the nearest enemy in front)
    ///   Space         — test PARRY (staggers an enemy that is mid-windup in range)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SimplePlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float runSpeed = 7f;
        [SerializeField] private float rotationSpeed = 12f;
        [SerializeField] private float gravity = -20f;

        [Header("Test Combat")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackDamage = 25f;
        [SerializeField] private float attackCooldown = 0.5f;
        [SerializeField] private float parryRange = 2.5f;

        private CharacterController controller;
        private float verticalVelocity;
        private float attackTimer;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            Move();
            attackTimer -= Time.deltaTime;

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && attackTimer <= 0f)
            {
                attackTimer = attackCooldown;
                TryAttack();
            }
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                TryParry();
            }
        }

        private void Move()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            input = Vector2.ClampMagnitude(input, 1f);

            // Camera-relative movement direction.
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 forward = cam != null ? cam.forward : Vector3.forward;
            Vector3 right = cam != null ? cam.right : Vector3.right;
            forward.y = 0f; right.y = 0f;
            forward.Normalize(); right.Normalize();

            Vector3 move = forward * input.y + right * input.x;
            float speed = keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;

            // Gravity / grounding.
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = move * speed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            // Face movement direction.
            if (move.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                    rotationSpeed * Time.deltaTime);
            }
        }

        /// <summary>Very rough melee swing for testing enemy hit reactions & death.</summary>
        private void TryAttack()
        {
            EnemyAI target = FindEnemy(attackRange, requireInFront: true);
            if (target != null && target.TryGetComponent(out Health enemyHealth))
            {
                enemyHealth.TakeDamage(new DamageInfo(attackDamage, gameObject,
                    target.transform.position));
            }
        }

        /// <summary>
        /// Test parry: if an enemy in range is mid-windup (parry window open),
        /// stagger it. This is the exact call the real parry system should make.
        /// </summary>
        private void TryParry()
        {
            EnemyAI target = FindEnemy(parryRange, requireInFront: false);
            if (target != null && target.IsInParryWindow)
            {
                target.OnParried();
            }
        }

        private EnemyAI FindEnemy(float range, bool requireInFront)
        {
            EnemyAI best = null;
            float bestDist = float.MaxValue;

            foreach (var enemy in FindObjectsByType<EnemyAI>())
            {
                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist > range || dist >= bestDist) continue;

                if (requireInFront)
                {
                    Vector3 dir = (enemy.transform.position - transform.position).normalized;
                    if (Vector3.Angle(transform.forward, dir) > 70f) continue;
                }

                best = enemy;
                bestDist = dist;
            }
            return best;
        }
    }
}

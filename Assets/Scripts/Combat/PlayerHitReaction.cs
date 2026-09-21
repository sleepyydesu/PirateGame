using UnityEngine;
using PirateGame.Combat;

[RequireComponent(typeof(Health))]
public class PlayerHitReaction : MonoBehaviour
{
    [SerializeField] PlayerController playerController;
    Health health;
    Animator animator;

    void Awake()
    {
        health = GetComponent<Health>();
        animator = GetComponent<Animator>();
        if (playerController == null) playerController = GetComponent<PlayerController>();
    }

    void OnEnable()
    {
        health.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        health.OnDamaged -= HandleDamaged;
    }

    void HandleDamaged(DamageInfo info)
    {
        if (health.IsDead) return;

        bool isBlocking = playerController.movementSM.currentState == playerController.defending;
        animator.SetTrigger(isBlocking ? "blockHit" : "damage");
    }
}
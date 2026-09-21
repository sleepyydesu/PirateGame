using UnityEngine;
using PirateGame.Combat;

public class PlayerDeathHandler : MonoBehaviour
{
    [SerializeField] Health playerHealth;
    [SerializeField] GameObject ragdollPrefab;
    [SerializeField] PlayerController playerController;

    void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDeath += HandleDeath;
        }
    }

    void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDeath -= HandleDeath;
        }
    }

    void HandleDeath()
    {
        // Disable player control immediately
        if (playerController != null)
        {
            playerController.enabled = false;
        }

        // Spawn ragdoll
        if (ragdollPrefab != null)
        {
            Instantiate(ragdollPrefab, transform.position, transform.rotation);
        }

        // Hide the animated character
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }

        // Disable the character controller/collider so it doesn't block anything
        var controller = GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
    }
}
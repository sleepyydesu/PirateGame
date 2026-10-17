using UnityEngine;
namespace PirateGame.PlayerV2
{
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class PlayerDeathPresentationV2 : MonoBehaviour
    {
        [SerializeField] private GameObject ragdollPrefab;
        private PlayerCombat combat;
        private GameObject ragdoll;
        private Renderer[] originalRenderers;
        private bool[] visibleBeforeDeath;
        private bool controllerWasEnabled, animatorWasEnabled;
        private void Awake() { combat = GetComponent<PlayerCombat>(); }
        private void OnEnable() { combat.onDied.AddListener(Die); combat.onRevived.AddListener(Revive); }
        private void OnDisable() { combat.onDied.RemoveListener(Die); combat.onRevived.RemoveListener(Revive); }
        private void Die()
        {
            originalRenderers = GetComponentsInChildren<Renderer>(true);
            visibleBeforeDeath = new bool[originalRenderers.Length];
            for (int i = 0; i < originalRenderers.Length; i++)
            { visibleBeforeDeath[i] = originalRenderers[i].enabled; originalRenderers[i].enabled = false; }
            var cc = GetComponent<CharacterController>(); controllerWasEnabled = cc.enabled; cc.enabled = false;
            var a = GetComponent<Animator>(); animatorWasEnabled = a.enabled; a.enabled = false;
            if (ragdollPrefab != null) ragdoll = Instantiate(ragdollPrefab, transform.position, transform.rotation);
            // This copies root pose only. Bone-pose transfer is a separate presentation improvement.
        }
        private void Revive()
        {
            if (originalRenderers == null) return;
            if (ragdoll != null) Destroy(ragdoll);
            for (int i = 0; i < originalRenderers.Length; i++)
                if (originalRenderers[i] != null) originalRenderers[i].enabled = visibleBeforeDeath[i];
            GetComponent<CharacterController>().enabled = controllerWasEnabled;
            GetComponent<Animator>().enabled = animatorWasEnabled;
        }
    }
}

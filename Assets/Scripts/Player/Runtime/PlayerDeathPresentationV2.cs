using System.Collections.Generic;
using UnityEngine;

namespace PirateGame.PlayerV2
{
    [RequireComponent(typeof(PlayerCombat))]
    public sealed class PlayerDeathPresentationV2 : MonoBehaviour
    {
        [SerializeField] private GameObject ragdollPrefab;
        private PlayerCombat combat;
        private CharacterController characterController;
        private Animator animator;
        private GameObject ragdoll;
        private Renderer[] originalRenderers;
        private bool[] visibleBeforeDeath;
        private bool controllerWasEnabled, animatorWasEnabled;

        private void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            characterController = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
        }

        private void OnEnable() { combat.onDied.AddListener(Die); combat.onRevived.AddListener(Revive); }
        private void OnDisable() { combat.onDied.RemoveListener(Die); combat.onRevived.RemoveListener(Revive); }

        private void Die()
        {
            originalRenderers = GetComponentsInChildren<Renderer>(true);
            visibleBeforeDeath = new bool[originalRenderers.Length];
            for (int i = 0; i < originalRenderers.Length; i++)
            {
                visibleBeforeDeath[i] = originalRenderers[i].enabled;
                originalRenderers[i].enabled = false;
            }

            if (characterController != null)
            {
                controllerWasEnabled = characterController.enabled;
                characterController.enabled = false;
            }

            if (animator != null)
            {
                animatorWasEnabled = animator.enabled;
                animator.enabled = false;
            }

            if (ragdollPrefab == null)
            {
                Debug.LogError($"{nameof(PlayerDeathPresentationV2)} on {name} has no ragdoll prefab assigned.", this);
                return;
            }

            ragdoll = Instantiate(ragdollPrefab, transform.position, transform.rotation);
            MatchRagdollPose(ragdoll);
            Rigidbody headBody = MakeHeadFollowTorso(ragdoll);
            AlignJointAnchors(ragdoll);

            Physics.SyncTransforms();
            Rigidbody[] ragdollBodies = ragdoll.GetComponentsInChildren<Rigidbody>(true);
            if (ragdollBodies.Length == 0)
            {
                Debug.LogError($"Ragdoll prefab '{ragdollPrefab.name}' has no Rigidbody components.", ragdoll);
                return;
            }

            foreach (Rigidbody body in ragdollBodies)
            {
                if (body == headBody) continue;
                body.useGravity = true;
                body.isKinematic = false;
                body.WakeUp();
            }
        }

        private void Revive()
        {
            if (originalRenderers == null) return;
            if (ragdoll != null) Destroy(ragdoll);
            for (int i = 0; i < originalRenderers.Length; i++)
                if (originalRenderers[i] != null) originalRenderers[i].enabled = visibleBeforeDeath[i];

            if (characterController != null) characterController.enabled = controllerWasEnabled;
            if (animator != null) animator.enabled = animatorWasEnabled;
        }

        private static Rigidbody MakeHeadFollowTorso(GameObject ragdollInstance)
        {
            Transform head = null;
            foreach (Transform bone in ragdollInstance.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name != "head") continue;
                if (head != null)
                {
                    Debug.LogWarning($"Ragdoll prefab '{ragdollInstance.name}' has multiple bones named 'head'; head physics was left unchanged.", ragdollInstance);
                    return null;
                }

                head = bone;
            }

            if (head == null)
            {
                Debug.LogWarning($"Ragdoll prefab '{ragdollInstance.name}' has no bone named 'head'; head physics was left unchanged.", ragdollInstance);
                return null;
            }

            CharacterJoint joint = head.GetComponent<CharacterJoint>();
            if (joint != null) Destroy(joint);

            Rigidbody body = head.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.isKinematic = true;
                body.Sleep();
            }

            foreach (Collider headCollider in head.GetComponents<Collider>())
                headCollider.enabled = false;

            return body;
        }

        private static void AlignJointAnchors(GameObject ragdollInstance)
        {
            foreach (CharacterJoint joint in ragdollInstance.GetComponentsInChildren<CharacterJoint>(true))
            {
                if (joint.connectedBody == null) continue;

                Vector3 worldAnchor = joint.transform.TransformPoint(joint.anchor);
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedAnchor = joint.connectedBody.transform.InverseTransformPoint(worldAnchor);
            }
        }

        private void MatchRagdollPose(GameObject ragdollInstance)
        {
            var sourceBones = new Dictionary<string, Transform>();
            var duplicateSourceNames = new HashSet<string>();
            foreach (Transform sourceBone in GetComponentsInChildren<Transform>(true))
            {
                if (sourceBones.ContainsKey(sourceBone.name))
                    duplicateSourceNames.Add(sourceBone.name);
                else
                    sourceBones.Add(sourceBone.name, sourceBone);
            }

            var matchedTargetNames = new HashSet<string>();
            foreach (Transform ragdollBone in ragdollInstance.GetComponentsInChildren<Transform>(true))
            {
                if (duplicateSourceNames.Contains(ragdollBone.name) ||
                    matchedTargetNames.Contains(ragdollBone.name) ||
                    !sourceBones.TryGetValue(ragdollBone.name, out Transform sourceBone))
                    continue;

                ragdollBone.SetPositionAndRotation(sourceBone.position, sourceBone.rotation);
                matchedTargetNames.Add(ragdollBone.name);
            }
        }
    }
}

using UnityEngine;
namespace PirateGame.PlayerV2
{
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimationDriver : MonoBehaviour
    {
        public static readonly int Speed = Animator.StringToHash("speed"); // Preserve existing lowercase parameter.
        public const string BaseLocomotion = "Base Layer.Locomotion_Blend_Tree";
        public const string CombatLocomotion = "Combat Layer.Combat_Blend_Tree";
        public const string ArmsDefault = "Arms Layer.Default", DamageDefault = "Damage Layer.Default";
        private Animator animator;
        private readonly int[] lastRequested = new int[4];
        private int expectedHash, expectedLayer = -1;
        private float blockPulseUntil, landPoseUntil;
        private bool entered, exited;
        private float hurtPulseUntil;
        public bool Faulted => exited;
        public bool ActionEntered => entered && !exited && expectedHash != 0;
        public bool ActionFinished
        {
            get
            {
                if (animator == null ||
                    expectedLayer < 0 ||
                    !ActionEntered)
                {
                    return false;
                }

                AnimatorStateInfo state =
                    animator.GetCurrentAnimatorStateInfo(expectedLayer);

                return state.fullPathHash == expectedHash &&
                       !state.loop &&
                       state.normalizedTime >= 1f;
            }
        }
        public float PlaybackScale => animator == null ? 1f : Mathf.Max(0f, animator.speed);
        public bool HasState(int layer, string path) => animator != null && layer < animator.layerCount &&
            animator.HasState(layer, Animator.StringToHash(path));
        private void Awake() { 
            animator = GetComponent<Animator>(); 
            animator.applyRootMotion = true;
            }
        public bool Validate()
        {
            if (animator.runtimeAnimatorController == null || animator.layerCount != 4) return false;
            string[] names = { "Base Layer", "Combat Layer", "Arms Layer", "Damage Layer" };
            for (int i = 0; i < 4; i++) if (animator.GetLayerName(i) != names[i]) return false;
            bool speed = false;
            foreach (var p in animator.parameters) if (p.nameHash == Speed && p.type == AnimatorControllerParameterType.Float) speed = true;
            return speed && HasState(0, BaseLocomotion) && HasState(1, CombatLocomotion) &&
                HasState(2, ArmsDefault) && HasState(3, DamageDefault);
        }
        public void Ensure(int layer, string path, float blend = 0.08f)
        {
            int hash = Animator.StringToHash(path);
            if (lastRequested[layer] == hash) return;
            lastRequested[layer] = hash; animator.CrossFadeInFixedTime(hash, blend, layer, 0f);
        }
        public void Begin(int layer, string path)
        {
            if (layer == 0 || layer == 3)
            {
                blockPulseUntil = 0f;
                hurtPulseUntil = 0f;
            }
            expectedLayer = layer; expectedHash = Animator.StringToHash(path); entered = exited = false;
            lastRequested[layer] = expectedHash;
            animator.CrossFadeInFixedTime(expectedHash, 0.06f, layer, 0f);
        }
        public void ResetPresentation()
        {
            System.Array.Clear(lastRequested, 0, lastRequested.Length);
            blockPulseUntil = landPoseUntil = hurtPulseUntil = 0f; Release();
        }
        public void Release()
        {
            expectedHash = 0; expectedLayer = -1; entered = exited = false;
            // The next free visual update deliberately parks unused action layers.
        }
        public void StateEntered(int layer, int hash) { if (layer == expectedLayer && hash == expectedHash) entered = true; }
        public void StateExited(int layer, int hash) { if (layer == expectedLayer && hash == expectedHash && entered) exited = true; }
        public bool Accept(AnimationEvent e) => isActiveAndEnabled && e.isFiredByAnimator && entered && !exited &&
            expectedHash != 0 && e.animatorStateInfo.fullPathHash == expectedHash;
        public void Visuals(
    bool drawn,
    bool airborne,
    bool crouched,
    bool sprintJump,
    bool rolling,
    int actionLayer,
    float normalizedSpeed,
    float dt)
        {
            animator.SetFloat(Speed, normalizedSpeed, 0.08f, dt);

            bool baseAction = actionLayer == 0;

            float combat =
                !rolling && !airborne && !crouched &&
                (drawn || actionLayer == 1) ? 1f : 0f;

            float arms =
                !rolling && !airborne && !crouched &&
                actionLayer == 2 ? 1f : 0f;

            float damage =
                actionLayer == 3 ||
                Time.time < blockPulseUntil ||
                Time.time < hurtPulseUntil ? 1f : 0f;

            if (baseAction)
            {
                animator.SetLayerWeight(1, 0f);
                animator.SetLayerWeight(2, 0f);
                animator.SetLayerWeight(3, 0f);
            }
            else
            {
                SetWeight(1, combat, dt);
                SetWeight(2, arms, dt);
                SetWeight(3, damage, dt);
            }

            if (!rolling && !baseAction)
            {
                Ensure(
                    0,
                    airborne
                        ? (sprintJump
                            ? "Base Layer.Player_SprintJump"
                            : "Base Layer.Player_Jump")
                        : crouched
                            ? "Base Layer.Crouch_Blend_Tree"
                            : Time.time < landPoseUntil &&
                              normalizedSpeed < 0.1f
                                ? "Base Layer.Player_Land"
                                : BaseLocomotion);
            }

            if (actionLayer != 1)
                Ensure(1, CombatLocomotion);

            if (actionLayer != 2)
                Ensure(2, ArmsDefault);

            if (actionLayer != 3 &&
                Time.time >= blockPulseUntil &&
                Time.time >= hurtPulseUntil)
            {
                Ensure(3, DamageDefault);
            }
        }
        public void Landing(float impact) { if (impact >= 7.5f) landPoseUntil = Time.time + 0.1f; }
        public void HurtImpact(string fullStatePath, float duration)
        {
            if (!HasState(3, fullStatePath))
                return;

            blockPulseUntil = 0f;
            hurtPulseUntil = Time.time + Mathf.Max(0.05f, duration);

            int hash = Animator.StringToHash(fullStatePath);
            lastRequested[3] = hash;

            animator.CrossFadeInFixedTime(
                hash,
                0.04f,
                3,
                0f);
        }
        public void BlockImpact()
        {
            blockPulseUntil = Time.time + 0.25f;
            int hash = Animator.StringToHash("Damage Layer.Player_BlockHit");
            lastRequested[3] = hash; animator.CrossFadeInFixedTime(hash, 0.035f, 3, 0f);
        }
        private void SetWeight(int layer, float target, float dt)
        {
            animator.SetLayerWeight(layer, target > 0f ? 1f : Mathf.MoveTowards(animator.GetLayerWeight(layer), 0f, dt * 15f));
        }
    }
}

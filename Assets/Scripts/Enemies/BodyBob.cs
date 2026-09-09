using UnityEngine;

namespace PirateGame.Enemies
{
    /// <summary>
    /// Adds the up-and-down of a footstep to a walk or run.
    ///
    /// WHY THIS EXISTS: on a humanoid rig you cannot author hip height. Unity's
    /// HumanPoseHandler rebuilds it from the leg pose and throws away any hip
    /// translation in the clip - measured, moving the mapped Hips bone by 100mm
    /// changed the baked pose by 0mm. The leg pose alone only yields about 8mm of
    /// rise and fall, where a real walk is nearer 40mm, and that shortfall is what
    /// makes a character look like it is gliding while its legs move.
    ///
    /// So the dip is applied here instead, in LateUpdate, after the Animator has
    /// written the pose - a small offset on the hip bone that the rest of the
    /// skeleton inherits.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class BodyBob : MonoBehaviour
    {
        [Tooltip("How far the hips dip at their lowest, in metres, at full running speed.")]
        [SerializeField] private float runDip = 0.075f;

        [Tooltip("How far the hips dip when walking.")]
        [SerializeField] private float walkDip = 0.035f;

        [Tooltip("Animator float that carries normalised movement speed (0 idle, 0.5 walk, 1 run).")]
        [SerializeField] private string speedParameter = "Speed";

        [Tooltip("Footfalls per animation loop. Both the walk and run clips are one full stride, so 2.")]
        [SerializeField] private int stepsPerCycle = 2;

        private Animator animator;
        private Transform hips;
        private Vector3 hipsRestPosition;
        private int speedHash;
        private bool ready;

        private bool failed;

        /// <summary>
        /// Set up on the first LateUpdate rather than in Awake: the Animator does not
        /// report isHuman / resolve its bone map until it has initialised, so doing
        /// this in Awake silently disabled the component.
        /// </summary>
        private bool EnsureReady()
        {
            if (ready) return true;
            if (failed) return false;

            if (animator == null) animator = GetComponent<Animator>();
            if (speedHash == 0) speedHash = Animator.StringToHash(speedParameter);

            if (animator.avatar == null || !animator.isHuman) return false;   // retry next frame

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                Debug.LogWarning("BodyBob: no Hips bone on " + name + " - disabling.", this);
                failed = true;
                return false;
            }

            hipsRestPosition = hips.localPosition;
            ready = true;
            return true;
        }

        private void LateUpdate()
        {
            if (!EnsureReady()) return;

            float speed = HasSpeedParameter() ? animator.GetFloat(speedHash) : 0f;
            if (speed <= 0.02f)
            {
                hips.localPosition = hipsRestPosition;
                return;
            }

            // Blend the dip in as the character moves: nothing when idle, walkDip at
            // the walk threshold, runDip at a full sprint.
            float dip = speed <= 0.5f
                ? Mathf.Lerp(0f, walkDip, speed / 0.5f)
                : Mathf.Lerp(walkDip, runDip, (speed - 0.5f) / 0.5f);

            // Phase comes from the clip itself, so the dip lands on the footfalls
            // rather than drifting against them.
            float cycle = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float phase = cycle * Mathf.PI * 2f * stepsPerCycle;

            // 0 at the top of the step, -dip at the bottom. Never lifts the character
            // above the pose the animation already put it in.
            float offset = (Mathf.Cos(phase) - 1f) * 0.5f * dip;

            hips.localPosition = hipsRestPosition + Vector3.up * offset;
        }

        private bool HasSpeedParameter()
        {
            foreach (var p in animator.parameters)
                if (p.nameHash == speedHash) return true;
            return false;
        }
    }
}

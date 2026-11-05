using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Keeps the rider's hands on the handlebar grips (Humanoid IK, OnAnimatorIK on the base layer).
    /// Targets are the sled's Grip.L/R (or Grip2.L/R for the Tandem passenger) plus the wrist offset measured
    /// from Sit_Idle, so IK only corrects drift (steering, brake pull, bumps). States tagged "NoIK"
    /// (push, lift, win, lose, wave) fade the weight out so the hands can leave the bar.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class RiderIK : MonoBehaviour
    {
        public const string NoIKTag = "NoIK";

        public Transform LeftGrip;
        public Transform RightGrip;
        [Tooltip("Hand (wrist) position relative to the grip, in grip space (from the Sit_Idle pose).")]
        public Vector3 LeftOffset = new Vector3(0.007f, 0.057f, -0.07f);
        public Vector3 RightOffset = new Vector3(-0.007f, 0.057f, -0.07f);
        [Range(0f, 1f)] public float MaxWeight = 1f;
        [Tooltip("Weight change per second when entering/leaving NoIK states.")]
        public float BlendSpeed = 6f;

        private Animator animator;
        private float weight = 1f;

        public float CurrentWeight => weight;

        private void Awake() { animator = GetComponent<Animator>(); }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || animator == null || !animator.isHuman) return;
            bool noIk = animator.GetCurrentAnimatorStateInfo(0).IsTag(NoIKTag)
                        || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag(NoIKTag));
            float target = noIk ? 0f : MaxWeight;
            weight = Mathf.MoveTowards(weight, target, BlendSpeed * Time.deltaTime);

            Set(AvatarIKGoal.LeftHand, LeftGrip, LeftOffset);
            Set(AvatarIKGoal.RightHand, RightGrip, RightOffset);
        }

        private void Set(AvatarIKGoal goal, Transform grip, Vector3 offset)
        {
            if (grip == null)
            {
                animator.SetIKPositionWeight(goal, 0f);
                return;
            }
            animator.SetIKPositionWeight(goal, weight);
            animator.SetIKPosition(goal, grip.TransformPoint(offset));
        }

        /// <summary>World position the hand is pulled to (debug / tests).</summary>
        public Vector3 GetTarget(bool left) => left
            ? (LeftGrip != null ? LeftGrip.TransformPoint(LeftOffset) : transform.position)
            : (RightGrip != null ? RightGrip.TransformPoint(RightOffset) : transform.position);
    }
}

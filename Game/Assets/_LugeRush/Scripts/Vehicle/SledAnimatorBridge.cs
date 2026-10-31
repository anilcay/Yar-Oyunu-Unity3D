using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Feeds the rider Animator (see ARCHITECTURE.md, animation contract) from the SledController.
    /// Does nothing when there is no Animator or a parameter is missing.
    /// </summary>
    public class SledAnimatorBridge : MonoBehaviour
    {
        [SerializeField] private SledController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private float damping = 0.08f;

        private static readonly int SteerId = Animator.StringToHash("Steer");
        private static readonly int BrakeId = Animator.StringToHash("Brake");
        private static readonly int Speed01Id = Animator.StringToHash("Speed01");
        private static readonly int DriftId = Animator.StringToHash("Drift");
        private static readonly int OnLiftId = Animator.StringToHash("OnLift");
        private static readonly int PushId = Animator.StringToHash("Push");
        private static readonly int BumpId = Animator.StringToHash("Bump");
        private static readonly int WinId = Animator.StringToHash("Win");
        private static readonly int LoseId = Animator.StringToHash("Lose");

        private bool hasSteer, hasBrake, hasSpeed, hasDrift, hasLift, hasPush, hasBump, hasWin, hasLose;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<SledController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheParameters();
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.Bumped += OnBumped;
            controller.BoostStarted += OnBoost;
        }

        private void OnDisable()
        {
            if (controller == null) return;
            controller.Bumped -= OnBumped;
            controller.BoostStarted -= OnBoost;
        }

        /// <summary>Call after the rider model / Animator is swapped at runtime.</summary>
        public void SetAnimator(Animator newAnimator)
        {
            animator = newAnimator;
            CacheParameters();
        }

        private void CacheParameters()
        {
            hasSteer = hasBrake = hasSpeed = hasDrift = hasLift = hasPush = hasBump = hasWin = hasLose = false;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                int h = p.nameHash;
                hasSteer |= h == SteerId; hasBrake |= h == BrakeId; hasSpeed |= h == Speed01Id;
                hasDrift |= h == DriftId; hasLift |= h == OnLiftId; hasPush |= h == PushId;
                hasBump |= h == BumpId; hasWin |= h == WinId; hasLose |= h == LoseId;
            }
        }

        private void Update()
        {
            if (animator == null || controller == null) return;
            var input = controller.ControlsLocked ? SledInput.None : controller.LastInput;
            float dt = Time.deltaTime;
            if (hasSteer) animator.SetFloat(SteerId, input.Steer, damping, dt);
            if (hasBrake) animator.SetFloat(BrakeId, input.Brake, damping, dt);
            if (hasSpeed) animator.SetFloat(Speed01Id, controller.Speed01, damping, dt);
            if (hasDrift) animator.SetBool(DriftId, controller.IsDrifting);
            if (hasLift) animator.SetBool(OnLiftId, controller.IsOnLift);
        }

        private void OnBumped(Collision c) { if (animator != null && hasBump) animator.SetTrigger(BumpId); }
        private void OnBoost(SledBoostKind kind) { if (kind == SledBoostKind.Start) TriggerPush(); }

        public void TriggerPush() { if (animator != null && hasPush) animator.SetTrigger(PushId); }
        public void TriggerWin() { if (animator != null && hasWin) animator.SetTrigger(WinId); }
        public void TriggerLose() { if (animator != null && hasLose) animator.SetTrigger(LoseId); }
    }
}

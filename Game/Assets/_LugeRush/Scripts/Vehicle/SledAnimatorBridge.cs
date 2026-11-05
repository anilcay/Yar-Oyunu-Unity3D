using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Feeds the rider Animator(s) (see ARCHITECTURE.md, animation contract) from the SledController.
    /// Supports several riders (Tandem driver + passenger). Missing parameters are skipped.
    /// If the controller has a layer named "Brake", its weight follows the (smoothed) brake input.
    /// </summary>
    public class SledAnimatorBridge : MonoBehaviour
    {
        public const string BrakeLayerName = "Brake";

        [SerializeField] private SledController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private float damping = 0.08f;
        [SerializeField] private float brakeLayerSpeed = 8f;

        private static readonly int SteerId = Animator.StringToHash("Steer");
        private static readonly int BrakeId = Animator.StringToHash("Brake");
        private static readonly int Speed01Id = Animator.StringToHash("Speed01");
        private static readonly int DriftId = Animator.StringToHash("Drift");
        private static readonly int OnLiftId = Animator.StringToHash("OnLift");
        private static readonly int PushId = Animator.StringToHash("Push");
        private static readonly int BumpId = Animator.StringToHash("Bump");
        private static readonly int WinId = Animator.StringToHash("Win");
        private static readonly int LoseId = Animator.StringToHash("Lose");

        private class Target
        {
            public Animator Animator;
            public bool Steer, Brake, Speed, Drift, Lift, Push, Bump, Win, Lose;
            public int BrakeLayer = -1;
        }

        private readonly List<Target> targets = new List<Target>();
        private float brakeWeight;

        public IReadOnlyList<Animator> Animators
        {
            get
            {
                var list = new List<Animator>();
                foreach (var t in targets) list.Add(t.Animator);
                return list;
            }
        }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<SledController>();
            Rebind();
        }

        private void OnEnable()
        {
            if (controller == null) controller = GetComponent<SledController>();
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
            targets.Clear();
            Add(newAnimator);
        }

        /// <summary>Re-collects every rider Animator under this sled (after SledVariant swapped the model).</summary>
        public void Rebind()
        {
            targets.Clear();
            var all = GetComponentsInChildren<Animator>(true);
            if (all.Length == 0 && animator != null) Add(animator);
            foreach (var a in all) Add(a);
            animator = targets.Count > 0 ? targets[0].Animator : null;
        }

        private void Add(Animator a)
        {
            if (a == null) return;
            var t = new Target { Animator = a };
            if (a.runtimeAnimatorController != null)
            {
                foreach (var p in a.parameters)
                {
                    int h = p.nameHash;
                    t.Steer |= h == SteerId; t.Brake |= h == BrakeId; t.Speed |= h == Speed01Id;
                    t.Drift |= h == DriftId; t.Lift |= h == OnLiftId; t.Push |= h == PushId;
                    t.Bump |= h == BumpId; t.Win |= h == WinId; t.Lose |= h == LoseId;
                }
                t.BrakeLayer = a.GetLayerIndex(BrakeLayerName);
            }
            targets.Add(t);
        }

        private void Update()
        {
            if (controller == null || targets.Count == 0) return;
            var input = controller.ControlsLocked ? SledInput.None : controller.LastInput;
            float dt = Time.deltaTime;
            bool brakeAllowed = !controller.IsDrifting && !controller.IsOnLift;
            brakeWeight = Mathf.MoveTowards(brakeWeight, brakeAllowed ? input.Brake : 0f, brakeLayerSpeed * dt);
            foreach (var t in targets)
            {
                var a = t.Animator;
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                if (t.Steer) a.SetFloat(SteerId, input.Steer, damping, dt);
                if (t.Brake) a.SetFloat(BrakeId, input.Brake, damping, dt);
                if (t.Speed) a.SetFloat(Speed01Id, controller.Speed01, damping, dt);
                if (t.Drift) a.SetBool(DriftId, controller.IsDrifting);
                if (t.Lift) a.SetBool(OnLiftId, controller.IsOnLift);
                if (t.BrakeLayer >= 0) a.SetLayerWeight(t.BrakeLayer, brakeWeight);
            }
        }

        private void Fire(int id, System.Func<Target, bool> has)
        {
            foreach (var t in targets)
                if (t.Animator != null && t.Animator.isActiveAndEnabled && has(t)) t.Animator.SetTrigger(id);
        }

        private void OnBumped(Collision c) => Fire(BumpId, t => t.Bump);
        private void OnBoost(SledBoostKind kind) { if (kind == SledBoostKind.Start) TriggerPush(); }

        public void TriggerPush() => Fire(PushId, t => t.Push);
        public void TriggerWin() => Fire(WinId, t => t.Win);
        public void TriggerLose() => Fire(LoseId, t => t.Lose);
    }
}

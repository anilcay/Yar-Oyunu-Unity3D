using System;
using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Vehicle
{
    public enum SledBoostKind { Start, DriftMini, DriftSuper, Slipstream }

    /// <summary>
    /// Rigidbody based arcade sled physics. Four suspension rays keep the sled glued to the track and
    /// aligned with its slope/banking; gravity along the slope provides all propulsion.
    /// The physics root carries this component; visuals live under a separate "SledVisual" child.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(0)]
    public class SledController : MonoBehaviour
    {
        /// <summary>All active sleds (used for slipstream and by track systems).</summary>
        public static readonly List<SledController> All = new List<SledController>();

        public SledStats Stats;

        [Header("Probing")]
        [SerializeField] private LayerMask groundMask = 1 << 8;   // "Track"
        [SerializeField] private LayerMask barrierMask = 1 << 10; // "Barrier"
        [Tooltip("Half width (x) and half length (z) of the suspension probe rectangle.")]
        [SerializeField] private Vector2 probeHalfExtents = new Vector2(0.38f, 0.75f);
        [SerializeField] private float probeStartHeight = 0.5f;
        [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.12f, 0f);
        [Tooltip("Minimum relative speed for a collision to raise Bumped.")]
        [SerializeField] private float bumpEventMinSpeed = 1.5f;

        private const float LiftExitGhostTime = 0.6f;

        private Rigidbody rb;
        private bool frozen;
        private bool grounded;
        private Vector3 groundNormal = Vector3.up;
        private float driftCharge;
        private float driftGrace;
        private float slipCharge;
        private float slipCooldown;
        private float boostTimer;
        private float boostAccel;
        private float startPenaltyTimer;
        private float liftExitGhost;
        private readonly Vector3[] probeOffsets = new Vector3[4];

        public ISledInputSource InputSource { get; set; }
        public Rigidbody Body => rb;

        public float Speed => rb != null && !rb.isKinematic ? rb.linearVelocity.magnitude : kinematicSpeed;
        public float SpeedKmh => Speed * 3.6f;
        public float Speed01 => Stats != null ? Mathf.Clamp01(Speed / Stats.MaxSpeed) : 0f;
        public float DriftCharge01 => driftCharge;
        /// <summary>0 = none, 1 = mini boost ready, 2 = super boost ready.</summary>
        public int DriftLevel => driftCharge >= 1f ? 2 : driftCharge >= 0.5f ? 1 : 0;
        public float SlipstreamCharge01 => slipCharge;
        public bool InSlipstream { get; private set; }
        public bool IsBoosting => boostTimer > 0f;
        public bool IsDrifting { get; private set; }
        public bool IsOnLift { get; private set; }
        public bool IsGrounded => grounded;
        /// <summary>Half width (x) / half length (z) of the suspension probe rectangle (per sled model).</summary>
        public Vector2 ProbeHalfExtents
        {
            get => probeHalfExtents;
            set { probeHalfExtents = value; UpdateProbeOffsets(); }
        }
        public Vector3 CenterOfMass
        {
            get => centerOfMass;
            set { centerOfMass = value; if (rb != null) rb.centerOfMass = value; }
        }
        public bool IsScrapingBarrier { get; private set; }
        public Vector3 GroundNormal => groundNormal;
        public bool ControlsLocked { get; set; }
        public SledInput LastInput { get; private set; }
        /// <summary>Multiplier on air drag (used by AI rubber banding; 1 = neutral).</summary>
        public float ExternalDragMultiplier { get; set; } = 1f;
        /// <summary>Hover height of the root above the ground at rest (visual offset, spawn placement).</summary>
        public float RestHoverHeight => Stats != null ? Stats.RestHoverHeight : 0.15f;

        /// <summary>Frozen sleds are kinematic and ignore input (grid during countdown).</summary>
        public bool Frozen
        {
            get => frozen;
            set
            {
                frozen = value;
                ApplyKinematicState();
            }
        }

        public event Action<Collision> Bumped;
        public event Action PushPressed;
        public event Action<SledBoostKind> BoostStarted;

        private float kinematicSpeed;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (InputSource == null) InputSource = GetComponent<ISledInputSource>();
            ConfigureBody();
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>Applies Stats to the Rigidbody and colliders. Call again after swapping Stats.</summary>
        public void ConfigureBody()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            // Swap the art model / colliders when the sled type changed (menu selection, random bots).
            if (Stats != null && TryGetComponent(out SledVariant variant)) variant.ApplyType(Stats.Type);
            if (Stats != null) rb.mass = Stats.Mass;
            rb.useGravity = true;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.5f;
            rb.maxAngularVelocity = 20f;
            rb.maxDepenetrationVelocity = 5f; // overlapping respawns must not launch the sled
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.centerOfMass = centerOfMass;

            var slick = new PhysicsMaterial("SledSlick")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0.05f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var col in GetComponentsInChildren<Collider>()) col.sharedMaterial = slick;
            UpdateProbeOffsets();
        }

        private void UpdateProbeOffsets()
        {
            probeOffsets[0] = new Vector3(-probeHalfExtents.x, probeStartHeight, probeHalfExtents.y);
            probeOffsets[1] = new Vector3(probeHalfExtents.x, probeStartHeight, probeHalfExtents.y);
            probeOffsets[2] = new Vector3(-probeHalfExtents.x, probeStartHeight, -probeHalfExtents.y);
            probeOffsets[3] = new Vector3(probeHalfExtents.x, probeStartHeight, -probeHalfExtents.y);
        }

        private void FixedUpdate()
        {
            var raw = InputSource != null ? InputSource.ReadInput() : SledInput.None;
            LastInput = raw;
            if (raw.Push) PushPressed?.Invoke();

            if (liftExitGhost > 0f)
            {
                liftExitGhost -= Time.fixedDeltaTime;
                if (liftExitGhost <= 0f) rb.excludeLayers = 0;
            }

            if (frozen || IsOnLift || Stats == null) return;

            float dt = Time.fixedDeltaTime;
            if (rb.linearVelocity.sqrMagnitude > Stats.MaxSpeed * Stats.MaxSpeed * 4f)
            {
                Debug.LogWarning($"[SledController] {name} velocity spike {rb.linearVelocity.magnitude:0} m/s grounded={grounded} lift={IsOnLift} pos={rb.position}");
                ClampSpeed();
            }
            var input = ControlsLocked ? SledInput.None : raw;
            if (startPenaltyTimer > 0f)
            {
                startPenaltyTimer -= dt;
                input.Brake = 1f;
                input.Drift = false;
            }

            UpdateGround();
            if (grounded) DriveGrounded(input, dt);
            else DriveAirborne(dt);

            UpdateSlipstream(dt);
            ApplyDragAndBoost(dt);
            ClampSpeed();
            IsScrapingBarrier = false; // re-set by OnCollisionStay during the next physics step
        }

        private void UpdateGround()
        {
            Vector3 up = transform.up;
            float rayLength = probeStartHeight + Stats.SuspensionLength;
            Vector3 normalSum = Vector3.zero;
            int hits = 0;

            for (int i = 0; i < probeOffsets.Length; i++)
            {
                Vector3 origin = transform.TransformPoint(probeOffsets[i]);
                if (!Physics.Raycast(origin, -up, out RaycastHit hit, rayLength, groundMask, QueryTriggerInteraction.Ignore))
                    continue;

                hits++;
                normalSum += hit.normal;
                float compression = rayLength - hit.distance;
                float pointSpeed = Vector3.Dot(rb.GetPointVelocity(origin), up);
                float accel = compression * Stats.SuspensionStiffness - pointSpeed * Stats.SuspensionDamping;
                if (accel > 0f) rb.AddForceAtPosition(up * (accel * rb.mass), origin, ForceMode.Force);
            }

            grounded = hits >= 2;
            if (hits > 0) groundNormal = normalSum.normalized;
        }

        private void DriveGrounded(SledInput input, float dt)
        {
            Vector3 n = groundNormal;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
            Vector3 right = Vector3.Cross(n, fwd);
            Vector3 v = rb.linearVelocity;
            float vF = Vector3.Dot(v, fwd);
            float vR = Vector3.Dot(v, right);
            float g = Mathf.Abs(Physics.gravity.y);

            // Arcade slope pull: amplify the along-slope part of gravity.
            Vector3 gAlong = Vector3.ProjectOnPlane(Physics.gravity, n);
            rb.AddForce(gAlong * (Stats.SlopeGravityMultiplier - 1f), ForceMode.Acceleration);

            UpdateDrift(input, Mathf.Abs(vF), dt);

            // Rolling resistance + brake (never reverses direction).
            float brake = input.Brake * (IsDrifting ? Stats.DriftBrakeScale : 1f);
            float decel = Stats.RollingResistance * g + brake * Stats.BrakeForce;
            float dv = Mathf.Min(Mathf.Abs(vF), decel * dt) * Mathf.Sign(vF);
            rb.AddForce(-fwd * dv, ForceMode.VelocityChange);

            // Lateral grip: bleed sideways velocity.
            float grip = Stats.GripLateral * (IsDrifting ? Stats.DriftGripMultiplier : 1f);
            rb.AddForce(-right * (vR * Mathf.Clamp01(grip * dt)), ForceMode.VelocityChange);

            // Steering: speed dependent yaw rate around the ground normal.
            float yawRate = Stats.SteerRate * Mathf.Deg2Rad / (1f + Stats.SteerSpeedFalloff * Mathf.Abs(vF));
            if (IsDrifting) yawRate *= Stats.DriftSteerMultiplier;
            float lowSpeed = Mathf.Clamp01(Mathf.Abs(vF) / 3f);
            float targetYaw = Mathf.Clamp(input.Steer, -1f, 1f) * yawRate * lowSpeed * (vF < -0.5f ? -1f : 1f);

            Vector3 av = rb.angularVelocity;
            float yaw = Vector3.Dot(av, n);
            float newYaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-Stats.SteerResponse * dt));
            av += n * (newYaw - yaw);

            // Align up axis with the ground (slope + banking).
            av += AlignTorque(n, av, Stats.AlignStrength, Stats.AlignDamping) * dt;
            rb.angularVelocity = av;
        }

        private void DriveAirborne(float dt)
        {
            rb.AddForce(Physics.gravity * (Stats.AirGravityMultiplier - 1f), ForceMode.Acceleration);
            Vector3 av = rb.angularVelocity;
            av += AlignTorque(groundNormal, av, Stats.AlignStrength * 0.3f, Stats.AlignDamping * 0.5f) * dt;
            rb.angularVelocity = av;
            if (IsDrifting) EndDrift(false);
        }

        private Vector3 AlignTorque(Vector3 targetUp, Vector3 angularVelocity, float strength, float damping)
        {
            Vector3 axis = Vector3.Cross(transform.up, targetUp);
            Vector3 nonYaw = angularVelocity - targetUp * Vector3.Dot(angularVelocity, targetUp);
            return axis * strength - nonYaw * damping;
        }

        private void UpdateDrift(SledInput input, float forwardSpeed, float dt)
        {
            bool wantDrift = (input.Drift || input.Brake > 0.5f) && Mathf.Abs(input.Steer) > 0.4f && forwardSpeed > Stats.DriftMinSpeed;
            if (wantDrift)
            {
                IsDrifting = true;
                driftGrace = 0.15f;
            }
            else if (IsDrifting)
            {
                driftGrace -= dt;
                if (driftGrace <= 0f) EndDrift(true);
            }

            if (IsDrifting) driftCharge = Mathf.Min(1f, driftCharge + Stats.DriftChargeRate * Mathf.Abs(input.Steer) * dt);
        }

        private void EndDrift(bool grantBoost)
        {
            IsDrifting = false;
            if (grantBoost && !ControlsLocked)
            {
                if (driftCharge >= 1f) StartBoost(Stats.BoostForce, Stats.BoostDuration, SledBoostKind.DriftSuper);
                else if (driftCharge >= 0.5f) StartBoost(Stats.BoostForce * Stats.MiniBoostScale, Stats.BoostDuration * Stats.MiniBoostScale, SledBoostKind.DriftMini);
            }
            driftCharge = 0f;
        }

        private void UpdateSlipstream(float dt)
        {
            InSlipstream = false;
            if (grounded && Speed > 8f)
            {
                Vector3 n = groundNormal;
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
                Vector3 right = Vector3.Cross(n, fwd);
                Vector3 pos = rb.position;
                foreach (var other in All)
                {
                    if (other == this || other.IsOnLift || other.rb == null) continue;
                    Vector3 rel = other.rb.position - pos;
                    float along = Vector3.Dot(rel, fwd);
                    if (along < Stats.SlipstreamMinDistance || along > Stats.SlipstreamMaxDistance) continue;
                    if (Mathf.Abs(Vector3.Dot(rel, right)) > Stats.SlipstreamHalfWidth) continue;
                    if (Mathf.Abs(Vector3.Dot(rel, n)) > 2f) continue;
                    InSlipstream = true;
                    break;
                }
            }

            // Drag reduction applies whenever drafting; the boost meter only fills outside the cooldown.
            float rate = 1f / Mathf.Max(0.1f, Stats.SlipstreamChargeTime);
            if (slipCooldown > 0f)
            {
                slipCooldown -= dt;
                slipCharge = 0f;
                return;
            }
            slipCharge = InSlipstream ? slipCharge + rate * dt : Mathf.Max(0f, slipCharge - rate * Stats.SlipstreamDecayRate * dt);
            if (slipCharge >= 1f)
            {
                slipCharge = 0f;
                slipCooldown = Stats.SlipstreamCooldown;
                StartBoost(Stats.SlipstreamBoostForce, Stats.SlipstreamBoostDuration, SledBoostKind.Slipstream);
            }
        }

        private void ApplyDragAndBoost(float dt)
        {
            Vector3 v = rb.linearVelocity;
            float drag = Stats.DragCoefficient * ExternalDragMultiplier * (InSlipstream ? 1f - Stats.SlipstreamDragReduction : 1f);
            rb.AddForce(-v * (v.magnitude * drag), ForceMode.Acceleration);

            if (boostTimer > 0f)
            {
                boostTimer -= dt;
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, groundNormal).normalized;
                rb.AddForce(fwd * boostAccel, ForceMode.Acceleration);
            }
        }

        private void ClampSpeed()
        {
            float cap = Stats.MaxSpeed * Stats.SpeedCapMultiplier;
            Vector3 v = rb.linearVelocity;
            if (v.sqrMagnitude > cap * cap) rb.linearVelocity = v.normalized * cap;
        }

        private void StartBoost(float accel, float duration, SledBoostKind kind)
        {
            if (boostTimer > 0f && boostAccel > accel)
            {
                boostTimer = Mathf.Max(boostTimer, duration);
            }
            else
            {
                boostAccel = accel;
                boostTimer = Mathf.Max(boostTimer, duration);
            }
            BoostStarted?.Invoke(kind);
        }

        /// <summary>Start push result: quality in 0..1 grants a boost, negative = false start penalty.</summary>
        public void ApplyStartPush(float quality)
        {
            if (Stats == null) return;
            if (quality < 0f)
            {
                startPenaltyTimer = 0.6f;
                return;
            }
            if (quality <= 0f) return;
            StartBoost(Stats.StartBoostForce * quality, Stats.StartBoostDuration, SledBoostKind.Start);
        }

        /// <summary>Teleports the sled and clears all transient state.</summary>
        public void ResetTo(Vector3 pos, Quaternion rot)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            IsDrifting = false;
            driftCharge = 0f;
            slipCharge = 0f;
            slipCooldown = 0f;
            boostTimer = 0f;
            startPenaltyTimer = 0f;
            groundNormal = rot * Vector3.up;
        }

        /// <summary>Gives a dynamic sled an initial velocity (after reset or release).</summary>
        public void SetVelocity(Vector3 velocity)
        {
            if (rb != null && !rb.isKinematic) rb.linearVelocity = velocity;
        }

        // ---------- Lift ----------

        public void AttachToLift()
        {
            if (IsOnLift) return;
            IsOnLift = true;
            kinematicSpeed = Speed;
            EndDrift(false);
            boostTimer = 0f;
            slipCharge = 0f;
            ApplyKinematicState();
            // A kinematic rider sweeping through a sled would launch it: lift riders never touch other sleds.
            liftExitGhost = 0f;
            rb.excludeLayers = 1 << gameObject.layer;
        }

        /// <summary>Called by the lift every FixedUpdate while attached.</summary>
        public void MoveOnLift(Vector3 pos, Quaternion rot, float speed)
        {
            kinematicSpeed = speed;
            rb.MovePosition(pos);
            rb.MoveRotation(rot);
        }

        public void DetachFromLift(Vector3 releaseVelocity)
        {
            if (!IsOnLift) return;
            IsOnLift = false;
            ApplyKinematicState();
            SetVelocity(releaseVelocity);
            liftExitGhost = LiftExitGhostTime; // the next rider is released about one sled length behind this one
        }

        private void ApplyKinematicState()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            bool kinematic = frozen || IsOnLift;
            if (rb.isKinematic == kinematic) return;
            if (kinematic)
            {
                kinematicSpeed = rb.linearVelocity.magnitude;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                kinematicSpeed = 0f;
            }
        }

        // ---------- Collisions ----------

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody != null && collision.rigidbody.TryGetComponent(out SledController other) && !rb.isKinematic)
            {
                Vector3 dir = Vector3.ProjectOnPlane(rb.position - other.rb.position, groundNormal);
                if (dir.sqrMagnitude > 1e-4f)
                {
                    dir.Normalize();
                    float closing = Mathf.Max(0f, -Vector3.Dot(rb.linearVelocity - other.rb.linearVelocity, dir));
                    float massRatio = 2f * other.rb.mass / (rb.mass + other.rb.mass);
                    rb.AddForce(dir * Mathf.Min(closing, 12f) * Stats.CollisionKnockbackScale * massRatio, ForceMode.VelocityChange);
                }
            }

            if (collision.relativeVelocity.magnitude >= bumpEventMinSpeed) Bumped?.Invoke(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (rb.isKinematic || Stats == null) return;
            if (((1 << collision.gameObject.layer) & barrierMask) == 0) return;
            IsScrapingBarrier = true;
            rb.linearVelocity *= 1f - Mathf.Clamp01(Stats.BarrierFriction * Time.fixedDeltaTime);
        }
    }
}

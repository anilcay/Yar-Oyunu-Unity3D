using UnityEngine;

namespace LugeRush.Vehicle
{
    public enum SledType { Classic = 0, Sport = 1, Tandem = 2, Mini = 3 }

    /// <summary>
    /// All tunable sled physics in one place. Forces are expressed as accelerations (m/s^2) so that
    /// mass only matters for collisions (light sleds get knocked around more).
    /// </summary>
    [CreateAssetMenu(menuName = "Luge Rush/Sled Stats", fileName = "SledStats")]
    public class SledStats : ScriptableObject
    {
        [Header("Identity")]
        public SledType Type = SledType.Classic;
        public string DisplayName = "Orman";

        [Header("UI bars (1..5)")]
        [Range(1, 5)] public int UiSpeed = 3;
        [Range(1, 5)] public int UiGrip = 3;
        [Range(1, 5)] public int UiHandling = 3;
        [Range(1, 5)] public int UiWeight = 3;
        [Range(1, 5)] public int UiDrift = 3;

        [Header("Body")]
        [Tooltip("Rigidbody mass in kg (sled + rider).")]
        public float Mass = 110f;

        [Header("Speed")]
        [Tooltip("Reference top speed (m/s). Drag is tuned so terminal speed on ReferenceSlope equals this.")]
        public float MaxSpeed = 24f;
        [Tooltip("Hard safety cap = MaxSpeed * this (boosts and steep sections can exceed MaxSpeed a bit).")]
        public float SpeedCapMultiplier = 1.3f;
        [Tooltip("Arcade multiplier on the along-slope component of gravity.")]
        public float SlopeGravityMultiplier = 2.5f;
        [Tooltip("Rolling resistance coefficient (fraction of g).")]
        public float RollingResistance = 0.015f;
        [Tooltip("Air drag: deceleration = DragCoefficient * v^2 (1/m).")]
        public float DragCoefficient = 0.0029f;

        [Header("Handling")]
        [Tooltip("How fast sideways velocity is removed (1/s).")]
        public float GripLateral = 8f;
        [Tooltip("Yaw rate at low speed (deg/s).")]
        public float SteerRate = 100f;
        [Tooltip("Yaw rate = SteerRate / (1 + SteerSpeedFalloff * speed).")]
        public float SteerSpeedFalloff = 0.04f;
        [Tooltip("How quickly the actual yaw rate follows the requested one (1/s).")]
        public float SteerResponse = 10f;
        [Tooltip("Braking deceleration at full brake (m/s^2).")]
        public float BrakeForce = 6f;

        [Header("Drift")]
        public float DriftMinSpeed = 9f;
        [Tooltip("Lateral grip multiplier while drifting.")]
        public float DriftGripMultiplier = 0.35f;
        [Tooltip("Yaw rate multiplier while drifting.")]
        public float DriftSteerMultiplier = 1.3f;
        [Tooltip("Brake input is scaled by this while drifting (drift scrubs, doesn't stop).")]
        public float DriftBrakeScale = 0.25f;
        [Tooltip("Drift meter fill per second at full steer (meter 0..1; 0.5 = mini boost, 1 = super boost).")]
        public float DriftChargeRate = 0.45f;

        [Header("Boost")]
        [Tooltip("Forward acceleration while boosting (m/s^2).")]
        public float BoostForce = 4.5f;
        public float BoostDuration = 1.2f;
        [Tooltip("Mini (level 1) drift boost strength/duration relative to super boost.")]
        public float MiniBoostScale = 0.55f;
        public float StartBoostForce = 6f;
        public float StartBoostDuration = 1.0f;

        [Header("Slipstream")]
        public float SlipstreamMinDistance = 2f;
        public float SlipstreamMaxDistance = 10f;
        public float SlipstreamHalfWidth = 1.5f;
        [Tooltip("Fraction of air drag removed while in slipstream.")]
        public float SlipstreamDragReduction = 0.45f;
        [Tooltip("Seconds in slipstream to fill the meter.")]
        public float SlipstreamChargeTime = 3f;
        [Tooltip("Meter drain speed outside the slipstream, relative to the fill rate.")]
        public float SlipstreamDecayRate = 1f;
        [Tooltip("Seconds after a slipstream boost before the meter can fill again (drag reduction still applies).")]
        public float SlipstreamCooldown = 10f;
        public float SlipstreamBoostForce = 3f;
        public float SlipstreamBoostDuration = 0.8f;

        [Header("Collisions")]
        [Tooltip("Extra separating velocity on sled-sled contacts, as a fraction of closing speed (weighted by mass ratio).")]
        public float CollisionKnockbackScale = 0.6f;
        [Tooltip("Speed bleed while scraping a barrier (1/s).")]
        public float BarrierFriction = 0.9f;

        [Header("Suspension / ground stick")]
        public float SuspensionLength = 0.2f;
        [Tooltip("Per-corner spring (acceleration per metre of compression).")]
        public float SuspensionStiffness = 60f;
        public float SuspensionDamping = 7f;
        [Tooltip("Torque gain aligning the sled to the ground normal.")]
        public float AlignStrength = 30f;
        public float AlignDamping = 6f;
        [Tooltip("Extra gravity while airborne so small hops come back down quickly.")]
        public float AirGravityMultiplier = 1.8f;

        /// <summary>Hover height of the physics root above the ground at rest.</summary>
        public float RestHoverHeight => Mathf.Max(0.02f, SuspensionLength - Mathf.Abs(Physics.gravity.y) / (4f * Mathf.Max(1f, SuspensionStiffness)));

        /// <summary>Drag coefficient that makes terminal speed equal <paramref name="maxSpeed"/> on a slope (rise/run).</summary>
        public static float DragForTerminalSpeed(float maxSpeed, float slope, float slopeGravityMultiplier, float rollingResistance)
        {
            float g = 9.81f;
            float sin = slope / Mathf.Sqrt(1f + slope * slope);
            float net = g * sin * slopeGravityMultiplier - rollingResistance * g;
            return Mathf.Max(0.0001f, net / (maxSpeed * maxSpeed));
        }
    }
}

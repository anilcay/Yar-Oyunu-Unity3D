using System;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.AI
{
    /// <summary>
    /// Bot driver. Precomputes a racing line (inside offset scaled by curvature) and a target speed profile
    /// (yaw-rate limited corner speed + backward braking pass) for its own SledStats, then each physics step
    /// does pure-pursuit steering towards a speed dependent look-ahead point, brakes to the profile,
    /// optionally drifts tight corners, avoids/overtakes sleds ahead and applies light rubber banding.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class AIDriver : MonoBehaviour, ISledInputSource
    {
        public AIDifficulty Difficulty;
        public TrackPath Track;

        [Header("Line")]
        [SerializeField] private float edgeMargin = 0.75f;
        [Tooltip("Curvature (1/m) at which the full inside offset is used.")]
        [SerializeField] private float curvatureForFullOffset = 0.05f;
        [SerializeField] private float lineSmoothing = 12f;
        [Tooltip("Fraction of the max yaw rate the planner assumes is available in corners.")]
        [SerializeField] private float steerUsage = 0.8f;

        [Header("Avoidance")]
        [SerializeField] private float avoidLookAhead = 12f;
        [SerializeField] private float avoidLateral = 1.3f;
        [SerializeField] private float avoidShiftSpeed = 2.5f;

        [Header("Recovery")]
        [SerializeField] private float stuckSpeed = 0.8f;
        [SerializeField] private float stuckTime = 2.5f;

        /// <summary>Metres this sled is ahead (+) / behind (-) the rubber band reference (usually the player).</summary>
        public Func<float> GapProvider;
        /// <summary>Per-bot skill variation, e.g. -0.03..0.03 (applied to corner speed).</summary>
        public float SkillBias;

        private SledController sled;
        private TrackFollower follower;
        private float[] lineOffset;
        private float[] targetSpeed;
        private float[] curvature;
        private float step;
        private float avoidOffset;
        private float wanderPhase;
        private bool wasLocked = true;
        private float pushAt = -1f;
        private float stuckTimer;
        private bool driftThisCorner;
        private bool goForSuper;
        private float cornerSign;
        private bool inCorner;
        private System.Random rng;

        public float TargetSpeed { get; private set; }
        public bool ProfilesBuilt => targetSpeed != null;

        private void Awake()
        {
            sled = GetComponent<SledController>();
            follower = GetComponent<TrackFollower>();
            if (Track == null) Track = FindFirstObjectByType<TrackPath>();
            rng = new System.Random(GetInstanceID());
            wanderPhase = (float)rng.NextDouble() * 100f;
        }

        private void Start() { BuildProfiles(); }

        /// <summary>Rebuild line + speed profile (call after changing Stats or Difficulty).</summary>
        public void BuildProfiles()
        {
            if (Track == null || sled == null || sled.Stats == null || Difficulty == null) return;
            Track.EnsureBuilt();
            int n = Track.SampleCount;
            step = Track.SampleStep;
            var stats = sled.Stats;
            curvature = new float[n];
            lineOffset = new float[n];
            targetSpeed = new float[n];

            for (int k = 0; k < n; k++) curvature[k] = Track.GetCurvature(k * step);

            // Racing line: inside offset from smoothed curvature.
            int win = Mathf.Max(1, Mathf.RoundToInt(lineSmoothing / step));
            for (int k = 0; k < n; k++)
            {
                float sum = 0f;
                for (int o = -win; o <= win; o++) sum += curvature[(k + o + n) % n];
                float kSmooth = sum / (2 * win + 1);
                float half = Track.GetWidth(k * step) * 0.5f - edgeMargin;
                lineOffset[k] = Mathf.Clamp(kSmooth / curvatureForFullOffset, -1f, 1f) * Mathf.Max(0f, half) * Difficulty.LineAccuracy;
            }

            // Corner speed from the yaw-rate limit: v / R <= S / (1 + f v)  =>  f v^2 + v - R S <= 0.
            float S = stats.SteerRate * Mathf.Deg2Rad * steerUsage;
            float f = Mathf.Max(1e-4f, stats.SteerSpeedFalloff);
            float cornerFactor = Mathf.Max(0.3f, Difficulty.CornerSpeedFactor + SkillBias);
            float vCap = stats.MaxSpeed * stats.SpeedCapMultiplier;
            for (int k = 0; k < n; k++)
            {
                float kAbs = 0f;
                for (int o = -2; o <= 2; o++) kAbs = Mathf.Max(kAbs, Mathf.Abs(curvature[(k + o + n) % n]));
                float R = 1f / Mathf.Max(kAbs, 1e-4f);
                float v = (-1f + Mathf.Sqrt(1f + 4f * f * R * S)) / (2f * f);
                targetSpeed[k] = Mathf.Min(vCap, v * cornerFactor);
            }

            // Backward pass: make sure we can brake down to every upcoming corner speed.
            float a = stats.BrakeForce * Difficulty.BrakeEfficiency;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = n - 1; k >= 0; k--)
                {
                    float next = targetSpeed[(k + 1) % n];
                    float allowed = Mathf.Sqrt(next * next + 2f * a * step);
                    if (allowed < targetSpeed[k]) targetSpeed[k] = allowed;
                }
            }
        }

        private float SampleArray(float[] arr, float distance)
        {
            float s = Track.Wrap(distance) / step;
            int i0 = Mathf.FloorToInt(s) % arr.Length;
            int i1 = (i0 + 1) % arr.Length;
            return Mathf.Lerp(arr[i0], arr[i1], s - Mathf.Floor(s));
        }

        public SledInput ReadInput()
        {
            var input = SledInput.None;
            if (Track == null || sled == null || follower == null || !follower.IsValid) return input;
            if (targetSpeed == null) BuildProfiles();
            if (targetSpeed == null) return input;

            float dt = Time.fixedDeltaTime;
            bool locked = sled.ControlsLocked || sled.Frozen;

            // Start push shortly after GO.
            if (wasLocked && !locked && !sled.IsOnLift)
                pushAt = Time.time + Mathf.Max(0f, Difficulty.PushReaction + ((float)rng.NextDouble() - 0.5f) * Difficulty.PushReactionSpread);
            wasLocked = locked;
            if (pushAt > 0f && Time.time >= pushAt)
            {
                input.Push = true;
                pushAt = -1f;
            }
            if (locked || sled.IsOnLift)
            {
                stuckTimer = 0f;
                return input;
            }

            float d = follower.Distance;
            float v = sled.Speed;
            var stats = sled.Stats;

            // --- Steering: pure pursuit on the racing line ---
            UpdateAvoidance(d, v, dt);
            float look = Mathf.Max(Difficulty.LookAheadMin, v * Difficulty.LookAheadTime);
            float dl = d + look;
            float half = Track.GetWidth(dl) * 0.5f - edgeMargin * 0.6f;
            float wander = Mathf.Sin(Time.time * 0.4f + wanderPhase) * Difficulty.LineWander;
            float offset = Mathf.Clamp(SampleArray(lineOffset, dl) + avoidOffset + wander, -half, half);
            Vector3 target = Track.GetPosition(dl) + Track.GetRight(dl) * offset;
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, Mathf.Max(0.5f, local.z));
            input.Steer = Mathf.Clamp(angle * Difficulty.SteerGain, -1f, 1f);

            // --- Speed: brake to the planned profile (with a little anticipation) ---
            float vt = float.MaxValue;
            float horizon = v * 0.4f + 3f;
            for (float o = 0f; o <= horizon; o += step) vt = Mathf.Min(vt, SampleArray(targetSpeed, d + o));
            TargetSpeed = vt;
            float brake = v > vt + 0.3f ? Mathf.Clamp01((v - vt) / 2.5f) : 0f;

            // --- Drift decision per corner ---
            float kSigned = SampleArray(curvature, d + look);
            float kAhead = Mathf.Abs(kSigned);
            if (!inCorner && kAhead > Difficulty.DriftCurvature)
            {
                inCorner = true;
                cornerSign = Mathf.Sign(kSigned);
                driftThisCorner = rng.NextDouble() < Difficulty.DriftUsage;
                goForSuper = driftThisCorner && rng.NextDouble() < Difficulty.SuperDriftUsage;
            }
            else if (inCorner && kAhead < Difficulty.DriftCurvature * 0.4f)
            {
                inCorner = false;
                driftThisCorner = false;
            }
            bool drift;
            if (sled.IsDrifting)
            {
                // Hold the drift through the corner until the wanted boost level is charged.
                int wantLevel = goForSuper ? 2 : 1;
                bool charged = sled.DriftLevel >= wantLevel;
                float kNear = SampleArray(curvature, d + 2f);
                bool cornerGoesOn = inCorner && Mathf.Sign(kNear) == cornerSign && Mathf.Abs(kNear) > Difficulty.DriftCurvature * 0.3f;
                float insideRoom = Track.GetWidth(d) * 0.5f - cornerSign * follower.Lateral;
                drift = driftThisCorner && !charged && cornerGoesOn && insideRoom > edgeMargin && v > stats.DriftMinSpeed;
                // The controller drops the drift below |steer| 0.4: keep a little steer into the corner while holding.
                if (drift && Mathf.Sign(input.Steer) == cornerSign && Mathf.Abs(input.Steer) < 0.45f)
                    input.Steer = cornerSign * 0.45f;
            }
            else
            {
                // Start only once braking for the corner is done (drifting scrubs brake force).
                drift = driftThisCorner && Mathf.Abs(input.Steer) > 0.5f && v > stats.DriftMinSpeed + 1f && brake < 0.3f;
            }
            input.Drift = drift;
            // Avoid accidental brake-drifts while turning unless drifting on purpose.
            if (!drift && Mathf.Abs(input.Steer) > 0.4f) brake = Mathf.Min(brake, 0.45f);
            input.Brake = brake;

            // --- Rubber band + difficulty drag ---
            float rubber = 0f;
            if (GapProvider != null)
            {
                float gap = GapProvider();
                if (gap < -20f) rubber = -Mathf.Clamp01((-gap - 20f) / 80f);
                else if (gap > 20f) rubber = 0.5f * Mathf.Clamp01((gap - 20f) / 80f);
            }
            sled.ExternalDragMultiplier = Difficulty.DragFactor * (1f + rubber * Difficulty.RubberBand);

            // --- Stuck: ask for a reset ---
            stuckTimer = v < stuckSpeed ? stuckTimer + dt : 0f;
            if (stuckTimer > stuckTime)
            {
                stuckTimer = 0f;
                input.Reset = true;
            }
            return input;
        }

        private void UpdateAvoidance(float d, float v, float dt)
        {
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            float desired = 0f;
            float closest = float.MaxValue;
            float myLateral = follower.Lateral;
            float baseLine = SampleArray(lineOffset, d);
            float half = Track.GetWidth(d) * 0.5f - edgeMargin * 0.6f;

            foreach (var other in SledController.All)
            {
                if (other == sled || other.IsOnLift || other.Body == null) continue;
                Vector3 rel = other.Body.position - sled.Body.position;
                float along = Vector3.Dot(rel, fwd);
                if (along < -0.5f || along > avoidLookAhead || along >= closest) continue;
                float lat = Vector3.Dot(rel, right);
                if (Mathf.Abs(lat) > avoidLateral * 1.6f) continue;
                if (along > 2.5f && v - other.Speed < 0.3f) continue; // not catching up

                float otherOffset = myLateral + lat;
                float roomRight = half - (otherOffset + avoidLateral);
                float roomLeft = (otherOffset - avoidLateral) + half;
                float side = roomRight > roomLeft ? 1f : -1f;
                desired = otherOffset + side * avoidLateral - baseLine;
                closest = along;
            }

            avoidOffset = Mathf.MoveTowards(avoidOffset, desired, avoidShiftSpeed * dt);
        }
    }
}

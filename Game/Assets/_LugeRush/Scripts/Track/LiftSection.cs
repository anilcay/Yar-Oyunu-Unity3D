using System;
using System.Collections.Generic;
using LugeRush.UI;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>
    /// Cable lift over [LiftStart, LiftEnd]. Sleds entering the range are attached (kinematic, controls off) exactly
    /// where they are: they ease onto the centreline and cable speed, and a sled that arrived too close to the one
    /// ahead holds back until MinGap has opened. Nothing is ever moved backwards. Released at the top with an exit speed.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class LiftSection : MonoBehaviour
    {
        /// <summary>Must match SM_Track_Lift_Cable_4m: cable line relative to the lane centre.</summary>
        public const float CableLateral = 0.75f;
        public const float CableHeight = 0.255f;
        /// <summary>Height of SM_Track_Lift_Hook above its pivot (the grip on the cable).</summary>
        private const float HookModelLength = 0.18f;
        private const float HookSledHalfWidth = 0.38f;
        private const float HookSledHeight = 0.2f;
        private const float HookShowOffset = 0.3f;

        public TrackPath Track;
        public float Speed = 18f;
        public float ExitSpeed = 9f;
        [Tooltip("Minimum spacing between sleds on the cable (metres).")]
        public float MinGap = 3.5f;
        [Tooltip("Sleds are grabbed only within this many metres after LiftStart.")]
        public float CaptureWindow = 15f;
        [Tooltip("Max lateral distance from the centreline for a grab.")]
        public float CaptureLateral = 4f;
        [Tooltip("How quickly a rider's speed changes towards the cable speed (m/s^2).")]
        public float SpeedBlendRate = 12f;
        [Tooltip("How quickly a rider slides onto the centreline after the grab (m/s).")]
        public float OffsetBlendRate = 3f;
        [Tooltip("How quickly a rider turns to the lane direction after the grab (deg/s).")]
        public float TurnBlendRate = 120f;

        private class Rider
        {
            public SledController Sled;
            public TrackFollower Follower;
            public float S;          // metres since LiftStart
            public float V;          // current speed along the cable
            public Vector2 Offset;   // lateral / height from the rest pose on the centreline, eases to zero
            public Quaternion Rotation;
            public Transform Hook;
        }

        /// <summary>Sorted front to back (largest S first).</summary>
        private readonly List<Rider> riders = new List<Rider>();
        private readonly Dictionary<SledController, TrackFollower> followers = new Dictionary<SledController, TrackFollower>();
        private readonly Stack<Transform> hookPool = new Stack<Transform>();

        public event Action<SledController> SledAttached;
        public event Action<SledController> SledReleased;

        public int RiderCount => riders.Count;

        private void Awake()
        {
            if (Track == null) Track = GetComponentInParent<TrackPath>();
            if (Track == null) Track = FindFirstObjectByType<TrackPath>();
        }

        private void FixedUpdate()
        {
            if (Track == null || !Track.HasLift) return;
            float dt = Time.fixedDeltaTime;
            CaptureNewRiders();

            Rider ahead = null;
            for (int i = 0; i < riders.Count; i++)
            {
                var r = riders[i];
                if (r.Sled == null || !r.Sled.isActiveAndEnabled || !r.Sled.IsOnLift)
                {
                    ReturnHook(r);
                    riders.RemoveAt(i--);
                    continue;
                }

                float target = Speed;
                if (ahead != null) target *= Mathf.Clamp01((ahead.S - r.S) / MinGap);
                r.V = Mathf.MoveTowards(r.V, target, SpeedBlendRate * dt);
                r.S += r.V * dt;
                if (r.S >= Track.LiftLength)
                {
                    Release(r);
                    riders.RemoveAt(i--);
                    continue;
                }

                float d = Track.Wrap(Track.LiftStart + r.S);
                Vector3 up = Track.GetUp(d);
                Vector3 right = Track.GetRight(d);
                Vector3 tan = Track.GetTangent(d);
                r.Offset = Vector2.MoveTowards(r.Offset, Vector2.zero, OffsetBlendRate * dt);
                r.Rotation = Quaternion.RotateTowards(r.Rotation, Quaternion.LookRotation(tan, up), TurnBlendRate * dt);
                Vector3 pos = Track.GetPosition(d) + up * (r.Sled.RestHoverHeight + r.Offset.y) + right * r.Offset.x;
                r.Sled.MoveOnLift(pos, r.Rotation, r.V);
                r.Follower?.SetDistanceHint(d);
                UpdateHook(r, d, up, right, tan, pos);
                ahead = r;
            }
        }

        private void CaptureNewRiders()
        {
            foreach (var sled in SledController.All)
            {
                if (sled.IsOnLift || sled.Frozen) continue;
                var follower = GetFollower(sled);
                if (follower == null || !follower.IsValid) continue;

                float s = Track.Wrap(follower.Distance - Track.LiftStart);
                if (s > CaptureWindow || s > Track.LiftLength) continue;
                if (Mathf.Abs(follower.Lateral) > CaptureLateral || Mathf.Abs(follower.Height) > 3f) continue;

                var rider = new Rider
                {
                    Sled = sled,
                    Follower = follower,
                    S = s,
                    V = Mathf.Max(0f, Vector3.Dot(sled.Body.linearVelocity, Track.GetTangent(follower.Distance))),
                    Offset = new Vector2(follower.Lateral, follower.Height - sled.RestHoverHeight),
                    Rotation = sled.Body.rotation
                };
                sled.AttachToLift();
                int index = 0;
                while (index < riders.Count && riders[index].S > s) index++;
                riders.Insert(index, rider);
                SledAttached?.Invoke(sled);
            }
        }

        private void Release(Rider r)
        {
            ReturnHook(r);
            float d = Track.LiftEnd;
            Vector3 up = Track.GetUp(d);
            Vector3 tan = Track.GetTangent(d);
            r.Sled.ResetTo(Track.GetPosition(d) + up * r.Sled.RestHoverHeight, Quaternion.LookRotation(tan, up));
            r.Sled.DetachFromLift(tan * ExitSpeed);
            r.Follower?.SetDistanceHint(d);
            SledReleased?.Invoke(r.Sled);
        }

        public bool IsRiding(SledController sled)
        {
            foreach (var r in riders) if (r.Sled == sled) return true;
            return false;
        }

        /// <summary>Hook visual: grips the cable and reaches across to the sled once it has settled in the lane.</summary>
        private void UpdateHook(Rider r, float d, Vector3 up, Vector3 right, Vector3 tan, Vector3 sledPos)
        {
            bool show = r.Offset.sqrMagnitude < HookShowOffset * HookShowOffset;
            if (show && r.Hook == null) r.Hook = TakeHook();
            if (r.Hook == null) return;
            if (!show) { ReturnHook(r); return; }

            Vector3 grip = Track.GetPosition(d) + right * CableLateral + up * CableHeight;
            Vector3 toSled = sledPos + right * HookSledHalfWidth + up * HookSledHeight - grip;
            r.Hook.SetPositionAndRotation(grip, Quaternion.LookRotation(tan, toSled));
            r.Hook.localScale = new Vector3(1.6f, toSled.magnitude / HookModelLength, 1.6f);
        }

        private Transform TakeHook()
        {
            if (hookPool.Count > 0)
            {
                var pooled = hookPool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            var content = GameContent.Instance;
            if (content == null || content.LiftHookPrefab == null) return null;
            return Instantiate(content.LiftHookPrefab, transform).transform;
        }

        private void ReturnHook(Rider r)
        {
            if (r.Hook == null) return;
            r.Hook.gameObject.SetActive(false);
            hookPool.Push(r.Hook);
            r.Hook = null;
        }

        private TrackFollower GetFollower(SledController sled)
        {
            if (!followers.TryGetValue(sled, out var f) || f == null)
            {
                f = sled.GetComponent<TrackFollower>();
                followers[sled] = f;
            }
            return f;
        }
    }
}

using System;
using System.Collections.Generic;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>
    /// Cable lift over [LiftStart, LiftEnd]. Sleds entering the range are attached (kinematic, controls off),
    /// pulled at a constant speed in entry order with a minimum gap, and released at the top with an exit speed.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class LiftSection : MonoBehaviour
    {
        public TrackPath Track;
        public float Speed = 18f;
        public float ExitSpeed = 9f;
        [Tooltip("Minimum spacing between sleds on the cable (metres).")]
        public float MinGap = 3.5f;
        [Tooltip("Sleds are grabbed only within this many metres after LiftStart.")]
        public float CaptureWindow = 15f;
        [Tooltip("Max lateral distance from the centreline for a grab.")]
        public float CaptureLateral = 4f;

        private class Rider
        {
            public SledController Sled;
            public TrackFollower Follower;
            public float S; // metres since LiftStart
        }

        private readonly List<Rider> riders = new List<Rider>();
        private readonly Dictionary<SledController, TrackFollower> followers = new Dictionary<SledController, TrackFollower>();

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

            for (int i = 0; i < riders.Count; i++)
            {
                var r = riders[i];
                if (r.Sled == null || !r.Sled.isActiveAndEnabled || !r.Sled.IsOnLift)
                {
                    riders.RemoveAt(i--);
                    continue;
                }

                r.S += Speed * dt;
                if (r.S >= Track.LiftLength)
                {
                    Release(r);
                    riders.RemoveAt(i--);
                    continue;
                }

                float d = Track.Wrap(Track.LiftStart + r.S);
                Vector3 up = Track.GetUp(d);
                Vector3 pos = Track.GetPosition(d) + up * r.Sled.RestHoverHeight;
                r.Sled.MoveOnLift(pos, Quaternion.LookRotation(Track.GetTangent(d), up), Speed);
                r.Follower?.SetDistanceHint(d);
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

                // Keep entry order and a constant gap: never closer than MinGap to the previous rider.
                if (riders.Count > 0) s = Mathf.Min(s, riders[riders.Count - 1].S - MinGap);
                sled.AttachToLift();
                riders.Add(new Rider { Sled = sled, Follower = follower, S = s });
                SledAttached?.Invoke(sled);
            }
        }

        private void Release(Rider r)
        {
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

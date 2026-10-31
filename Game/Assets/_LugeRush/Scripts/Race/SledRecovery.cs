using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Race
{
    /// <summary>
    /// Respawns a sled at its last checkpoint when it is off track, stuck, flipped, or the driver presses Reset.
    /// </summary>
    [RequireComponent(typeof(RaceParticipant))]
    [DefaultExecutionOrder(20)]
    public class SledRecovery : MonoBehaviour
    {
        public float StuckSpeed = 1.5f;
        public float StuckTime = 3f;
        [Tooltip("Metres beyond the road edge that count as off track.")]
        public float OffTrackMargin = 2.5f;
        public float OffTrackBelow = 4f;
        public float OffTrackTime = 0.5f;
        public float FlipTime = 1.5f;
        public float ManualCooldown = 2f;
        public float RespawnSpeed = 6f;
        [Tooltip("Respawn this far before the lift if the last checkpoint is on the lift.")]
        public float LiftRespawnBackoff = 10f;

        private RaceParticipant participant;
        private float stuckTimer, offTimer, flipTimer;
        private float lastResetTime = -100f;

        public int RespawnCount { get; private set; }

        private void Awake() { participant = GetComponent<RaceParticipant>(); }

        private void FixedUpdate()
        {
            var rm = RaceManager.Instance;
            var sled = participant.Controller;
            var follower = participant.Follower;
            if (rm == null || sled == null || follower == null || !follower.IsValid) return;
            bool active = rm.State == RaceState.Racing || rm.State == RaceState.Finished;
            if (!active || sled.IsOnLift || sled.Frozen)
            {
                stuckTimer = offTimer = flipTimer = 0f;
                return;
            }

            float dt = Time.fixedDeltaTime;
            var track = follower.Track;
            float d = follower.Distance;

            if (sled.LastInput.Reset && Time.time - lastResetTime > ManualCooldown)
            {
                Respawn();
                return;
            }

            stuckTimer = sled.Speed < StuckSpeed ? stuckTimer + dt : 0f;
            bool off = Mathf.Abs(follower.Lateral) > track.GetWidth(d) * 0.5f + OffTrackMargin || follower.Height < -OffTrackBelow;
            offTimer = off ? offTimer + dt : 0f;
            bool flipped = Vector3.Dot(transform.up, track.GetUp(d)) < 0.2f;
            flipTimer = flipped ? flipTimer + dt : 0f;

            if (stuckTimer > StuckTime || offTimer > OffTrackTime || flipTimer > FlipTime) Respawn();
        }

        public void Respawn()
        {
            var sled = participant.Controller;
            var follower = participant.Follower;
            var track = follower.Track;
            if (sled == null || track == null || sled.IsOnLift) return;

            float d = participant.LastCheckpointDistance;
            if (track.IsInLift(d)) d = track.Wrap(track.LiftStart - LiftRespawnBackoff);

            Vector3 up = track.GetUp(d);
            Vector3 right = track.GetRight(d);
            Vector3 center = track.GetPosition(d) + up * sled.RestHoverHeight;
            float lateral = PickFreeLateral(center, right, track.GetWidth(d));
            Vector3 tan = track.GetTangent(d);

            sled.ResetTo(center + right * lateral, Quaternion.LookRotation(tan, up));
            sled.SetVelocity(tan * RespawnSpeed);
            follower.SetDistanceHint(d);
            RaceManager.Instance?.NotifyTeleported(participant, d);

            stuckTimer = offTimer = flipTimer = 0f;
            lastResetTime = Time.time;
            RespawnCount++;
            participant.ResetCount = RespawnCount;
        }

        private float PickFreeLateral(Vector3 center, Vector3 right, float width)
        {
            float[] candidates = { 0f, -width * 0.25f, width * 0.25f };
            foreach (float c in candidates)
            {
                Vector3 p = center + right * c;
                bool free = true;
                foreach (var other in SledController.All)
                {
                    if (other == participant.Controller || other.Body == null) continue;
                    if ((other.Body.position - p).sqrMagnitude < 2.5f * 2.5f) { free = false; break; }
                }
                if (free) return c;
            }
            return 0f;
        }
    }
}

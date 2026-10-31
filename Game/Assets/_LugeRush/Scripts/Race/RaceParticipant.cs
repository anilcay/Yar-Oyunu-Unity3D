using System.Collections.Generic;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Race
{
    /// <summary>Per-sled race state. Written only by RaceManager (through its authority); read by HUD/camera/AI.</summary>
    public class RaceParticipant : MonoBehaviour
    {
        public string DisplayName = "Sürücü";
        public bool IsBot;
        public bool IsLocalPlayer;
        [Tooltip("Start grid slot (0..7).")]
        public int GridSlot;

        [Header("Runtime (read only)")]
        public int Lap;              // current lap, 1-based (0 = not started)
        public float TrackDistance;  // metres from track origin
        public int Position;         // 1-based
        public bool Finished;
        public float TotalTime;
        public float BestLapTime;    // 0 = none yet

        public float LastLapTime;
        public int FinishOrder;
        public int ResetCount;
        public bool WrongWay;
        public readonly List<float> LapTimes = new List<float>();

        /// <summary>Signed distance travelled since the start line (Lap * Length + progress). Used for standings.</summary>
        public float RaceDistance { get; internal set; }
        /// <summary>Number of checkpoints passed in order since the start.</summary>
        public int CheckpointsPassed { get; internal set; }
        /// <summary>Track distance of the last passed checkpoint (respawn point).</summary>
        public float LastCheckpointDistance { get; internal set; }
        public float CurrentLapTime { get; internal set; }

        internal float PrevTrackDistance;
        internal float LapStartTime;
        internal float WrongWayTimer;

        public SledController Controller { get; private set; }
        public TrackFollower Follower { get; private set; }

        private void Awake()
        {
            Controller = GetComponent<SledController>();
            Follower = GetComponent<TrackFollower>();
        }

        internal void ResetRaceState()
        {
            Lap = 0;
            Position = 0;
            Finished = false;
            TotalTime = 0f;
            BestLapTime = 0f;
            LastLapTime = 0f;
            FinishOrder = 0;
            ResetCount = 0;
            WrongWay = false;
            LapTimes.Clear();
            RaceDistance = 0f;
            CheckpointsPassed = 0;
            CurrentLapTime = 0f;
        }
    }
}

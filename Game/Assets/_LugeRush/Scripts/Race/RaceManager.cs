using System;
using System.Collections.Generic;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Race
{
    /// <summary>
    /// Race flow: grid -> countdown 3-2-1-GO -> laps via ordered checkpoints -> finish (+ grace period).
    /// Rules run only with authority; every state change goes through <see cref="Authority"/> and lands
    /// in the Apply* methods, so a networked authority can replicate them.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public class RaceManager : MonoBehaviour
    {
        public static RaceManager Instance;

        [Header("Setup")]
        public TrackPath Track;
        public StartGrid Grid;
        public RaceConfig Config = new RaceConfig();

        [Header("Timing")]
        [Tooltip("Delay before the countdown begins.")]
        public float PreCountdownDelay = 1f;
        public int CountdownFrom = 3;
        [Tooltip("After the first finisher, the others have at least this long to finish.")]
        public float FinishGraceSeconds = 20f;
        [Tooltip("Grace also grows with the race: this fraction of the winner's time (a fixed window ends long races early).")]
        public float FinishGraceFraction = 0.2f;

        [Header("Start push timing (seconds relative to GO)")]
        public float PushEarlyLimit = -0.25f;
        public float PushPerfectWindow = 0.15f;
        public float PushLateLimit = 0.5f;

        [Header("Wrong way")]
        public float WrongWaySpeed = 2f;
        public float WrongWayDelay = 1f;

        [Header("Network")]
        [Tooltip("Participants are registered later via SetupParticipants (network spawned sleds).")]
        public bool DeferParticipantSetup;
        [Tooltip("Stay in Waiting until cleared (host waits for every peer before the countdown).")]
        public bool HoldInWaiting;

        public RaceState State = RaceState.Waiting;
        public IRaceAuthority Authority { get; set; }
        /// <summary>True once the participant list is known and placed on the grid.</summary>
        public bool IsSetUp { get; private set; }

        public event Action<int> CountdownTick;
        public event Action RaceStarted;
        public event Action<RaceParticipant, int> LapCompleted;
        public event Action<RaceParticipant> ParticipantFinished;
        public event Action RaceFinished;
        public event Action<RaceState> StateChanged;

        private readonly List<RaceParticipant> participants = new List<RaceParticipant>();
        private readonly List<RaceParticipant> standings = new List<RaceParticipant>();
        private readonly Dictionary<RaceParticipant, float> pushPressTimes = new Dictionary<RaceParticipant, float>();
        private readonly HashSet<RaceParticipant> pushJudged = new HashSet<RaceParticipant>();
        private readonly Dictionary<SledController, Action> pushHandlers = new Dictionary<SledController, Action>();

        private float stateTime;
        private float raceStartTime;
        private int lastTick = int.MaxValue;
        private int finishCount;
        private float graceDeadline = float.PositiveInfinity;

        public IReadOnlyList<RaceParticipant> Standings => standings;
        public IReadOnlyList<RaceParticipant> Participants => participants;
        public float RaceStartTime => raceStartTime;
        public float RaceTime => State == RaceState.Racing || State == RaceState.Finished ? Time.time - raceStartTime : 0f;
        /// <summary>Seconds left for the others after the first finisher (infinity if nobody finished yet).</summary>
        public float GraceRemaining => float.IsPositiveInfinity(graceDeadline) ? float.PositiveInfinity : Mathf.Max(0f, graceDeadline - Time.time);
        public int TotalLaps => Mathf.Clamp(Config.Laps, RaceConfig.MinLaps, RaceConfig.MaxLaps);

        private void Awake()
        {
            Instance = this;
            Authority ??= new LocalRaceAuthority();
            if (Track == null) Track = FindFirstObjectByType<TrackPath>();
            if (Grid == null) Grid = FindFirstObjectByType<StartGrid>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var kv in pushHandlers) if (kv.Key != null) kv.Key.PushPressed -= kv.Value;
        }

        private void Start()
        {
            Authority.Bind(this);
            Track.EnsureBuilt();
            StartGateLights.Attach(this);
            if (DeferParticipantSetup) return;
            var found = new List<RaceParticipant>();
            foreach (var p in FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None))
                if (p.isActiveAndEnabled) found.Add(p);
            SetupParticipants(found);
        }

        /// <summary>
        /// Registers the racing sleds (sorted by GridSlot), places them on the grid and enters Waiting.
        /// Called from Start for scene-placed sleds, or by the network layer once every sled is spawned.
        /// </summary>
        public void SetupParticipants(IEnumerable<RaceParticipant> list)
        {
            foreach (var kv in pushHandlers) if (kv.Key != null) kv.Key.PushPressed -= kv.Value;
            pushHandlers.Clear();
            participants.Clear();
            foreach (var p in list) if (p != null) participants.Add(p);
            participants.Sort((a, b) => a.GridSlot.CompareTo(b.GridSlot));

            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                p.ResetRaceState();
                PlaceOnGrid(p, i);
                var sled = p.Controller;
                if (sled != null)
                {
                    Action handler = () => OnPushPressed(p);
                    sled.PushPressed += handler;
                    pushHandlers[sled] = handler;
                }
            }
            standings.Clear();
            standings.AddRange(participants);
            UpdateStandings();
            IsSetUp = true;
            ApplyState(RaceState.Waiting, 0f);
        }

        private void PlaceOnGrid(RaceParticipant p, int slot)
        {
            var sled = p.Controller;
            float hover = sled != null ? sled.RestHoverHeight : 0.15f;
            var pose = Grid.GetSlot(slot, hover);
            sled?.ResetTo(pose.position, pose.rotation);
            if (sled != null)
            {
                sled.Frozen = true;
                sled.ControlsLocked = true;
            }
            float d = Grid.GetSlotDistance(slot);
            p.Follower?.SetDistanceHint(d);
            p.TrackDistance = d;
            p.PrevTrackDistance = d;
            p.RaceDistance = Track.SignedDelta(Track.StartLineDistance, d);
            p.LastCheckpointDistance = d;
        }

        private void FixedUpdate()
        {
            if (!IsSetUp) return;
            UpdateProgress();
            UpdateStandings();
            if (Authority == null || !Authority.HasAuthority) return;

            stateTime += Time.fixedDeltaTime;
            switch (State)
            {
                case RaceState.Waiting:
                    if (HoldInWaiting) stateTime = 0f;
                    else if (stateTime >= PreCountdownDelay) Authority.CommitState(RaceState.Countdown, 0f);
                    break;
                case RaceState.Countdown:
                    int tick = CountdownFrom - Mathf.FloorToInt(stateTime);
                    if (tick != lastTick && tick > 0) Authority.CommitCountdown(tick);
                    if (stateTime >= CountdownFrom)
                    {
                        Authority.CommitCountdown(0);
                        Authority.CommitState(RaceState.Racing, Time.time);
                    }
                    break;
                case RaceState.Racing:
                    EvaluateRules();
                    break;
            }
        }

        // ---------- Rules (authority only) ----------

        private void EvaluateRules()
        {
            int cpCount = Track.CheckpointCount;
            int laps = TotalLaps;
            foreach (var p in participants)
            {
                if (p.Finished) continue;
                int lapsDone = p.CheckpointsPassed / cpCount;
                if (lapsDone >= p.Lap && p.Lap >= 1)
                {
                    float lapTime = Time.time - p.LapStartTime;
                    Authority.CommitLap(p, p.Lap + 1, lapTime);
                    if (lapsDone >= laps) Authority.CommitFinish(p, Time.time - raceStartTime);
                }
            }

            bool allDone = true;
            foreach (var p in participants) allDone &= p.Finished;
            if (allDone || Time.time >= graceDeadline) Authority.CommitState(RaceState.Finished, raceStartTime);

            if (Time.fixedTime - raceStartTime > PushLateLimit) pushPressTimes.Clear();
        }

        /// <summary>Distance bookkeeping; runs on every peer from synchronized transforms.</summary>
        private void UpdateProgress()
        {
            if (Track == null || participants.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            int cpCount = Track.CheckpointCount;
            float interval = Track.CheckpointInterval;

            foreach (var p in participants)
            {
                if (p.Follower == null || !p.Follower.IsValid) continue;
                float d = p.Follower.Distance;
                float speedLimit = (p.Controller != null && p.Controller.Stats != null ? p.Controller.Stats.MaxSpeed * 2f : 60f) * dt + 2f;
                float delta = Mathf.Clamp(Track.SignedDelta(p.PrevTrackDistance, d), -speedLimit, speedLimit);
                p.PrevTrackDistance = d;
                p.TrackDistance = d;
                if (State != RaceState.Racing && State != RaceState.Finished) continue;

                p.RaceDistance += delta;
                // Checkpoints only count in order: the odometer must exceed the next one.
                while (p.RaceDistance >= (p.CheckpointsPassed + 1) * interval)
                {
                    p.CheckpointsPassed++;
                    p.LastCheckpointDistance = Track.GetCheckpointDistance(p.CheckpointsPassed % cpCount);
                }
                if (!p.Finished) p.CurrentLapTime = Time.time - p.LapStartTime;

                // Wrong way: moving against the track tangent.
                var sled = p.Controller;
                bool backwards = sled != null && sled.Body != null && !sled.Body.isKinematic &&
                                 Vector3.Dot(sled.Body.linearVelocity, Track.GetTangent(d)) < -WrongWaySpeed;
                p.WrongWayTimer = backwards ? p.WrongWayTimer + dt : 0f;
                p.WrongWay = p.WrongWayTimer > WrongWayDelay;
            }
        }

        private void UpdateStandings()
        {
            standings.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished) return a.FinishOrder.CompareTo(b.FinishOrder);
                return b.RaceDistance.CompareTo(a.RaceDistance);
            });
            for (int i = 0; i < standings.Count; i++) standings[i].Position = i + 1;
        }

        /// <summary>Call after a respawn/teleport so the odometer follows exactly (bypasses the jump clamp).</summary>
        public void NotifyTeleported(RaceParticipant p, float newTrackDistance)
        {
            float delta = Track.SignedDelta(p.PrevTrackDistance, newTrackDistance);
            if (State == RaceState.Racing || State == RaceState.Finished) p.RaceDistance += delta;
            p.PrevTrackDistance = newTrackDistance;
            p.TrackDistance = newTrackDistance;
        }

        // ---------- Start push ----------

        private void OnPushPressed(RaceParticipant p)
        {
            if (pushJudged.Contains(p)) return;
            if (State == RaceState.Countdown || State == RaceState.Waiting)
            {
                if (!pushPressTimes.ContainsKey(p)) pushPressTimes[p] = Time.fixedTime;
            }
            else if (State == RaceState.Racing)
            {
                float rel = Time.fixedTime - raceStartTime;
                if (rel <= PushLateLimit) JudgePush(p, rel);
            }
        }

        private void JudgePush(RaceParticipant p, float relativeTime)
        {
            pushJudged.Add(p);
            float quality;
            if (relativeTime < PushEarlyLimit) quality = -1f;
            else if (relativeTime <= PushPerfectWindow) quality = 1f;
            else quality = Mathf.Lerp(1f, 0.3f, Mathf.InverseLerp(PushPerfectWindow, PushLateLimit, relativeTime));
            p.Controller?.ApplyStartPush(quality);
        }

        // ---------- Apply (called by the authority) ----------

        public void ApplyState(RaceState state, float startTime)
        {
            State = state;
            stateTime = 0f;
            switch (state)
            {
                case RaceState.Countdown:
                    lastTick = int.MaxValue;
                    break;
                case RaceState.Racing:
                    raceStartTime = startTime;
                    foreach (var p in participants)
                    {
                        p.Lap = 1;
                        p.LapStartTime = startTime;
                        if (p.Controller != null)
                        {
                            p.Controller.Frozen = false;
                            p.Controller.ControlsLocked = false;
                        }
                        if (pushPressTimes.TryGetValue(p, out float t)) JudgePush(p, t - startTime);
                    }
                    RaceStarted?.Invoke();
                    break;
                case RaceState.Finished:
                    RaceFinished?.Invoke();
                    break;
            }
            StateChanged?.Invoke(state);
        }

        public void ApplyCountdown(int tick)
        {
            lastTick = tick;
            CountdownTick?.Invoke(tick);
        }

        public void ApplyLap(RaceParticipant p, int newLap, float lapTime)
        {
            p.LastLapTime = lapTime;
            p.LapTimes.Add(lapTime);
            if (p.BestLapTime <= 0f || lapTime < p.BestLapTime) p.BestLapTime = lapTime;
            p.Lap = Mathf.Min(newLap, TotalLaps);
            p.LapStartTime = Time.time;
            LapCompleted?.Invoke(p, newLap - 1);
        }

        public void ApplyFinish(RaceParticipant p, float totalTime)
        {
            if (p.Finished) return;
            p.Finished = true;
            p.TotalTime = totalTime;
            p.FinishOrder = ++finishCount;
            if (finishCount == 1) graceDeadline = Time.time + Mathf.Max(FinishGraceSeconds, totalTime * FinishGraceFraction);
            UpdateStandings();
            ParticipantFinished?.Invoke(p);
        }
    }
}

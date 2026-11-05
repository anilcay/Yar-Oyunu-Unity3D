using System.Collections.Generic;
using System.Linq;
using LugeRush.Race;
using LugeRush.Track;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LugeRush.Network
{
    /// <summary>
    /// Host-authoritative race state for NGO. The host's RaceManager evaluates the rules; every commit is
    /// applied locally and replicated with RPCs (countdown, laps, finishes) plus NetworkVariables
    /// (state + server start time) so a late peer can catch up. Clients never evaluate rules.
    /// Also holds the start until every peer has spawned all sleds.
    /// </summary>
    public class NetworkRaceAuthority : NetworkBehaviour, IRaceAuthority
    {
        public static NetworkRaceAuthority Instance { get; private set; }

        [Tooltip("Host waits at most this long for slow peers before starting the countdown.")]
        [SerializeField] private float readyTimeout = 15f;

        public readonly NetworkVariable<int> ExpectedSleds = new NetworkVariable<int>();
        private readonly NetworkVariable<byte> state = new NetworkVariable<byte>((byte)RaceState.Waiting);
        private readonly NetworkVariable<double> raceStartServerTime = new NetworkVariable<double>();

        private RaceManager manager;
        private readonly Dictionary<int, NetworkSled> bySlot = new Dictionary<int, NetworkSled>();
        private readonly HashSet<ulong> readyClients = new HashSet<ulong>();
        private bool localSetupDone;
        private float setupTime;

        public bool HasAuthority => IsServer;
        public bool IsSetUp => localSetupDone;
        public RaceManager Manager => manager;

        public void Bind(RaceManager m) => manager = m;

        // ------------------------------------------------------------------ Spawning (host)

        /// <summary>Host: spawns the race authority and one NetworkSled per roster slot (owned by its player, bots by the host).</summary>
        public static void ServerSpawnRace(NetworkObject authorityPrefab, NetworkObject sledPrefab, IReadOnlyList<RosterEntry> roster, ICollection<ulong> unavailable)
        {
            var nm = NetworkManager.Singleton;
            var grid = FindFirstObjectByType<StartGrid>();
            var track = FindFirstObjectByType<TrackPath>();
            if (grid == null || track == null || authorityPrefab == null || sledPrefab == null)
            {
                Debug.LogError("[LR] Race scene is missing StartGrid/TrackPath or network prefabs are not assigned.");
                return;
            }
            track.EnsureBuilt();
            if (grid.Track == null) grid.Track = track;

            var auth = Instantiate(authorityPrefab);
            auth.GetComponent<NetworkRaceAuthority>().ExpectedSleds.Value = roster.Count;
            auth.Spawn(true);

            foreach (var e in roster)
            {
                bool connected = !e.IsBot && nm.ConnectedClientsIds.Contains(e.ClientId) && (unavailable == null || !unavailable.Contains(e.ClientId));
                bool bot = e.IsBot || !connected;
                var pose = grid.GetSlot(e.Slot, 0.15f);
                var no = Instantiate(sledPrefab, pose.position, pose.rotation);
                var ns = no.GetComponent<NetworkSled>();
                string name = e.Name;
                if (!e.IsBot && !connected) name = LobbyRules.SanitizeName(e.Name + " (bot)");
                ns.Info.Value = new SledSpawnInfo
                {
                    Slot = (byte)e.Slot, Sled = (byte)e.Sled, Color = (byte)e.Color, Helmet = (byte)e.Helmet,
                    IsBot = bot, Name = new FixedString32Bytes(name)
                };
                if (bot) no.Spawn(true);
                else no.SpawnWithOwnership(e.ClientId, true);
            }
            Debug.Log($"[LR] Spawned {roster.Count} sleds for the race.");
        }

        // ------------------------------------------------------------------ Lifecycle

        public override void OnNetworkSpawn()
        {
            Instance = this;
            manager = RaceManager.Instance != null ? RaceManager.Instance : FindFirstObjectByType<RaceManager>();
            if (manager == null)
            {
                Debug.LogError("[LR] NetworkRaceAuthority spawned without a RaceManager in the scene.");
                return;
            }
            manager.DeferParticipantSetup = true;
            manager.Authority = this;
            Bind(manager);
            if (IsServer)
            {
                manager.HoldInWaiting = true;
                // Network start: give remote players a moment after the hold is released.
                manager.PreCountdownDelay = Mathf.Max(manager.PreCountdownDelay, 1.5f);
            }
            if (manager.GetComponent<RaceEventLog>() == null) manager.gameObject.AddComponent<RaceEventLog>();
            foreach (var s in NetworkSled.All) Register(s);
            TrySetup();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(NetworkSled sled)
        {
            if (sled == null || sled.Participant == null) return;
            bySlot[sled.Info.Value.Slot] = sled;
            TrySetup();
        }

        private void TrySetup()
        {
            if (localSetupDone || manager == null || !IsSpawned) return;
            int expected = ExpectedSleds.Value;
            if (expected <= 0 || bySlot.Count < expected) return;

            manager.SetupParticipants(bySlot.Values.Select(s => s.Participant));
            localSetupDone = true;
            setupTime = Time.unscaledTime;
            Debug.Log($"[LR] Race set up locally with {bySlot.Count} sleds (server={IsServer}).");
            if (IsServer)
            {
                readyClients.Add(NetworkManager.ServerClientId);
            }
            else
            {
                ClientReadyRpc();
                CatchUpState();
            }
        }

        private void Update()
        {
            if (!IsServer || !localSetupDone || manager == null || !manager.HoldInWaiting) return;
            bool allReady = NetworkManager.ConnectedClientsIds.All(readyClients.Contains);
            bool timeout = Time.unscaledTime - setupTime > readyTimeout;
            if (allReady || timeout)
            {
                if (!allReady) Debug.LogWarning("[LR] Not every client reported ready; starting anyway.");
                manager.HoldInWaiting = false;
                Debug.Log("[LR] All peers ready, countdown can start.");
            }
        }

        [Rpc(SendTo.Server)]
        private void ClientReadyRpc(RpcParams rpc = default)
        {
            readyClients.Add(rpc.Receive.SenderClientId);
        }

        /// <summary>Client set up after the host already moved on (slow load): apply the replicated state.</summary>
        private void CatchUpState()
        {
            var s = (RaceState)state.Value;
            if (s == RaceState.Waiting) return;
            if (s == RaceState.Countdown) manager.ApplyState(RaceState.Countdown, 0f);
            else
            {
                manager.ApplyState(RaceState.Racing, LocalStartTime(raceStartServerTime.Value));
                if (s == RaceState.Finished) manager.ApplyState(RaceState.Finished, 0f);
            }
        }

        private float LocalStartTime(double serverStart)
        {
            double elapsed = NetworkManager.ServerTime.Time - serverStart;
            return Time.time - (float)System.Math.Max(0.0, elapsed);
        }

        private RaceParticipant BySlot(int slot) => bySlot.TryGetValue(slot, out var s) && s != null ? s.Participant : null;

        private static int SlotOf(RaceParticipant p) => p != null ? p.GridSlot : -1;

        // ------------------------------------------------------------------ IRaceAuthority (host side)

        public void CommitState(RaceState newState, float raceStartTime)
        {
            manager.ApplyState(newState, raceStartTime);
            if (!IsServer) return;
            double serverStart = raceStartServerTime.Value;
            if (newState == RaceState.Racing)
            {
                serverStart = NetworkManager.ServerTime.Time;
                raceStartServerTime.Value = serverStart;
            }
            state.Value = (byte)newState;
            StateRpc((byte)newState, serverStart);
        }

        public void CommitCountdown(int tick)
        {
            manager.ApplyCountdown(tick);
            if (IsServer) CountdownRpc(tick);
        }

        public void CommitLap(RaceParticipant participant, int newLap, float lapTime)
        {
            manager.ApplyLap(participant, newLap, lapTime);
            if (IsServer) LapRpc(SlotOf(participant), newLap, lapTime);
        }

        public void CommitFinish(RaceParticipant participant, float totalTime)
        {
            manager.ApplyFinish(participant, totalTime);
            if (IsServer) FinishRpc(SlotOf(participant), totalTime);
        }

        // ------------------------------------------------------------------ Replication (clients)

        [Rpc(SendTo.NotServer)]
        private void StateRpc(byte newState, double serverStart)
        {
            if (!localSetupDone || manager == null) return; // CatchUpState handles it after setup
            var s = (RaceState)newState;
            manager.ApplyState(s, s == RaceState.Racing ? LocalStartTime(serverStart) : 0f);
        }

        [Rpc(SendTo.NotServer)]
        private void CountdownRpc(int tick)
        {
            if (localSetupDone && manager != null) manager.ApplyCountdown(tick);
        }

        [Rpc(SendTo.NotServer)]
        private void LapRpc(int slot, int newLap, float lapTime)
        {
            var p = BySlot(slot);
            if (localSetupDone && p != null) manager.ApplyLap(p, newLap, lapTime);
        }

        [Rpc(SendTo.NotServer)]
        private void FinishRpc(int slot, float totalTime)
        {
            var p = BySlot(slot);
            if (localSetupDone && p != null) manager.ApplyFinish(p, totalTime);
        }
    }
}

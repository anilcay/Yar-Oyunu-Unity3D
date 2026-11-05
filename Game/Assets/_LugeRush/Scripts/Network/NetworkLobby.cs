using System;
using System.Collections.Generic;
using LugeRush.Race;
using LugeRush.UI;
using LugeRush.Vehicle;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LugeRush.Network
{
    /// <summary>
    /// Replicated lobby: players (name, sled, colour, ready) and host settings (map, laps, bots, difficulty).
    /// Spawned by the host when the server starts and kept alive across scene loads (not destroyed with scene).
    /// </summary>
    public class NetworkLobby : NetworkBehaviour
    {
        public static NetworkLobby Instance { get; private set; }

        public NetworkList<LobbyPlayerData> Players;
        public readonly NetworkVariable<LobbySettingsData> Settings = new NetworkVariable<LobbySettingsData>();
        public readonly NetworkVariable<bool> InRace = new NetworkVariable<bool>();

        /// <summary>Raised on every peer whenever players or settings change.</summary>
        public static event Action Changed;

        private int joinCounter;

        private void Awake()
        {
            Players = new NetworkList<LobbyPlayerData>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Players.OnListChanged += OnPlayersChanged;
            Settings.OnValueChanged += OnSettingsChanged;
            InRace.OnValueChanged += OnInRaceChanged;

            if (IsServer)
            {
                var gs = GameSettings.Current;
                var content = GameContent.Instance;
                int trackCount = content != null && content.Tracks != null ? content.Tracks.Count : 1;
                int trackIndex = content != null && content.Tracks != null ? Mathf.Max(0, content.Tracks.IndexOf(gs.TrackId)) : 0;
                var s = new LobbySettingsData
                {
                    TrackIndex = trackIndex,
                    Laps = LaunchArgs.Laps > 0 ? LaunchArgs.Laps : gs.Laps,
                    Bots = LaunchArgs.Bots >= 0 ? LaunchArgs.Bots : gs.Bots,
                    Difficulty = LaunchArgs.Difficulty >= 0 ? LaunchArgs.Difficulty : (int)gs.Difficulty
                };
                if (!string.IsNullOrEmpty(LaunchArgs.TrackId) && content != null && content.Tracks != null && content.Tracks.IndexOf(LaunchArgs.TrackId) >= 0)
                    s.TrackIndex = content.Tracks.IndexOf(LaunchArgs.TrackId);
                Settings.Value = LobbyRules.Clamp(s, trackCount);
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            SubmitLocalProfile();
            ApplyToRaceSession();
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            Players.OnListChanged -= OnPlayersChanged;
            Settings.OnValueChanged -= OnSettingsChanged;
            InRace.OnValueChanged -= OnInRaceChanged;
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (Instance == this) Instance = null;
            Changed?.Invoke();
        }

        private void OnPlayersChanged(NetworkListEvent<LobbyPlayerData> e)
        {
            ApplyToRaceSession();
            Changed?.Invoke();
        }

        private void OnSettingsChanged(LobbySettingsData a, LobbySettingsData b)
        {
            ApplyToRaceSession();
            Changed?.Invoke();
        }

        private void OnInRaceChanged(bool a, bool b) => Changed?.Invoke();

        // ------------------------------------------------------------------ Queries

        public List<LobbyMember> Members()
        {
            var list = new List<LobbyMember>(Players.Count);
            foreach (var p in Players)
            {
                list.Add(new LobbyMember
                {
                    ClientId = p.ClientId, Name = p.Name.ToString(), Sled = (SledType)p.Sled, Color = (SledColor)p.Color,
                    Ready = p.Ready, JoinOrder = p.JoinOrder, IsHost = p.ClientId == NetworkManager.ServerClientId
                });
            }
            list.Sort((a, b) => a.IsHost != b.IsHost ? (a.IsHost ? -1 : 1) : a.JoinOrder.CompareTo(b.JoinOrder));
            return list;
        }

        public bool TryGetLocal(out LobbyPlayerData data)
        {
            ulong id = NetworkManager != null ? NetworkManager.LocalClientId : 0;
            foreach (var p in Players)
                if (p.ClientId == id) { data = p; return true; }
            data = default;
            return false;
        }

        public int EffectiveBots => LobbyRules.EffectiveBots(Players.Count, Settings.Value.Bots);

        /// <summary>Copies the replicated settings into RaceSession (read by RaceBootstrap/HUD in the race scene).</summary>
        public void ApplyToRaceSession()
        {
            var s = Settings.Value;
            var content = GameContent.Instance;
            var entry = content != null && content.Tracks != null ? content.Tracks.Get(s.TrackIndex) : null;
            if (entry != null) RaceSession.Config.TrackId = entry.TrackId;
            RaceSession.Config.Laps = Mathf.Clamp(s.Laps, RaceConfig.MinLaps, RaceConfig.MaxLaps);
            RaceSession.Config.BotCount = EffectiveBots;
            RaceSession.Config.Difficulty = (BotDifficulty)Mathf.Clamp(s.Difficulty, 0, 2);
        }

        // ------------------------------------------------------------------ Local player actions

        /// <summary>Sends name/sled/colour from the settings (or command line) to the host.</summary>
        public void SubmitLocalProfile()
        {
            var gs = GameSettings.Current;
            string name = !string.IsNullOrEmpty(LaunchArgs.Name) ? LaunchArgs.Name : gs.PlayerName;
            byte sled = (byte)(LaunchArgs.Sled >= 0 ? LaunchArgs.Sled % 4 : (int)gs.Sled);
            byte color = (byte)(LaunchArgs.Color >= 0 ? LaunchArgs.Color % 4 : (int)gs.Color);
            SubmitProfileRpc(new FixedString32Bytes(LobbyRules.SanitizeName(name)), sled, color);
        }

        public void SetLocalSled(SledType type, SledColor color)
        {
            if (!TryGetLocal(out var me)) return;
            SubmitProfileRpc(me.Name, (byte)type, (byte)color);
        }

        public void SetLocalReady(bool ready) => SetReadyRpc(ready);

        [Rpc(SendTo.Server)]
        private void SubmitProfileRpc(FixedString32Bytes name, byte sled, byte color, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            int index = IndexOf(sender);
            var taken = new HashSet<string>();
            for (int i = 0; i < Players.Count; i++)
                if (i != index) taken.Add(Players[i].Name.ToString());
            string unique = LobbyRules.UniqueName(name.ToString(), taken);

            if (index >= 0)
            {
                var p = Players[index];
                p.Name = new FixedString32Bytes(unique);
                p.Sled = (byte)(sled % 4);
                p.Color = (byte)(color % 4);
                Players[index] = p;
            }
            else
            {
                if (Players.Count >= LobbyRules.Capacity || InRace.Value) return;
                Players.Add(new LobbyPlayerData
                {
                    ClientId = sender, Name = new FixedString32Bytes(unique), Sled = (byte)(sled % 4), Color = (byte)(color % 4),
                    Ready = false, JoinOrder = joinCounter++
                });
                Debug.Log($"[LR] Lobby: {unique} joined (client {sender}), {Players.Count} player(s).");
            }
        }

        [Rpc(SendTo.Server)]
        private void SetReadyRpc(bool ready, RpcParams rpc = default)
        {
            int index = IndexOf(rpc.Receive.SenderClientId);
            if (index < 0) return;
            var p = Players[index];
            if (p.Ready == ready) return;
            p.Ready = ready;
            Players[index] = p;
            Debug.Log($"[LR] Lobby: {p.Name} {(ready ? "ready" : "not ready")}.");
        }

        // ------------------------------------------------------------------ Host

        public void ServerSetSettings(LobbySettingsData s)
        {
            if (!IsServer) return;
            var content = GameContent.Instance;
            int trackCount = content != null && content.Tracks != null ? content.Tracks.Count : 1;
            s = LobbyRules.Clamp(s, trackCount);
            if (!s.Equals(Settings.Value)) Settings.Value = s;
        }

        public void ServerSetInRace(bool inRace)
        {
            if (IsServer) InRace.Value = inRace;
        }

        public void ServerResetReady()
        {
            if (!IsServer) return;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (!p.Ready) continue;
                p.Ready = false;
                Players[i] = p;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            int index = IndexOf(clientId);
            if (index < 0) return;
            Debug.Log($"[LR] Lobby: {Players[index].Name} left.");
            Players.RemoveAt(index);
        }

        private int IndexOf(ulong clientId)
        {
            for (int i = 0; i < Players.Count; i++) if (Players[i].ClientId == clientId) return i;
            return -1;
        }
    }
}

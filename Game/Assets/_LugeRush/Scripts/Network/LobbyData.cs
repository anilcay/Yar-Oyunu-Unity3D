using System;
using Unity.Collections;
using Unity.Netcode;

namespace LugeRush.Network
{
    /// <summary>One human in the lobby (replicated in NetworkLobby.Players).</summary>
    public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public byte Sled;     // SledType
        public byte Color;    // SledColor
        public bool Ready;
        public int JoinOrder;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Name);
            s.SerializeValue(ref Sled);
            s.SerializeValue(ref Color);
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref JoinOrder);
        }

        public bool Equals(LobbyPlayerData o) =>
            ClientId == o.ClientId && Name.Equals(o.Name) && Sled == o.Sled && Color == o.Color && Ready == o.Ready && JoinOrder == o.JoinOrder;

        public override bool Equals(object obj) => obj is LobbyPlayerData o && Equals(o);
        public override int GetHashCode() => ClientId.GetHashCode();
    }

    /// <summary>Host-controlled race settings (replicated in NetworkLobby.Settings).</summary>
    public struct LobbySettingsData : INetworkSerializable, IEquatable<LobbySettingsData>
    {
        public int TrackIndex;
        public int Laps;
        public int Bots;
        public int Difficulty; // BotDifficulty

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref TrackIndex);
            s.SerializeValue(ref Laps);
            s.SerializeValue(ref Bots);
            s.SerializeValue(ref Difficulty);
        }

        public bool Equals(LobbySettingsData o) => TrackIndex == o.TrackIndex && Laps == o.Laps && Bots == o.Bots && Difficulty == o.Difficulty;
        public override bool Equals(object obj) => obj is LobbySettingsData o && Equals(o);
        public override int GetHashCode() => (TrackIndex, Laps, Bots, Difficulty).GetHashCode();
    }

    /// <summary>Spawn description of one race sled (players and bots), replicated in NetworkSled.Info.</summary>
    public struct SledSpawnInfo : INetworkSerializable, IEquatable<SledSpawnInfo>
    {
        public byte Slot;
        public byte Sled;
        public byte Color;
        public byte Helmet;
        public bool IsBot;
        public FixedString32Bytes Name;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Slot);
            s.SerializeValue(ref Sled);
            s.SerializeValue(ref Color);
            s.SerializeValue(ref Helmet);
            s.SerializeValue(ref IsBot);
            s.SerializeValue(ref Name);
        }

        public bool Equals(SledSpawnInfo o) =>
            Slot == o.Slot && Sled == o.Sled && Color == o.Color && Helmet == o.Helmet && IsBot == o.IsBot && Name.Equals(o.Name);
        public override bool Equals(object obj) => obj is SledSpawnInfo o && Equals(o);
        public override int GetHashCode() => Slot;
    }
}

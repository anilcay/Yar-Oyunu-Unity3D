using System;
using System.Collections.Generic;
using LugeRush.Race;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Network
{
    /// <summary>Plain lobby member (no netcode types) used by <see cref="LobbyRules"/>.</summary>
    public struct LobbyMember
    {
        public ulong ClientId;
        public string Name;
        public SledType Sled;
        public SledColor Color;
        public bool Ready;
        public int JoinOrder;
        public bool IsHost;
    }

    /// <summary>One grid slot of the race roster built from the lobby.</summary>
    public struct RosterEntry
    {
        public int Slot;
        public bool IsBot;
        public ulong ClientId;   // owner (server for bots)
        public string Name;
        public SledType Sled;
        public SledColor Color;
        public int Helmet;
    }

    /// <summary>
    /// Pure lobby logic (capacity, bot fill, ready/start rules, roster order). No Unity/netcode state,
    /// so it is unit tested in EditMode and shared by NetworkLobby and the menus.
    /// </summary>
    public static class LobbyRules
    {
        public const int Capacity = RaceConfig.MaxSleds;
        public const int MaxNameLength = 16;

        public static readonly string[] BotNames = { "Ayşe", "Mert", "Zeynep", "Can", "Elif", "Burak", "Deniz" };

        public enum JoinResult { Ok, Full, RaceInProgress, SinglePlayer }

        public static JoinResult CanJoin(int humans, bool raceInProgress, bool singlePlayer)
        {
            if (singlePlayer) return JoinResult.SinglePlayer;
            if (raceInProgress) return JoinResult.RaceInProgress;
            if (humans >= Capacity) return JoinResult.Full;
            return JoinResult.Ok;
        }

        public static string JoinResultMessage(JoinResult r)
        {
            switch (r)
            {
                case JoinResult.Full: return "Oda dolu (8/8).";
                case JoinResult.RaceInProgress: return "Yarış başladı, odaya şu an katılınamaz.";
                case JoinResult.SinglePlayer: return "Bu oyun tek oyunculu.";
                default: return "";
            }
        }

        /// <summary>Bots actually used: the requested count, limited by the free slots.</summary>
        public static int EffectiveBots(int humans, int requestedBots) =>
            Mathf.Clamp(requestedBots, 0, Mathf.Max(0, Capacity - Mathf.Max(0, humans)));

        /// <summary>The host may start when every other human is ready (the host's own flag is implicit).</summary>
        public static bool CanStart(IReadOnlyList<LobbyMember> members)
        {
            if (members == null || members.Count == 0) return false;
            foreach (var m in members)
                if (!m.IsHost && !m.Ready) return false;
            return true;
        }

        public static int ReadyCount(IReadOnlyList<LobbyMember> members)
        {
            int n = 0;
            foreach (var m in members) if (m.Ready || m.IsHost) n++;
            return n;
        }

        public static LobbySettingsData Clamp(LobbySettingsData s, int trackCount)
        {
            s.TrackIndex = Mathf.Clamp(s.TrackIndex, 0, Mathf.Max(0, trackCount - 1));
            s.Laps = Mathf.Clamp(s.Laps, RaceConfig.MinLaps, RaceConfig.MaxLaps);
            s.Bots = Mathf.Clamp(s.Bots, 0, Capacity - 1);
            s.Difficulty = Mathf.Clamp(s.Difficulty, 0, 2);
            return s;
        }

        public static string SanitizeName(string name)
        {
            name = (name ?? "").Trim();
            var chars = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (!char.IsControl(c) && c != '<' && c != '>') chars.Append(c);
            name = chars.ToString();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            return string.IsNullOrWhiteSpace(name) ? "Oyuncu" : name;
        }

        /// <summary>Makes <paramref name="name"/> unique among <paramref name="taken"/> ("Ali" -> "Ali 2").</summary>
        public static string UniqueName(string name, ICollection<string> taken)
        {
            name = SanitizeName(name);
            if (taken == null || !taken.Contains(name)) return name;
            for (int i = 2; i < 100; i++)
            {
                string suffix = " " + i;
                string baseName = name.Length + suffix.Length > MaxNameLength ? name.Substring(0, MaxNameLength - suffix.Length) : name;
                string candidate = baseName + suffix;
                if (!taken.Contains(candidate)) return candidate;
            }
            return name;
        }

        /// <summary>
        /// Grid order: humans by join order (host first), then bots. Bot sleds/colours are seeded so every
        /// rematch with the same lobby gives the same field.
        /// </summary>
        public static List<RosterEntry> BuildRoster(IReadOnlyList<LobbyMember> members, int requestedBots, ulong serverClientId, int seed = 1234)
        {
            var humans = new List<LobbyMember>(members);
            humans.Sort((a, b) =>
            {
                if (a.IsHost != b.IsHost) return a.IsHost ? -1 : 1;
                return a.JoinOrder.CompareTo(b.JoinOrder);
            });
            if (humans.Count > Capacity) humans.RemoveRange(Capacity, humans.Count - Capacity);

            var roster = new List<RosterEntry>(Capacity);
            for (int i = 0; i < humans.Count; i++)
            {
                var h = humans[i];
                roster.Add(new RosterEntry
                {
                    Slot = i, IsBot = false, ClientId = h.ClientId, Name = SanitizeName(h.Name),
                    Sled = h.Sled, Color = h.Color, Helmet = i
                });
            }

            int bots = EffectiveBots(humans.Count, requestedBots);
            var rng = new System.Random(seed);
            for (int b = 0; b < bots; b++)
            {
                int slot = roster.Count;
                roster.Add(new RosterEntry
                {
                    Slot = slot, IsBot = true, ClientId = serverClientId, Name = BotNames[b % BotNames.Length],
                    Sled = (SledType)rng.Next(0, 4), Color = (SledColor)((b + 1) % 4), Helmet = slot + 1
                });
            }
            return roster;
        }
    }
}

using System;
using LugeRush.Vehicle;

namespace LugeRush.Race
{
    public enum BotDifficulty { Easy = 0, Normal = 1, Hard = 2 }

    public enum RaceState { Waiting, Countdown, Racing, Finished }

    [Serializable]
    public class RaceConfig
    {
        public const int MinLaps = 1;
        public const int MaxLaps = 10;
        public const int MaxSleds = 8;

        public string TrackId = "dev_greybox";
        public int Laps = 3;
        public int BotCount = 7;
        public BotDifficulty Difficulty = BotDifficulty.Normal;

        public RaceConfig Clone() => (RaceConfig)MemberwiseClone();
    }

    /// <summary>
    /// Selections carried from the menu into the race scene (map, laps, bots, difficulty, sled).
    /// Static so it survives scene loads; the network phase will fill it from the session host.
    /// </summary>
    public static class RaceSession
    {
        public static RaceConfig Config = new RaceConfig();
        public static SledType PlayerSled = SledType.Classic;
        public static SledColor PlayerColor = SledColor.Orange;
        public static string PlayerName = "Oyuncu";
        /// <summary>Drive the local player with AI (automated tests, attract mode).</summary>
        public static bool PlayerAutopilot;

        public static void Reset()
        {
            Config = new RaceConfig();
            PlayerSled = SledType.Classic;
            PlayerColor = SledColor.Orange;
            PlayerName = "Oyuncu";
            PlayerAutopilot = false;
        }
    }
}

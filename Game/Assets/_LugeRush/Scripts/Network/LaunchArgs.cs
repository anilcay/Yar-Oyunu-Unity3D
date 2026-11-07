using System;
using System.Globalization;

namespace LugeRush.Network
{
    /// <summary>
    /// Command line options for automation and testing:
    /// <code>
    /// -lr-autohost                 host a direct-IP room, start when -lr-expect-players are in and ready
    /// -lr-autojoin ip:port         join a direct-IP room and set ready
    /// -lr-autosingle               single player race straight from the menu
    /// -lr-port 7777                host port
    /// -lr-laps N  -lr-bots N  -lr-difficulty 0..2  -lr-track id  -lr-sled 0..3  -lr-color 0..3
    /// -lr-name Name                player name (not saved)
    /// -lr-expect-players N         (host) humans required before the start (default 1)
    /// -lr-autopilot                local player driven by AI (implied by the auto modes)
    /// -lr-autoquit-after-race      quit a few seconds after the results screen
    /// -lr-race-timeout S           (automation) give up after S seconds in the race scene
    /// -lr-offline                  do not initialise Unity Gaming Services
    /// -lr-screenshots DIR          capture ui_*.png screenshots during the automated run
    /// -lr-flow                     also exercise pause, restart, rematch, back to lobby / menu
    /// -lr-record DIR               write numbered JPG frames at a fixed frame rate, then quit
    /// -lr-record-from S            race time to start at (negative: from the countdown)
    /// -lr-record-secs S            clip length in seconds
    /// -lr-record-fps N             clip frame rate
    /// -lr-first-person             use the first person camera
    /// </code>
    /// </summary>
    public static class LaunchArgs
    {
        public static bool AutoHost { get; private set; }
        public static string AutoJoin { get; private set; }
        public static bool AutoSingle { get; private set; }
        public static int Port { get; private set; } = 7777;
        public static int Laps { get; private set; } = -1;
        public static int Bots { get; private set; } = -1;
        public static int Difficulty { get; private set; } = -1;
        public static string TrackId { get; private set; }
        public static int Sled { get; private set; } = -1;
        public static int Color { get; private set; } = -1;
        public static string Name { get; private set; }
        public static int ExpectPlayers { get; private set; } = 1;
        public static bool Autopilot { get; private set; }
        public static bool AutoQuitAfterRace { get; private set; }
        public static float RaceTimeout { get; private set; } = 360f;
        public static bool Offline { get; private set; }
        public static string ScreenshotDir { get; private set; }
        /// <summary>Automation also walks the pause, rematch and exit flows and logs "[LR] AUTO FLOW" results.</summary>
        public static bool Flow { get; private set; }
        public static string RecordDir { get; private set; }
        public static int RecordFrom { get; private set; } = -1;
        public static int RecordSeconds { get; private set; } = 20;
        public static int RecordFps { get; private set; } = 60;
        public static bool FirstPerson { get; private set; }

        public static bool IsAutomation => AutoHost || !string.IsNullOrEmpty(AutoJoin) || AutoSingle;

        private static bool parsed;

        public static void Parse(string[] args = null)
        {
            if (parsed && args == null) return;
            parsed = true;
            args ??= Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "-lr-autohost": AutoHost = true; break;
                    case "-lr-autojoin": AutoJoin = next; i++; break;
                    case "-lr-autosingle": AutoSingle = true; break;
                    case "-lr-port": Port = ParseInt(next, Port); i++; break;
                    case "-lr-laps": Laps = ParseInt(next, Laps); i++; break;
                    case "-lr-bots": Bots = ParseInt(next, Bots); i++; break;
                    case "-lr-difficulty": Difficulty = ParseInt(next, Difficulty); i++; break;
                    case "-lr-track": TrackId = next; i++; break;
                    case "-lr-sled": Sled = ParseInt(next, Sled); i++; break;
                    case "-lr-color": Color = ParseInt(next, Color); i++; break;
                    case "-lr-name": Name = next; i++; break;
                    case "-lr-expect-players": ExpectPlayers = Math.Max(1, ParseInt(next, ExpectPlayers)); i++; break;
                    case "-lr-autopilot": Autopilot = true; break;
                    case "-lr-autoquit-after-race": AutoQuitAfterRace = true; break;
                    case "-lr-race-timeout": RaceTimeout = ParseInt(next, (int)RaceTimeout); i++; break;
                    case "-lr-offline": Offline = true; break;
                    case "-lr-screenshots": ScreenshotDir = next; i++; break;
                    case "-lr-flow": Flow = true; break;
                    case "-lr-record": RecordDir = next; i++; break;
                    case "-lr-record-from": RecordFrom = ParseInt(next, RecordFrom); i++; break;
                    case "-lr-record-secs": RecordSeconds = ParseInt(next, RecordSeconds); i++; break;
                    case "-lr-record-fps": RecordFps = ParseInt(next, RecordFps); i++; break;
                    case "-lr-first-person": FirstPerson = true; break;
                }
            }
            if (IsAutomation) Autopilot = true;
        }

        public static bool TryParseAddress(string text, int defaultPort, out string ip, out ushort port)
        {
            ip = null;
            port = (ushort)defaultPort;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0)
            {
                if (!ushort.TryParse(text.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port == 0) return false;
                ip = text.Substring(0, colon);
            }
            else ip = text;
            if (ip == "localhost") ip = "127.0.0.1";
            return System.Net.IPAddress.TryParse(ip, out _);
        }

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }
}

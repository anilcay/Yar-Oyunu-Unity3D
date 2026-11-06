using System;
using System.Collections.Generic;
using LugeRush.Race;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.UI
{
    /// <summary>Key/value persistence used by <see cref="GameSettings"/> (PlayerPrefs in the game, memory in tests).</summary>
    public interface ISettingsStore
    {
        bool HasKey(string key);
        int GetInt(string key, int fallback);
        float GetFloat(string key, float fallback);
        string GetString(string key, string fallback);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
        void Save();
    }

    public class PlayerPrefsStore : ISettingsStore
    {
        private readonly string prefix;
        public PlayerPrefsStore(string prefix = "lr.") { this.prefix = prefix; }
        public bool HasKey(string key) => PlayerPrefs.HasKey(prefix + key);
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(prefix + key, fallback);
        public float GetFloat(string key, float fallback) => PlayerPrefs.GetFloat(prefix + key, fallback);
        public string GetString(string key, string fallback) => PlayerPrefs.GetString(prefix + key, fallback);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(prefix + key, value);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(prefix + key, value);
        public void SetString(string key, string value) => PlayerPrefs.SetString(prefix + key, value);
        public void Save() => PlayerPrefs.Save();
        public void DeleteAll(IEnumerable<string> keys) { foreach (var k in keys) PlayerPrefs.DeleteKey(prefix + k); }
    }

    public class MemorySettingsStore : ISettingsStore
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public int SaveCount { get; private set; }
        public bool HasKey(string key) => Values.ContainsKey(key);
        public int GetInt(string key, int fallback) => Values.TryGetValue(key, out var v) && v is int i ? i : fallback;
        public float GetFloat(string key, float fallback) => Values.TryGetValue(key, out var v) && v is float f ? f : fallback;
        public string GetString(string key, string fallback) => Values.TryGetValue(key, out var v) && v is string s ? s : fallback;
        public void SetInt(string key, int value) => Values[key] = value;
        public void SetFloat(string key, float value) => Values[key] = value;
        public void SetString(string key, string value) => Values[key] = value;
        public void Save() => SaveCount++;
    }

    public enum GameLanguage { Turkish = 0, English = 1 }

    /// <summary>
    /// Player options (graphics, audio, language, profile) and the last menu selections. Load/Save through
    /// an <see cref="ISettingsStore"/>; <see cref="Apply"/> pushes graphics/audio to the engine.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public const int Version = 1;

        // Profile
        public string PlayerName = "Oyuncu";
        // Graphics (ResolutionWidth/Height = 0 -> keep current)
        public int ResolutionWidth;
        public int ResolutionHeight;
        public bool Fullscreen = true;
        public int QualityLevel = -1; // -1 = keep project default
        // Audio 0..1
        public float MasterVolume = 0.8f;
        public float MusicVolume = 0.7f;
        public float SfxVolume = 0.8f;
        public GameLanguage Language = GameLanguage.Turkish;
        // Last menu choices
        public SledType Sled = SledType.Classic;
        public SledColor Color = SledColor.Orange;
        public string TrackId = "forest";
        public int Laps = 3;
        public int Bots = 7;
        public BotDifficulty Difficulty = BotDifficulty.Normal;
        public string LastJoinAddress = "127.0.0.1:7777";

        public static readonly string[] Keys =
        {
            "version", "name", "resW", "resH", "fullscreen", "quality", "volMaster", "volMusic", "volSfx", "lang",
            "sled", "color", "track", "laps", "bots", "difficulty", "joinAddr"
        };

        /// <summary>Automation / screenshot runs keep the window given on the command line.</summary>
        public static bool SkipDisplayApply;

        private static GameSettings current;
        private static ISettingsStore defaultStore;

        /// <summary>Process-wide settings, loaded lazily from PlayerPrefs.</summary>
        public static GameSettings Current
        {
            get
            {
                if (current == null) current = Load(DefaultStore);
                return current;
            }
            set => current = value;
        }

        public static ISettingsStore DefaultStore
        {
            get => defaultStore ??= new PlayerPrefsStore();
            set => defaultStore = value;
        }

        public static GameSettings Load(ISettingsStore store)
        {
            var s = new GameSettings();
            if (store == null) return s;
            s.PlayerName = store.GetString("name", s.PlayerName);
            s.ResolutionWidth = store.GetInt("resW", s.ResolutionWidth);
            s.ResolutionHeight = store.GetInt("resH", s.ResolutionHeight);
            s.Fullscreen = store.GetInt("fullscreen", s.Fullscreen ? 1 : 0) != 0;
            s.QualityLevel = store.GetInt("quality", s.QualityLevel);
            s.MasterVolume = store.GetFloat("volMaster", s.MasterVolume);
            s.MusicVolume = store.GetFloat("volMusic", s.MusicVolume);
            s.SfxVolume = store.GetFloat("volSfx", s.SfxVolume);
            s.Language = (GameLanguage)store.GetInt("lang", (int)s.Language);
            s.Sled = (SledType)store.GetInt("sled", (int)s.Sled);
            s.Color = (SledColor)store.GetInt("color", (int)s.Color);
            s.TrackId = store.GetString("track", s.TrackId);
            s.Laps = store.GetInt("laps", s.Laps);
            s.Bots = store.GetInt("bots", s.Bots);
            s.Difficulty = (BotDifficulty)store.GetInt("difficulty", (int)s.Difficulty);
            s.LastJoinAddress = store.GetString("joinAddr", s.LastJoinAddress);
            s.Sanitize();
            return s;
        }

        public void Save(ISettingsStore store = null)
        {
            store ??= DefaultStore;
            Sanitize();
            store.SetInt("version", Version);
            store.SetString("name", PlayerName);
            store.SetInt("resW", ResolutionWidth);
            store.SetInt("resH", ResolutionHeight);
            store.SetInt("fullscreen", Fullscreen ? 1 : 0);
            store.SetInt("quality", QualityLevel);
            store.SetFloat("volMaster", MasterVolume);
            store.SetFloat("volMusic", MusicVolume);
            store.SetFloat("volSfx", SfxVolume);
            store.SetInt("lang", (int)Language);
            store.SetInt("sled", (int)Sled);
            store.SetInt("color", (int)Color);
            store.SetString("track", TrackId ?? "");
            store.SetInt("laps", Laps);
            store.SetInt("bots", Bots);
            store.SetInt("difficulty", (int)Difficulty);
            store.SetString("joinAddr", LastJoinAddress ?? "");
            store.Save();
        }

        /// <summary>Clamps every field into its valid range (corrupt or hand-edited prefs).</summary>
        public void Sanitize()
        {
            PlayerName = Network.LobbyRules.SanitizeName(PlayerName);
            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SfxVolume = Mathf.Clamp01(SfxVolume);
            if (!Enum.IsDefined(typeof(GameLanguage), Language)) Language = GameLanguage.Turkish;
            if (!Enum.IsDefined(typeof(SledType), Sled)) Sled = SledType.Classic;
            if (!Enum.IsDefined(typeof(SledColor), Color)) Color = SledColor.Orange;
            if (!Enum.IsDefined(typeof(BotDifficulty), Difficulty)) Difficulty = BotDifficulty.Normal;
            Laps = Mathf.Clamp(Laps, RaceConfig.MinLaps, RaceConfig.MaxLaps);
            Bots = Mathf.Clamp(Bots, 0, RaceConfig.MaxSleds - 1);
            if (ResolutionWidth < 0 || ResolutionHeight < 0) ResolutionWidth = ResolutionHeight = 0;
            if (QualityLevel < -1) QualityLevel = -1;
            if (string.IsNullOrEmpty(TrackId)) TrackId = "forest";
        }

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        /// <summary>Pushes graphics and audio options to the engine (skipped in batch mode for graphics).</summary>
        public void Apply()
        {
            LugeRush.Audio.GameAudio.ApplyVolumes(this);
            if (Application.isBatchMode) return;
            if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != QualityLevel)
                QualitySettings.SetQualityLevel(QualityLevel, true);
            if (!Application.isEditor && !SkipDisplayApply)
            {
                var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (ResolutionWidth > 0 && ResolutionHeight > 0)
                {
                    if (Screen.width != ResolutionWidth || Screen.height != ResolutionHeight || Screen.fullScreenMode != mode)
                        Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
                }
                else if (Screen.fullScreenMode != mode)
                {
                    Screen.fullScreenMode = mode;
                }
            }
        }

        /// <summary>Copies the race choices into RaceSession (single player and the local lobby profile).</summary>
        public void ApplyToRaceSession()
        {
            RaceSession.PlayerName = PlayerName;
            RaceSession.PlayerSled = Sled;
            RaceSession.PlayerColor = Color;
            RaceSession.Config.TrackId = TrackId;
            RaceSession.Config.Laps = Laps;
            RaceSession.Config.BotCount = Bots;
            RaceSession.Config.Difficulty = Difficulty;
        }
    }
}

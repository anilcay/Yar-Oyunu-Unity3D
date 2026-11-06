using System.Collections.Generic;
using System.Linq;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.UI;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEngine;

namespace LugeRush.Tests
{
    public class GameSettingsTests
    {
        [Test]
        public void Defaults_WhenStoreIsEmpty()
        {
            var s = GameSettings.Load(new MemorySettingsStore());
            Assert.AreEqual("Oyuncu", s.PlayerName);
            Assert.AreEqual(3, s.Laps);
            Assert.AreEqual(SledType.Classic, s.Sled);
            Assert.AreEqual(GameLanguage.Turkish, s.Language);
            Assert.IsTrue(s.Fullscreen);
            Assert.AreEqual("forest", s.TrackId);
        }

        [Test]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            var store = new MemorySettingsStore();
            var a = new GameSettings
            {
                PlayerName = "Çağrı Şükrü", ResolutionWidth = 1600, ResolutionHeight = 900, Fullscreen = false, QualityLevel = 1,
                MasterVolume = 0.3f, MusicVolume = 0.2f, SfxVolume = 0.9f, Language = GameLanguage.English,
                Sled = SledType.Mini, Color = SledColor.Turquoise, TrackId = "alpine", Laps = 10, Bots = 0,
                Difficulty = BotDifficulty.Hard, LastJoinAddress = "10.0.0.5:7778"
            };
            a.Save(store);
            Assert.AreEqual(1, store.SaveCount);
            var b = GameSettings.Load(store);
            Assert.AreEqual(a.PlayerName, b.PlayerName);
            Assert.AreEqual(1600, b.ResolutionWidth);
            Assert.AreEqual(900, b.ResolutionHeight);
            Assert.IsFalse(b.Fullscreen);
            Assert.AreEqual(1, b.QualityLevel);
            Assert.AreEqual(0.3f, b.MasterVolume, 1e-5f);
            Assert.AreEqual(0.2f, b.MusicVolume, 1e-5f);
            Assert.AreEqual(0.9f, b.SfxVolume, 1e-5f);
            Assert.AreEqual(GameLanguage.English, b.Language);
            Assert.AreEqual(SledType.Mini, b.Sled);
            Assert.AreEqual(SledColor.Turquoise, b.Color);
            Assert.AreEqual("alpine", b.TrackId);
            Assert.AreEqual(10, b.Laps);
            Assert.AreEqual(0, b.Bots);
            Assert.AreEqual(BotDifficulty.Hard, b.Difficulty);
            Assert.AreEqual("10.0.0.5:7778", b.LastJoinAddress);
        }

        [Test]
        public void Load_ClampsCorruptValues()
        {
            var store = new MemorySettingsStore();
            store.SetInt("laps", 99);
            store.SetInt("bots", -4);
            store.SetFloat("volMaster", 7f);
            store.SetInt("sled", 42);
            store.SetInt("difficulty", 9);
            store.SetString("name", "   ");
            var s = GameSettings.Load(store);
            Assert.AreEqual(RaceConfig.MaxLaps, s.Laps);
            Assert.AreEqual(0, s.Bots);
            Assert.AreEqual(1f, s.MasterVolume);
            Assert.AreEqual(SledType.Classic, s.Sled);
            Assert.AreEqual(BotDifficulty.Normal, s.Difficulty);
            Assert.AreEqual("Oyuncu", s.PlayerName);
        }

        [Test]
        public void PlayerPrefsStore_RoundTrip()
        {
            var store = new PlayerPrefsStore("lr.test.");
            try
            {
                var a = new GameSettings { PlayerName = "İğdeli", Laps = 7, Color = SledColor.Red };
                a.Save(store);
                var b = GameSettings.Load(store);
                Assert.AreEqual("İğdeli", b.PlayerName);
                Assert.AreEqual(7, b.Laps);
                Assert.AreEqual(SledColor.Red, b.Color);
            }
            finally
            {
                store.DeleteAll(GameSettings.Keys);
            }
        }

        [Test]
        public void ApplyToRaceSession_CopiesChoices()
        {
            RaceSession.Reset();
            try
            {
                new GameSettings { PlayerName = "Ece", Sled = SledType.Sport, Color = SledColor.Yellow, Laps = 5, Bots = 2, TrackId = "dev_greybox", Difficulty = BotDifficulty.Easy }
                    .ApplyToRaceSession();
                Assert.AreEqual("Ece", RaceSession.PlayerName);
                Assert.AreEqual(SledType.Sport, RaceSession.PlayerSled);
                Assert.AreEqual(SledColor.Yellow, RaceSession.PlayerColor);
                Assert.AreEqual(5, RaceSession.Config.Laps);
                Assert.AreEqual(2, RaceSession.Config.BotCount);
                Assert.AreEqual(BotDifficulty.Easy, RaceSession.Config.Difficulty);
            }
            finally { RaceSession.Reset(); }
        }
    }

    public class LobbyRulesTests
    {
        private static LobbyMember M(ulong id, string name, bool ready = false, int order = 0, bool host = false) =>
            new LobbyMember { ClientId = id, Name = name, Ready = ready, JoinOrder = order, IsHost = host, Sled = SledType.Classic, Color = SledColor.Orange };

        [Test]
        public void BotsFillFreeSlots_UpToCapacity()
        {
            Assert.AreEqual(7, LobbyRules.EffectiveBots(1, 7));
            Assert.AreEqual(5, LobbyRules.EffectiveBots(3, 7));
            Assert.AreEqual(0, LobbyRules.EffectiveBots(8, 3));
            Assert.AreEqual(2, LobbyRules.EffectiveBots(2, 2));
            Assert.AreEqual(0, LobbyRules.EffectiveBots(2, -1));
        }

        [Test]
        public void Join_IsRefusedWhenFullRacingOrSinglePlayer()
        {
            Assert.AreEqual(LobbyRules.JoinResult.Ok, LobbyRules.CanJoin(3, false, false));
            Assert.AreEqual(LobbyRules.JoinResult.Full, LobbyRules.CanJoin(8, false, false));
            Assert.AreEqual(LobbyRules.JoinResult.RaceInProgress, LobbyRules.CanJoin(2, true, false));
            Assert.AreEqual(LobbyRules.JoinResult.SinglePlayer, LobbyRules.CanJoin(1, false, true));
            Assert.IsNotEmpty(LobbyRules.JoinResultMessage(LobbyRules.JoinResult.RaceInProgress));
        }

        [Test]
        public void Start_RequiresEveryClientReady_HostImplicit()
        {
            var members = new List<LobbyMember> { M(0, "Host", false, 0, true), M(1, "A", true, 1), M(2, "B", false, 2) };
            Assert.IsFalse(LobbyRules.CanStart(members));
            members[2] = M(2, "B", true, 2);
            Assert.IsTrue(LobbyRules.CanStart(members));
            Assert.AreEqual(3, LobbyRules.ReadyCount(members));
            Assert.IsTrue(LobbyRules.CanStart(new List<LobbyMember> { M(0, "Solo", false, 0, true) }));
            Assert.IsFalse(LobbyRules.CanStart(new List<LobbyMember>()));
        }

        [Test]
        public void Roster_HostFirstThenJoinOrderThenBots()
        {
            var members = new List<LobbyMember> { M(5, "Late", true, 3), M(2, "Early", true, 1), M(0, "Host", false, 0, true) };
            var roster = LobbyRules.BuildRoster(members, 7, 0);
            Assert.AreEqual(8, roster.Count, "3 humans + 5 bots (capacity 8)");
            Assert.AreEqual("Host", roster[0].Name);
            Assert.AreEqual("Early", roster[1].Name);
            Assert.AreEqual("Late", roster[2].Name);
            Assert.IsTrue(roster.Skip(3).All(r => r.IsBot && r.ClientId == 0));
            Assert.AreEqual(Enumerable.Range(0, 8), roster.Select(r => r.Slot));
            Assert.AreEqual(5UL, roster[2].ClientId);
            // Deterministic bots for rematches.
            var again = LobbyRules.BuildRoster(members, 7, 0);
            Assert.AreEqual(roster.Select(r => r.Sled), again.Select(r => r.Sled));
        }

        [Test]
        public void Settings_AreClamped()
        {
            var s = LobbyRules.Clamp(new LobbySettingsData { TrackIndex = 9, Laps = 0, Bots = 12, Difficulty = 5 }, 4);
            Assert.AreEqual(3, s.TrackIndex);
            Assert.AreEqual(RaceConfig.MinLaps, s.Laps);
            Assert.AreEqual(7, s.Bots);
            Assert.AreEqual(2, s.Difficulty);
            s = LobbyRules.Clamp(new LobbySettingsData { TrackIndex = 1, Laps = 11, Bots = 3, Difficulty = 1 }, 4);
            Assert.AreEqual(RaceConfig.MaxLaps, s.Laps);
        }

        [Test]
        public void Names_AreSanitizedAndUnique()
        {
            Assert.AreEqual("Oyuncu", LobbyRules.SanitizeName(""));
            Assert.AreEqual("Ali", LobbyRules.SanitizeName("  <b>Ali</b> ".Replace("<b>", "").Replace("</b>", "")));
            Assert.AreEqual("bAli/b", LobbyRules.SanitizeName("<b>Ali</b>"), "rich text tags are neutralised");
            Assert.LessOrEqual(LobbyRules.SanitizeName(new string('x', 40)).Length, LobbyRules.MaxNameLength);
            var taken = new HashSet<string> { "Ali", "Ali 2" };
            Assert.AreEqual("Ali 3", LobbyRules.UniqueName("Ali", taken));
            Assert.AreEqual("Şule", LobbyRules.UniqueName("Şule", taken));
        }

        [Test]
        public void Address_Parsing()
        {
            Assert.IsTrue(LaunchArgs.TryParseAddress("192.168.1.20:7778", 7777, out var ip, out var port));
            Assert.AreEqual("192.168.1.20", ip);
            Assert.AreEqual(7778, port);
            Assert.IsTrue(LaunchArgs.TryParseAddress("localhost", 7777, out ip, out port));
            Assert.AreEqual("127.0.0.1", ip);
            Assert.AreEqual(7777, port);
            Assert.IsFalse(LaunchArgs.TryParseAddress("not an ip", 7777, out _, out _));
            Assert.IsFalse(LaunchArgs.TryParseAddress("1.2.3.4:0", 7777, out _, out _));
        }

        [Test]
        public void LaunchArgs_ParseAutomationFlags()
        {
            LaunchArgs.Parse(new[] { "game.exe", "-lr-autohost", "-lr-laps", "1", "-lr-bots", "2", "-lr-expect-players", "2", "-lr-autoquit-after-race" });
            Assert.IsTrue(LaunchArgs.AutoHost);
            Assert.AreEqual(1, LaunchArgs.Laps);
            Assert.AreEqual(2, LaunchArgs.Bots);
            Assert.AreEqual(2, LaunchArgs.ExpectPlayers);
            Assert.IsTrue(LaunchArgs.AutoQuitAfterRace);
            Assert.IsTrue(LaunchArgs.Autopilot, "automation implies autopilot");
        }

        [Test]
        public void TrackCatalog_LookupAndAvailability()
        {
            var cat = ScriptableObject.CreateInstance<TrackCatalog>();
            try
            {
                cat.Tracks = new[]
                {
                    new TrackCatalog.Entry { TrackId = "forest", SceneName = "Track_DoesNotExist" },
                    new TrackCatalog.Entry { TrackId = "dev_greybox", SceneName = "" },
                };
                Assert.AreEqual(1, cat.IndexOf("DEV_GREYBOX"));
                Assert.AreEqual(-1, cat.IndexOf("canyon"));
                Assert.IsFalse(TrackCatalog.IsAvailable(cat.Get(0)), "scene not in build -> coming soon");
                Assert.IsFalse(TrackCatalog.IsAvailable(cat.Get(1)));
                Assert.AreSame(cat.Tracks[1], cat.Get(5), "Get clamps");
            }
            finally { Object.DestroyImmediate(cat); }
        }
    }
}

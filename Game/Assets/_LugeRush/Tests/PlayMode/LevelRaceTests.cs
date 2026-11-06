using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LugeRush.Race;
using LugeRush.Track;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>Full race on each real map: 8 sleds, 2 laps, Normal bots, accelerated time.</summary>
    public class LevelRaceTests
    {
        private const int Laps = 2;
        private const float TimeScale = 8f;
        private const float SimTimeout = 480f;
        private const float StuckWindow = 25f;
        private const float MinLap = 60f, MaxLap = 100f;
        // Lap 1 starts from rest in an eight sled pack: measured 7-19 s slower than lap 2 on the same map.
        private const float MaxFirstLap = 110f;

        [UnityTest, Timeout(900000)]
        public IEnumerator Race_TwoLaps_NormalBots(
            [Values("Track_Forest", "Track_Alpine", "Track_Canyon")] string sceneName)
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = Laps;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rm = RaceManager.Instance;
            Assert.IsNotNull(rm, "RaceManager missing in scene");
            var track = Object.FindFirstObjectByType<TrackPath>();
            Assert.IsNotNull(track);

            Time.timeScale = TimeScale;
            float t0 = Time.time;
            var monitor = SpeedSpikeMonitor.Create();
            var lastProgress = new Dictionary<RaceParticipant, float>();
            var lastProgressTime = new Dictionary<RaceParticipant, float>();
            var stuck = new HashSet<RaceParticipant>();
            var maxSpeed = new Dictionary<RaceParticipant, float>();

            while (rm.State != RaceState.Finished && Time.time - t0 < SimTimeout)
            {
                if (rm.State == RaceState.Racing)
                {
                    foreach (var p in rm.Participants)
                    {
                        maxSpeed[p] = Mathf.Max(maxSpeed.TryGetValue(p, out float ms) ? ms : 0f, p.Controller.SpeedKmh);
                        if (p.Finished) continue;
                        if (!lastProgress.TryGetValue(p, out float best) || p.RaceDistance > best + 5f)
                        {
                            lastProgress[p] = p.RaceDistance;
                            lastProgressTime[p] = Time.time;
                        }
                        else if (Time.time - lastProgressTime[p] > StuckWindow)
                        {
                            stuck.Add(p);
                        }
                    }
                }
                yield return null;
            }
            Time.timeScale = 1f;

            var sb = new StringBuilder();
            sb.AppendLine($"[LevelRace] {sceneName} state={rm.State} simTime={Time.time - t0:0.0}s trackLength={track.Length:0}m lift={track.LiftLength:0}m");
            foreach (var p in rm.Standings)
            {
                var laps = string.Join(", ", p.LapTimes.Select(l => l.ToString("0.0")));
                string stats = p.Controller != null && p.Controller.Stats != null ? p.Controller.Stats.Type.ToString() : "?";
                sb.AppendLine($"  P{p.Position} {p.DisplayName,-8} {stats,-8} finished={p.Finished} total={p.TotalTime:0.0}s laps=[{laps}] resets={p.ResetCount} dist={p.RaceDistance:0} vmax={(maxSpeed.TryGetValue(p, out float vm) ? vm : 0f):0}km/h");
            }
            sb.AppendLine("[LevelRace] " + monitor.Report());
            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());

            Assert.IsEmpty(monitor.Spikes, "Sudden speed jumps: " + monitor.Report());
            int finished = rm.Participants.Count(p => p.Finished);
            Assert.AreEqual(RaceState.Finished, rm.State, "Race did not finish within the simulated time limit");
            Assert.GreaterOrEqual(finished, 6, $"Only {finished}/8 sleds finished");
            Assert.IsEmpty(stuck.Select(p => p.DisplayName).ToList(), "Sleds made no progress for too long (stuck)");
            foreach (var p in rm.Participants.Where(p => p.Finished))
                for (int i = 0; i < p.LapTimes.Count; i++)
                {
                    float max = i == 0 ? MaxFirstLap : MaxLap;
                    Assert.That(p.LapTimes[i], Is.InRange(MinLap, max), $"{p.DisplayName} lap {i + 1} time {p.LapTimes[i]:0.0}s outside {MinLap}-{max}s");
                }
        }

        [SetUp]
        public void SetUp()
        {
            LevelTestUtil.BeginStripHud();
        }

        [TearDown]
        public void TearDown()
        {
            LevelTestUtil.EndStripHud();
            Time.timeScale = 1f;
            RaceSession.Reset();
        }
    }
}

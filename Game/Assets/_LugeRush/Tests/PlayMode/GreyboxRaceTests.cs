using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>
    /// Full race smoke test on Dev_Greybox: 8 AI-driven sleds (the player on autopilot), 2 laps, accelerated time.
    /// </summary>
    public class GreyboxRaceTests
    {
        private const string SceneName = "Dev_Greybox";
        private const int Laps = 2;
        private const float TimeScale = 8f;
        private const float SimTimeout = 480f;
        private const float StuckWindow = 25f;
        private const float MinLap = 40f, MaxLap = 120f;

        [UnityTest, Timeout(900000)]
        public IEnumerator AiRace_TwoLaps_MostSledsFinishWithinLapTimeTargets(
            [Values(BotDifficulty.Normal, BotDifficulty.Easy, BotDifficulty.Hard)] BotDifficulty difficulty)
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = Laps;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = difficulty;
            RaceSession.PlayerAutopilot = true;

            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rm = RaceManager.Instance;
            Assert.IsNotNull(rm, "RaceManager missing in scene");
            var track = Object.FindFirstObjectByType<TrackPath>();
            Assert.IsNotNull(track);

            Time.timeScale = TimeScale;
            float t0 = Time.time;
            var lastProgress = new Dictionary<RaceParticipant, float>();
            var lastProgressTime = new Dictionary<RaceParticipant, float>();
            var stuck = new HashSet<RaceParticipant>();
            var maxSpeed = new Dictionary<RaceParticipant, float>();
            var boosts = new Dictionary<RaceParticipant, Dictionary<SledBoostKind, int>>();
            foreach (var p in rm.Participants)
            {
                var counts = new Dictionary<SledBoostKind, int>();
                boosts[p] = counts;
                p.Controller.BoostStarted += kind => counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
            }

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
            var finishers = rm.Participants.Where(p => p.Finished).Select(p => p.TotalTime).ToList();
            float spread = finishers.Count > 1 ? finishers.Max() - finishers.Min() : 0f;
            sb.AppendLine($"[GreyboxRace] difficulty={difficulty} state={rm.State} simTime={Time.time - t0:0.0}s trackLength={track.Length:0}m lift={track.LiftLength:0}m finishSpread={spread:0.0}s ({100f * spread / Mathf.Max(1f, finishers.DefaultIfEmpty(1f).Min()):0.0}%)");
            foreach (var p in rm.Standings)
            {
                var laps = string.Join(", ", p.LapTimes.Select(l => l.ToString("0.0")));
                string stats = p.Controller != null && p.Controller.Stats != null ? p.Controller.Stats.Type.ToString() : "?";
                sb.AppendLine($"  P{p.Position} {p.DisplayName,-8} {stats,-8} finished={p.Finished} total={p.TotalTime:0.0}s laps=[{laps}] best={p.BestLapTime:0.0} resets={p.ResetCount} dist={p.RaceDistance:0} vmax={(maxSpeed.TryGetValue(p, out float vm) ? vm : 0f):0}km/h boosts[{string.Join(" ", boosts[p].Select(kv => kv.Key + ":" + kv.Value))}]");
            }
            Debug.Log(sb.ToString());
            TestContext.WriteLine(sb.ToString());

            int finished = rm.Participants.Count(p => p.Finished);
            Assert.AreEqual(RaceState.Finished, rm.State, "Race did not finish within the simulated time limit");
            Assert.AreEqual(8, finished, $"Only {finished}/8 sleds finished inside the grace period");
            Assert.IsEmpty(stuck.Select(p => p.DisplayName).ToList(), "Sleds made no progress for too long (stuck)");
            foreach (var p in rm.Participants.Where(p => p.Finished))
                foreach (var lap in p.LapTimes)
                    Assert.That(lap, Is.InRange(MinLap, MaxLap), $"{p.DisplayName} lap time {lap:0.0}s outside {MinLap}-{MaxLap}s");
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            RaceSession.Reset();
        }
    }
}

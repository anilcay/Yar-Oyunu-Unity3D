using System.Collections;
using System.Linq;
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
    /// Deterministic lift-entry pile-up: all eight sleds are placed bumper to bumper just before LiftStart,
    /// which is the situation that made the lift teleport riders into the queue behind them
    /// (SledController "velocity spike" warning, 141/427 km/h on Track_Alpine).
    /// </summary>
    public class LiftEntryTests
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator PackedEntry_NoSpeedSpike(
            [Values("Dev_Greybox", "Track_Forest", "Track_Alpine", "Track_Canyon")] string sceneName,
            [Values(13f, 20f)] float entrySpeed)
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = 3;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rm = RaceManager.Instance;
            var track = Object.FindFirstObjectByType<TrackPath>();
            Assert.IsNotNull(rm); Assert.IsNotNull(track); Assert.IsTrue(track.HasLift);
            var lift = Object.FindFirstObjectByType<LiftSection>();
            Assert.IsNotNull(lift);

            Time.timeScale = 4f;
            float wait = Time.time;
            while (rm.State != RaceState.Racing && Time.time - wait < 20f) yield return null;
            Assert.AreEqual(RaceState.Racing, rm.State);
            yield return new WaitForFixedUpdate();

            // Two-abreast pack, closing on the lift together.
            var sleds = rm.Participants;
            for (int i = 0; i < sleds.Count; i++)
            {
                var p = sleds[i];
                int row = i / 2;
                float d = track.Wrap(track.LiftStart - 1.5f - row * 2.8f);
                float lateral = (i % 2 == 0 ? -1f : 1f) * Mathf.Min(1.6f, track.GetWidth(d) * 0.3f);
                Vector3 up = track.GetUp(d);
                Vector3 tan = track.GetTangent(d);
                var sled = p.Controller;
                sled.ResetTo(track.GetPosition(d) + track.GetRight(d) * lateral + up * sled.RestHoverHeight, Quaternion.LookRotation(tan, up));
                sled.SetVelocity(tan * entrySpeed);
                p.Follower.SetDistanceHint(d);
                rm.NotifyTeleported(p, d);
            }
            int resets = sleds.Sum(p => p.ResetCount);

            var monitor = SpeedSpikeMonitor.Create();
            float t0 = Time.time;
            float limit = track.LiftLength / lift.Speed + 12f;
            while (Time.time - t0 < limit && !sleds.All(p => Passed(track, p))) yield return new WaitForFixedUpdate();
            Time.timeScale = 1f;

            string report = $"[LiftEntry] {sceneName} entry={entrySpeed:0} simTime={Time.time - t0:0.0}s " +
                            $"newResets={sleds.Sum(p => p.ResetCount) - resets} {monitor.Report()}";
            Debug.Log(report);
            TestContext.WriteLine(report);

            Assert.AreEqual(0, monitor.WarningCount, report);
            Assert.IsEmpty(monitor.Spikes, report);
            Assert.AreEqual(resets, sleds.Sum(p => p.ResetCount), report);
            Assert.IsTrue(sleds.All(p => Passed(track, p)), "Not every sled made it over the lift. " + report);
        }

        private static bool Passed(TrackPath track, RaceParticipant p)
        {
            float past = track.SignedDelta(track.LiftEnd, p.Follower.Distance);
            return !p.Controller.IsOnLift && past > 5f && past < 200f;
        }

        [SetUp] public void SetUp() => LevelTestUtil.BeginStripHud();

        [TearDown]
        public void TearDown()
        {
            LevelTestUtil.EndStripHud();
            Time.timeScale = 1f;
            RaceSession.Reset();
        }
    }
}

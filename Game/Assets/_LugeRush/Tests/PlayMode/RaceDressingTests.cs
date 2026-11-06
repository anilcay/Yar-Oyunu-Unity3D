using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.UI;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>Visual race dressing driven from gameplay state: start gate lamps and the lift hook.</summary>
    public class RaceDressingTests
    {
        [UnityTest, Timeout(120000)]
        public IEnumerator StartGateLamps_FollowCountdown([Values("Track_Forest", "Track_Alpine", "Track_Canyon")] string sceneName)
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = 1;
            RaceSession.Config.BotCount = 1;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rm = RaceManager.Instance;
            Assert.IsNotNull(rm);
            var lights = rm.GetComponent<StartGateLights>();
            Assert.IsNotNull(lights, "StartGateLights not attached");
            Assert.AreEqual(4, lights.LampCount, "One start gate with four lamps expected");
            Assert.AreEqual(0, lights.RedsLit);
            Assert.IsFalse(lights.GreenLit);

            var seen = new List<string>();
            int ticks = 0;
            rm.CountdownTick += t =>
            {
                ticks++;
                seen.Add($"{t}:{lights.RedsLit}:{lights.GreenLit}");
            };
            Time.timeScale = 4f;
            float t0 = Time.time;
            while (rm.State != RaceState.Racing && Time.time - t0 < 20f) yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(4, ticks, string.Join(" ", seen));

            // Handlers run in subscription order, the lamps first.
            CollectionAssert.AreEqual(new[] { "3:1:False", "2:2:False", "1:3:False", "0:0:True" }, seen);
            Assert.IsTrue(Lamp("Lamp_Green").enabled, "Green lamp renderer off at GO");
            Assert.IsFalse(Lamp("Lamp_Red_1").enabled, "Red lamp renderer still on at GO");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LiftHook_LinksSledAndCable()
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = 1;
            RaceSession.Config.BotCount = 0;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene("Track_Alpine", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.IsNotNull(GameContent.Instance.LiftHookPrefab, "GameContent.LiftHookPrefab not assigned");
            var rm = RaceManager.Instance;
            var track = Object.FindFirstObjectByType<TrackPath>();
            var lift = Object.FindFirstObjectByType<LiftSection>();
            Time.timeScale = 4f;
            float wait = Time.time;
            while (rm.State != RaceState.Racing && Time.time - wait < 20f) yield return null;
            yield return new WaitForFixedUpdate();

            var p = rm.Participants[0];
            float d = track.Wrap(track.LiftStart - 2f);
            Vector3 up = track.GetUp(d), tan = track.GetTangent(d);
            p.Controller.ResetTo(track.GetPosition(d) + up * p.Controller.RestHoverHeight, Quaternion.LookRotation(tan, up));
            p.Controller.SetVelocity(tan * 15f);
            p.Follower.SetDistanceHint(d);
            rm.NotifyTeleported(p, d);

            float t0 = Time.time;
            while (track.SignedDelta(track.LiftStart, p.Follower.Distance) < 30f && Time.time - t0 < 15f) yield return new WaitForFixedUpdate();
            Assert.IsTrue(p.Controller.IsOnLift, "Sled not on the lift");

            var hooks = lift.GetComponentsInChildren<Transform>().Where(t => t.parent == lift.transform && t.name.Contains("Hook")).ToList();
            Assert.AreEqual(1, hooks.Count, "One hook per rider expected");
            Vector3 tip = hooks[0].position + hooks[0].up * (0.18f * hooks[0].lossyScale.y);
            float toSled = Vector3.Distance(tip, p.Controller.transform.position);
            Assert.Less(toSled, 0.7f, "Hook tip does not reach the sled");

            while (p.Controller.IsOnLift && Time.time - t0 < 30f) yield return null;
            Time.timeScale = 1f;
            Assert.IsFalse(p.Controller.IsOnLift);
            Assert.IsFalse(hooks[0].gameObject.activeSelf, "Hook must hide after the release");
        }

        private static Renderer Lamp(string name) =>
            Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.name == name);

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

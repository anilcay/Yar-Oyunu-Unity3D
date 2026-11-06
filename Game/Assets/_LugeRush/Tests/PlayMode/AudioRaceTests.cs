using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LugeRush.Audio;
using LugeRush.Race;
using LugeRush.UI;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>Real race with 8 sleds: sources play, follow speed and stay inside the voice budget.</summary>
    public class AudioRaceTests
    {
        private const float TimeScale = 4f;
        private const float SampleSeconds = 30f;

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [UnityTest]
        public IEnumerator Settings_ReachMixer()
        {
            var mixer = AudioLibrary.Instance.Mixer;
            yield return null;
            yield return null;
            GameAudio.ApplyVolumes(new GameSettings { MasterVolume = 0.5f, MusicVolume = 0.25f, SfxVolume = 0f });
            yield return null;
            Assert.IsTrue(mixer.GetFloat(AudioTuning.MasterParam, out float master));
            Assert.IsTrue(mixer.GetFloat(AudioTuning.MusicParam, out float music));
            Assert.IsTrue(mixer.GetFloat(AudioTuning.SfxParam, out float sfx));
            Assert.IsTrue(mixer.GetFloat(AudioTuning.UiParam, out float ui));
            Assert.AreEqual(-6.02f, master, 0.05f);
            Assert.AreEqual(-12.04f, music, 0.05f);
            Assert.AreEqual(AudioTuning.SilenceDb, sfx, 0.01f);
            Assert.AreEqual(sfx, ui, 0.01f, "UI follows the effects volume");
            GameAudio.ApplyVolumes(GameSettings.Current);
        }

        /// <summary>Same treatment NetworkSled gives a proxy: Frozen, moved by MoveOnLift, rigidbody reports nothing.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator FrozenProxy_SpeedComesFromPositions()
        {
            yield return StartRace(1);
            // Hold the whole field still so this sled stays among the nearest ones while it circles in place.
            foreach (var other in SledController.All) other.Frozen = true;
            var ear = Object.FindFirstObjectByType<AudioListener>().transform.position;
            var proxy = SledAudio.All.Where(s => !s.IsLocal).OrderBy(s => (s.transform.position - ear).sqrMagnitude).First();
            var sled = proxy.GetComponent<SledController>();
            Vector3 center = sled.Body.position;
            var rot = sled.Body.rotation;
            const float radius = 3f, speed = 15f;
            float angle = 0f;
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                angle += speed / radius * Time.fixedDeltaTime;
                var pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                sled.MoveOnLift(pos, rot, 0f); // reported speed 0: the sound must not rely on it
            }
            yield return null;
            Assert.AreEqual(15f, proxy.AudioSpeed, 1.5f);
            Assert.IsNotNull(proxy.Roll, "Proxy has no roll source");
            Assert.IsTrue(proxy.Roll.isPlaying, "Proxy roll is silent");
            Assert.Greater(proxy.Roll.pitch, AudioTuning.RollPitch(0f));
        }

        private static IEnumerator StartRace(int laps)
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = laps;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene("Track_Forest", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var rm = RaceManager.Instance;
            Assert.IsNotNull(rm);
            Time.timeScale = TimeScale;
            float t0 = Time.time;
            while (rm.State != RaceState.Racing && Time.time - t0 < 30f) yield return null;
            Assert.AreEqual(RaceState.Racing, rm.State);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.6f); // first ranking pass
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Race_SoundsFollowSpeed_WithinBudget()
        {
            RaceSession.Reset();
            RaceSession.Config.Laps = 3;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene("Track_Canyon", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var rm = RaceManager.Instance;
            Assert.IsNotNull(rm);
            var audio = GameAudio.Instance;
            Assert.IsNotNull(audio, "GameAudio must bootstrap itself");

            int beeps = 0, go = 0;
            rm.CountdownTick += t => { if (t > 0) beeps++; else go++; };

            Time.timeScale = TimeScale;
            float t0 = Time.time;
            while (rm.State != RaceState.Racing && Time.time - t0 < 30f) yield return null;
            Assert.AreEqual(RaceState.Racing, rm.State);
            yield return null;

            Assert.AreEqual(AudioLibrary.Instance.MusicRace, audio.CurrentMusic);
            Assert.AreEqual(AudioLibrary.Instance.AmbCanyon, audio.CurrentAmbience);
            Assert.IsTrue(audio.MusicPlaying, "Race music is not playing");
            Assert.Greater(beeps, 0);
            Assert.AreEqual(1, go);

            var local = SledAudio.All.FirstOrDefault(s => s.IsLocal);
            Assert.AreEqual(8, SledAudio.All.Count, "Every sled needs a SledAudio");
            Assert.IsNotNull(local, "No local sled audio");

            var samples = new List<(float speed, float pitch, float volume)>();
            int maxVoices = 0, maxRemote = 0;
            bool rollHeard = false;
            float start = Time.time;
            while (Time.time - start < SampleSeconds)
            {
                yield return new WaitForSeconds(0.1f);
                var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.isPlaying).ToArray();
                maxVoices = Mathf.Max(maxVoices, sources.Length);
                maxRemote = Mathf.Max(maxRemote, SledAudio.All.Count(s => !s.IsLocal && s.Audible));
                var roll = local.Roll;
                if (roll == null || local.AudioSpeed < 0.5f) continue;
                rollHeard |= roll.isPlaying;
                if (!local.GetComponent<SledController>().IsOnLift)
                    samples.Add((local.AudioSpeed, roll.pitch, roll.volume));
            }

            Debug.Log($"[Audio] voices max {maxVoices}/{AudioTuning.MaxVoices}, audible remote max {maxRemote}, samples {samples.Count}");
            Assert.IsTrue(rollHeard, "Roll loop never played");
            Assert.LessOrEqual(maxVoices, AudioTuning.MaxVoices, "Voice budget exceeded");
            Assert.LessOrEqual(maxRemote, AudioTuning.MaxAudibleRemoteSleds);
            Assert.Greater(samples.Count, 50);

            var sorted = samples.OrderBy(s => s.speed).ToList();
            int q = sorted.Count / 4;
            var slow = sorted.Take(q).ToList();
            var fast = sorted.Skip(sorted.Count - q).ToList();
            Debug.Log($"[Audio] slow {slow.Average(s => s.speed):0.0} m/s pitch {slow.Average(s => s.pitch):0.00} vol {slow.Average(s => s.volume):0.00}; " +
                      $"fast {fast.Average(s => s.speed):0.0} m/s pitch {fast.Average(s => s.pitch):0.00} vol {fast.Average(s => s.volume):0.00}");
            Assert.Greater(fast.Average(s => s.speed), slow.Average(s => s.speed) + 2f, "Sample has no speed range");
            Assert.Greater(fast.Average(s => s.pitch), slow.Average(s => s.pitch));
            Assert.Greater(fast.Average(s => s.volume), slow.Average(s => s.volume));
        }
    }
}

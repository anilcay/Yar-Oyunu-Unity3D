using System.Linq;
using System.Reflection;
using LugeRush.Audio;
using LugeRush.Editor;
using LugeRush.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Tests
{
    public class AudioSetupTests
    {
        private static AudioLibrary Lib => AudioLibrary.Instance;

        [Test]
        public void Library_AllReferencesAssigned()
        {
            Assert.IsNotNull(Lib, "Resources/LugeRushAudio missing");
            foreach (var f in typeof(AudioLibrary).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(Object).IsAssignableFrom(f.FieldType))
                    Assert.IsTrue((Object)f.GetValue(Lib) != null, $"{f.Name} is not assigned");
            }
            Assert.AreEqual(3, Lib.SledImpacts.Length);
            Assert.IsTrue(Lib.SledImpacts.All(c => c != null));
        }

        [Test]
        public void Mixer_HasGroupsAndExposedParameters()
        {
            var groups = Lib.Mixer.FindMatchingGroups("").Select(g => g.name).ToArray();
            CollectionAssert.IsSupersetOf(groups, new[] { "Master", "Music", "SFX", "UI" });
            foreach (var p in new[] { AudioTuning.MasterParam, AudioTuning.MusicParam, AudioTuning.SfxParam, AudioTuning.UiParam })
                Assert.IsTrue(Lib.Mixer.GetFloat(p, out _), $"{p} is not exposed");
        }

        [Test]
        public void Settings_AttenuateOnlyThroughMixer()
        {
            GameAudio.ApplyVolumes(new GameSettings { MasterVolume = 0.5f });
            Assert.AreEqual(1f, AudioListener.volume, 1e-4f, "Master must not be applied twice");
            GameAudio.ApplyVolumes(GameSettings.Current);
        }

        [Test]
        public void ClipFormats_FollowImportRules()
        {
            foreach (var clip in new[] { Lib.MusicMenu, Lib.MusicRace, Lib.AmbForest, Lib.AmbAlpine, Lib.AmbCanyon })
            {
                var s = ((AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip))).defaultSampleSettings;
                Assert.AreEqual(AudioClipLoadType.Streaming, s.loadType, clip.name);
                Assert.AreEqual(AudioCompressionFormat.Vorbis, s.compressionFormat, clip.name);
            }
            foreach (var clip in new[] { Lib.SledRoll, Lib.UiNav, Lib.RaceBeep, Lib.SledImpacts[0] })
            {
                var s = ((AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip))).defaultSampleSettings;
                Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, s.loadType, clip.name);
            }
            foreach (var clip in new[] { Lib.SledRoll, Lib.SledWind, Lib.SledBrake, Lib.SledDrift, Lib.SledScrape, Lib.LiftChain })
                Assert.AreEqual(1, clip.channels, $"{clip.name} is used in 3D and must be mono");
            Assert.AreEqual(44100, Lib.SledRoll.frequency);
        }

        [Test]
        public void Curves_RiseWithSpeed()
        {
            float lastPitch = 0f, lastRoll = -1f, lastWind = -1f;
            for (float s = 0f; s <= 1.0001f; s += 0.05f)
            {
                Assert.Greater(AudioTuning.RollPitch(s), lastPitch);
                Assert.GreaterOrEqual(AudioTuning.RollVolume(s), lastRoll);
                Assert.GreaterOrEqual(AudioTuning.WindVolume(s), lastWind);
                lastPitch = AudioTuning.RollPitch(s);
                lastRoll = AudioTuning.RollVolume(s);
                lastWind = AudioTuning.WindVolume(s);
            }
            Assert.AreEqual(0f, AudioTuning.RollVolume(0f), 1e-4f);
            Assert.AreEqual(AudioTuning.SilenceDb, AudioTuning.ToDb(0f));
            Assert.AreEqual(0f, AudioTuning.ToDb(1f), 1e-4f);
            Assert.LessOrEqual(AudioTuning.MaxVoices, 32, "Over the project's real voice limit");
        }

        [Test]
        public void ProxySpeed_ComesFromPositions_AndIgnoresTeleports()
        {
            var e = new ProxySpeedEstimator();
            const float dt = 0.02f;
            var pos = Vector3.zero;
            for (int i = 0; i < 60; i++)
            {
                pos += Vector3.forward * (20f * dt);
                e.Push(pos, dt);
            }
            Assert.AreEqual(20f, e.Speed, 0.1f);
            pos += Vector3.right * 300f;
            e.Push(pos, dt);
            Assert.AreEqual(20f, e.Speed, 0.1f, "A respawn jump must not read as speed");
            for (int i = 0; i < 40; i++) e.Push(pos, dt);
            Assert.Less(e.Speed, 0.1f);
        }
    }
}

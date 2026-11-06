using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LugeRush.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace LugeRush.Editor
{
    /// <summary>
    /// Builds the audio mixer and the AudioLibrary asset from the WAV files made by Art/Scripts/audio_build.py.
    /// Unity has no public API to author a mixer, so its YAML is written directly with stable ids.
    /// </summary>
    public static class AudioAssetBuilder
    {
        public const string Root = EditorAssetUtil.Root + "/Audio";
        public const string MixerPath = Root + "/LugeRushMixer.mixer";
        public const string LibraryPath = Root + "/Resources/" + AudioLibrary.ResourceName + ".asset";

        /// <summary>AudioLibrary field name -> WAV path relative to the Audio folder.</summary>
        public static readonly Dictionary<string, string> ClipFiles = new Dictionary<string, string>
        {
            { nameof(AudioLibrary.SledRoll), "Sled/sled_roll" }, { nameof(AudioLibrary.SledWind), "Sled/sled_wind" },
            { nameof(AudioLibrary.SledBrake), "Sled/sled_brake" }, { nameof(AudioLibrary.SledDrift), "Sled/sled_drift" },
            { nameof(AudioLibrary.SledScrape), "Sled/sled_scrape" }, { nameof(AudioLibrary.SledBoost), "Sled/sled_boost" },
            { nameof(AudioLibrary.LiftChain), "Track/lift_chain" }, { nameof(AudioLibrary.RaceBeep), "Track/race_beep" },
            { nameof(AudioLibrary.RaceGo), "Track/race_go" }, { nameof(AudioLibrary.LapComplete), "Track/lap_complete" },
            { nameof(AudioLibrary.LapFinal), "Track/lap_final" }, { nameof(AudioLibrary.RaceFinish), "Track/race_finish" },
            { nameof(AudioLibrary.AmbForest), "Ambience/amb_forest" }, { nameof(AudioLibrary.AmbAlpine), "Ambience/amb_alpine" },
            { nameof(AudioLibrary.AmbCanyon), "Ambience/amb_canyon" }, { nameof(AudioLibrary.MusicMenu), "Music/music_menu" },
            { nameof(AudioLibrary.MusicRace), "Music/music_race" }, { nameof(AudioLibrary.UiNav), "UI/ui_nav" },
            { nameof(AudioLibrary.UiConfirm), "UI/ui_confirm" }, { nameof(AudioLibrary.UiBack), "UI/ui_back" },
            { nameof(AudioLibrary.UiNotify), "UI/ui_notify" },
        };

        private static readonly string[] ImpactFiles = { "Sled/sled_impact_light", "Sled/sled_impact_medium", "Sled/sled_impact_heavy" };

        [MenuItem("Luge Rush/Audio/Build Mixer + Library")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
            EditorAssetUtil.EnsureFolder(Root + "/Resources");
            WriteMixer();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            FillLibrary();
            AssetDatabase.SaveAssets();
            LogSizes();
        }

        private static void FillLibrary()
        {
            var lib = EditorAssetUtil.LoadOrCreate<AudioLibrary>(LibraryPath);
            lib.Mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            var groups = AssetDatabase.LoadAllAssetsAtPath(MixerPath).OfType<AudioMixerGroup>().ToList();
            lib.MusicGroup = groups.FirstOrDefault(g => g.name == "Music");
            lib.SfxGroup = groups.FirstOrDefault(g => g.name == "SFX");
            lib.UiGroup = groups.FirstOrDefault(g => g.name == "UI");
            foreach (var pair in ClipFiles)
                typeof(AudioLibrary).GetField(pair.Key).SetValue(lib, LoadClip(pair.Value));
            lib.SledImpacts = ImpactFiles.Select(LoadClip).ToArray();
            EditorUtility.SetDirty(lib);
            Debug.Log($"[LR] Audio library built: mixer={(lib.Mixer != null)}, groups={groups.Count}");
        }

        private static AudioClip LoadClip(string relative)
        {
            string path = $"{Root}/{relative}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogError($"[LR] Missing audio clip {path}: run python Art/Scripts/audio_build.py");
            return clip;
        }

        private static void LogSizes()
        {
            var compSize = typeof(AudioImporter).GetProperty("compSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            long comp = 0, orig = 0, memory = 0;
            var lines = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (AudioImporter)AssetImporter.GetAtPath(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                long c = compSize != null ? System.Convert.ToInt64(compSize.GetValue(imp)) : 0;
                comp += c;
                orig += new FileInfo(path).Length;
                var st = imp.defaultSampleSettings;
                if (st.loadType == AudioClipLoadType.DecompressOnLoad) memory += clip.samples * clip.channels * 2L;
                lines.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: {c / 1024} KB {st.compressionFormat} {st.loadType}");
            }
            Debug.Log($"[LR] Audio size: {orig / 1048576f:0.0} MB WAV -> {comp / 1048576f:0.0} MB in build, {memory / 1048576f:0.0} MB PCM decoded on load\n{lines}");
        }

        // ------------------------------------------------------------------ Mixer YAML

        private static string Id(string key)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("LugeRushMixer/" + key));
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }

        private const string Header =
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n";
        private const int MixerFile = 24100000, SnapshotFile = 24500006;

        private static void WriteMixer()
        {
            string[] names = { "Master", "Music", "SFX", "UI" };
            string[] parameters = { AudioTuning.MasterParam, AudioTuning.MusicParam, AudioTuning.SfxParam, AudioTuning.UiParam };
            int GroupFile(int i) => 24300002 + i * 2;
            int EffectFile(int i) => 24400004 + i * 2;

            var sb = new StringBuilder("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            sb.Append($"--- !u!241 &{MixerFile}\nAudioMixerController:\n{Header}  m_Name: LugeRushMixer\n  m_OutputGroup: {{fileID: 0}}\n");
            sb.Append($"  m_MasterGroup: {{fileID: {GroupFile(0)}}}\n  m_Snapshots:\n  - {{fileID: {SnapshotFile}}}\n");
            sb.Append($"  m_StartSnapshot: {{fileID: {SnapshotFile}}}\n  m_SuspendThreshold: -80\n  m_EnableSuspend: 1\n  m_UpdateMode: 0\n");
            sb.Append("  m_ExposedParameters:\n");
            for (int i = 0; i < names.Length; i++)
                sb.Append($"  - guid: {Id(names[i] + "/volume")}\n    name: {parameters[i]}\n");
            sb.Append("  m_AudioMixerGroupViews:\n  - guids:\n");
            foreach (var n in names) sb.Append($"    - {Id(n + "/group")}\n");
            sb.Append($"    name: View\n  m_CurrentViewIndex: 0\n  m_TargetSnapshot: {{fileID: {SnapshotFile}}}\n");

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                sb.Append($"--- !u!243 &{GroupFile(i)}\nAudioMixerGroupController:\n{Header}  m_Name: {n}\n  m_AudioMixer: {{fileID: {MixerFile}}}\n");
                sb.Append($"  m_GroupID: {Id(n + "/group")}\n");
                if (i == 0)
                {
                    sb.Append("  m_Children:\n");
                    for (int c = 1; c < names.Length; c++) sb.Append($"  - {{fileID: {GroupFile(c)}}}\n");
                }
                else sb.Append("  m_Children: []\n");
                sb.Append($"  m_Volume: {Id(n + "/volume")}\n  m_Pitch: {Id(n + "/pitch")}\n  m_Send: 00000000000000000000000000000000\n");
                sb.Append($"  m_Effects:\n  - {{fileID: {EffectFile(i)}}}\n  m_UserColorIndex: 0\n  m_Mute: 0\n  m_Solo: 0\n  m_BypassEffects: 0\n");

                sb.Append($"--- !u!244 &{EffectFile(i)}\nAudioMixerEffectController:\n{Header.Replace("HideFlags: 0", "HideFlags: 3")}  m_Name: \n");
                sb.Append($"  m_EffectID: {Id(n + "/effect")}\n  m_EffectName: Attenuation\n  m_MixLevel: {Id(n + "/mix")}\n");
                sb.Append("  m_Parameters: []\n  m_SendTarget: {fileID: 0}\n  m_EnableWetMix: 0\n  m_Bypass: 0\n");
            }

            sb.Append($"--- !u!245 &{SnapshotFile}\nAudioMixerSnapshotController:\n{Header}  m_Name: Snapshot\n  m_AudioMixer: {{fileID: {MixerFile}}}\n");
            sb.Append($"  m_SnapshotID: {Id("snapshot")}\n  m_FloatValues: {{}}\n  m_TransitionOverrides: {{}}\n");
            File.WriteAllText(MixerPath, sb.ToString());
        }
    }
}

using UnityEngine;
using UnityEngine.Audio;

namespace LugeRush.Audio
{
    /// <summary>
    /// Every clip and mixer reference of the game, in one Resources asset (built by AudioAssetBuilder).
    /// Field names map one-to-one to the generated WAV files (see AudioAssetBuilder.ClipFiles).
    /// </summary>
    [CreateAssetMenu(menuName = "Luge Rush/Audio Library", fileName = ResourceName)]
    public class AudioLibrary : ScriptableObject
    {
        public const string ResourceName = "LugeRushAudio";

        [Header("Mixer")]
        public AudioMixer Mixer;
        public AudioMixerGroup MusicGroup;
        public AudioMixerGroup SfxGroup;
        public AudioMixerGroup UiGroup;

        [Header("Sled loops")]
        public AudioClip SledRoll;
        public AudioClip SledWind;
        public AudioClip SledBrake;
        public AudioClip SledDrift;
        public AudioClip SledScrape;

        [Header("Sled one-shots")]
        public AudioClip SledBoost;
        [Tooltip("Light, medium, heavy.")]
        public AudioClip[] SledImpacts = new AudioClip[3];

        [Header("Track")]
        public AudioClip LiftChain;
        public AudioClip RaceBeep;
        public AudioClip RaceGo;
        public AudioClip LapComplete;
        public AudioClip LapFinal;
        public AudioClip RaceFinish;

        [Header("Ambience")]
        public AudioClip AmbForest;
        public AudioClip AmbAlpine;
        public AudioClip AmbCanyon;

        [Header("Music")]
        public AudioClip MusicMenu;
        public AudioClip MusicRace;

        [Header("UI")]
        public AudioClip UiNav;
        public AudioClip UiConfirm;
        public AudioClip UiBack;
        public AudioClip UiNotify;

        private static AudioLibrary instance;

        public static AudioLibrary Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<AudioLibrary>(ResourceName);
                return instance;
            }
        }

        /// <summary>Ambience of a track scene (null when the scene has none).</summary>
        public AudioClip AmbienceForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            if (sceneName.Contains("Forest")) return AmbForest;
            if (sceneName.Contains("Alpine")) return AmbAlpine;
            if (sceneName.Contains("Canyon")) return AmbCanyon;
            return null;
        }
    }
}

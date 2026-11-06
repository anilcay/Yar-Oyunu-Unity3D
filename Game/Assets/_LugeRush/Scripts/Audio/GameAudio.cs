using System.Collections.Generic;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.UI;
using LugeRush.Vehicle;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace LugeRush.Audio
{
    /// <summary>Priority order: when a click raises several, the highest one in that frame is played.</summary>
    public enum UiSound { None, Nav, Confirm, Back, Notify }

    /// <summary>
    /// Persistent audio director, created automatically before the first scene runs. Watches the active scene
    /// and RaceManager.Instance instead of being wired into them, so offline and network races take one path:
    /// music and ambience by scene, race events, SledAudio on every race sled, one-shot and UI voices.
    /// Silent no-op when the AudioLibrary asset is missing.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private const float CrossfadeSeconds = 1.6f;
        private const float MenuMusicTrim = 0.8f;
        private const float RaceMusicTrim = 0.62f;
        private const float AmbienceTrim = 0.9f;
        private const float RankInterval = 0.25f;
        private const float UiInterval = 0.04f;
        private const float NavSilenceAfterChange = 0.3f;
        private const float NearSpatial = 0.15f;

        private sealed class Fader
        {
            public readonly AudioSource[] Sources = new AudioSource[2];
            public int Active;
            public float Trim;

            public void Play(AudioClip clip, float trim)
            {
                Trim = trim;
                var cur = Sources[Active];
                if (cur.clip == clip && cur.isPlaying) return;
                Active = 1 - Active;
                var next = Sources[Active];
                next.Stop();
                next.clip = clip;
                next.volume = 0f;
                if (clip != null) next.Play();
            }

            public void Tick(float dt)
            {
                for (int i = 0; i < 2; i++)
                {
                    var s = Sources[i];
                    float target = i == Active && s.clip != null ? Trim : 0f;
                    s.volume = Mathf.MoveTowards(s.volume, target, dt / CrossfadeSeconds);
                    if (s.volume <= 0f && i != Active && s.isPlaying) s.Stop();
                }
            }
        }

        public AudioClip CurrentMusic => music.Sources[music.Active].clip;
        public AudioClip CurrentAmbience => ambience.Sources[ambience.Active].clip;
        public bool MusicPlaying => music.Sources[music.Active].isPlaying;

        private AudioLibrary lib;
        private readonly Fader music = new Fader();
        private readonly Fader ambience = new Fader();
        private AudioSource ui;
        private readonly AudioSource[] pool = new AudioSource[AudioTuning.OneShotVoices];
        private int poolNext;

        private string sceneName;
        private RaceManager boundManager;
        private UiSound pendingUi;
        private float nextUi;
        private float navSilentUntil;
        private GameObject lastSelected;
        private float nextRank;
        private AudioListener listener;
        private int lobbyCount = -1;
        private readonly List<SledAudio> ranked = new List<SledAudio>();
        private Vector3 rankOrigin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || AudioLibrary.Instance == null) return;
            new GameObject("GameAudio").AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            lib = AudioLibrary.Instance;
            for (int i = 0; i < 2; i++)
            {
                music.Sources[i] = Create("Music" + i, lib.MusicGroup, true, 0);
                ambience.Sources[i] = Create("Ambience" + i, lib.SfxGroup, true, 8);
            }
            ui = Create("UI", lib.UiGroup, false, 16);
            for (int i = 0; i < pool.Length; i++) pool[i] = Create("OneShot" + i, lib.SfxGroup, false, 32);
        }

        private void OnEnable() { NetworkLobby.Changed += OnLobbyChanged; }

        private void OnDisable() { NetworkLobby.Changed -= OnLobbyChanged; }

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // Mixer values set before the first Start of the audio system are lost: apply here as well.
            ApplyVolumes(GameSettings.Current);
        }

        private AudioSource Create(string childName, AudioMixerGroup group, bool loop, int priority)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.priority = priority;
            s.outputAudioMixerGroup = group;
            s.volume = loop ? 0f : 1f;
            return s;
        }

        // ------------------------------------------------------------------ Volumes

        /// <summary>Pushes the settings to the mixer. UI follows the effects volume (no UI option in settings).</summary>
        public static void ApplyVolumes(GameSettings s)
        {
            var library = AudioLibrary.Instance;
            if (library == null || library.Mixer == null)
            {
                AudioListener.volume = s.MasterVolume;
                return;
            }
            AudioListener.volume = 1f;
            ApplyVolumes(library.Mixer, s.MasterVolume, s.MusicVolume, s.SfxVolume);
        }

        public static void ApplyVolumes(AudioMixer mixer, float master, float musicVolume, float sfx)
        {
            mixer.SetFloat(AudioTuning.MasterParam, AudioTuning.ToDb(master));
            mixer.SetFloat(AudioTuning.MusicParam, AudioTuning.ToDb(musicVolume));
            mixer.SetFloat(AudioTuning.SfxParam, AudioTuning.ToDb(sfx));
            mixer.SetFloat(AudioTuning.UiParam, AudioTuning.ToDb(sfx));
        }

        // ------------------------------------------------------------------ Requests

        public static void PlayUi(UiSound sound)
        {
            if (Instance != null && sound > Instance.pendingUi) Instance.pendingUi = sound;
        }

        /// <summary>Plays a one-shot; near = the local sled or a 2D cue, otherwise 3D at the position.</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume, bool near)
        {
            if (Instance == null || clip == null) return;
            var s = Instance.NextVoice();
            s.transform.position = position;
            s.spatialBlend = near ? NearSpatial : 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = near ? 8f : 5f;
            s.maxDistance = AudioTuning.RemoteAudibleDistance;
            s.clip = clip;
            s.volume = volume;
            s.Play();
        }

        private void PlayCue(AudioClip clip, float volume)
        {
            if (clip == null) return;
            var s = NextVoice();
            s.spatialBlend = 0f;
            s.clip = clip;
            s.volume = volume;
            s.Play();
        }

        /// <summary>A free voice, or the oldest one.</summary>
        private AudioSource NextVoice()
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var s = pool[(poolNext + i) % pool.Length];
                if (s.isPlaying) continue;
                poolNext = (poolNext + i + 1) % pool.Length;
                return s;
            }
            var oldest = pool[poolNext];
            poolNext = (poolNext + 1) % pool.Length;
            return oldest;
        }

        // ------------------------------------------------------------------ Frame

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var active = SceneManager.GetActiveScene().name;
            if (active != sceneName)
            {
                sceneName = active;
                OnSceneChanged();
            }
            music.Tick(dt);
            ambience.Tick(dt);

            if (RaceManager.Instance != boundManager) Bind(RaceManager.Instance);
            if (boundManager != null) AttachSleds();
            if (Time.unscaledTime >= nextRank)
            {
                nextRank = Time.unscaledTime + RankInterval;
                RankRemoteSleds();
            }
            WatchSelection();
        }

        private void LateUpdate()
        {
            if (pendingUi == UiSound.None || Time.unscaledTime < nextUi) return;
            var clip = pendingUi == UiSound.Nav ? lib.UiNav : pendingUi == UiSound.Confirm ? lib.UiConfirm
                : pendingUi == UiSound.Back ? lib.UiBack : lib.UiNotify;
            if (pendingUi != UiSound.Nav) navSilentUntil = Time.unscaledTime + NavSilenceAfterChange;
            pendingUi = UiSound.None;
            nextUi = Time.unscaledTime + UiInterval;
            if (clip != null) ui.PlayOneShot(clip);
        }

        private void OnSceneChanged()
        {
            navSilentUntil = Time.unscaledTime + 1f;
            var content = GameContent.Instance;
            bool menu = content != null && sceneName == content.MainMenuScene;
            bool race = sceneName.StartsWith("Track_") || sceneName.StartsWith("Dev_");
            if (menu)
            {
                music.Play(lib.MusicMenu, MenuMusicTrim);
                ambience.Play(null, 0f);
            }
            else if (race)
            {
                music.Play(lib.MusicRace, RaceMusicTrim);
                ambience.Play(lib.AmbienceForScene(sceneName), AmbienceTrim);
            }
        }

        private void AttachSleds()
        {
            var sleds = SledController.All;
            for (int i = sleds.Count - 1; i >= 0; i--)
            {
                var sled = sleds[i];
                if (sled != null && !sled.TryGetComponent(out SledAudio _)) sled.gameObject.AddComponent<SledAudio>();
            }
        }

        /// <summary>Only the nearest remote sleds sound: this is what keeps 8 sleds inside the voice budget.</summary>
        private void RankRemoteSleds()
        {
            if (SledAudio.All.Count == 0) return;
            if (listener == null) listener = FindFirstObjectByType<AudioListener>();
            rankOrigin = listener != null ? listener.transform.position : Vector3.zero;
            ranked.Clear();
            foreach (var s in SledAudio.All)
                if (s != null && !s.IsLocal) ranked.Add(s);
            ranked.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
            float limit = AudioTuning.RemoteAudibleDistance * AudioTuning.RemoteAudibleDistance;
            for (int i = 0; i < ranked.Count; i++)
                ranked[i].Audible = i < AudioTuning.MaxAudibleRemoteSleds && Dist(ranked[i]) <= limit;
        }

        private float Dist(SledAudio s) => (s.transform.position - rankOrigin).sqrMagnitude;

        private void WatchSelection()
        {
            var es = EventSystem.current;
            var selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == lastSelected) return;
            lastSelected = selected;
            if (selected != null && Time.unscaledTime >= navSilentUntil) PlayUi(UiSound.Nav);
        }

        private void OnLobbyChanged()
        {
            var lobby = NetworkLobby.Instance;
            int count = lobby != null && lobby.IsSpawned ? lobby.Players.Count : -1;
            if (lobbyCount > 0 && count > 0 && count != lobbyCount) PlayUi(UiSound.Notify);
            lobbyCount = count;
        }

        // ------------------------------------------------------------------ Race events

        private void Bind(RaceManager manager)
        {
            Unbind();
            boundManager = manager;
            if (manager == null) return;
            manager.CountdownTick += OnCountdown;
            manager.LapCompleted += OnLap;
            manager.ParticipantFinished += OnFinished;
        }

        private void Unbind()
        {
            if (boundManager is null) return;
            boundManager.CountdownTick -= OnCountdown;
            boundManager.LapCompleted -= OnLap;
            boundManager.ParticipantFinished -= OnFinished;
            boundManager = null;
        }

        private void OnCountdown(int tick) => PlayCue(tick > 0 ? lib.RaceBeep : lib.RaceGo, 1f);

        private void OnLap(RaceParticipant p, int completedLap)
        {
            if (p == null || !p.IsLocalPlayer || boundManager == null) return;
            int total = boundManager.TotalLaps;
            if (completedLap >= total) return; // the finish cue covers it
            PlayCue(completedLap == total - 1 ? lib.LapFinal : lib.LapComplete, 1f);
        }

        private void OnFinished(RaceParticipant p)
        {
            if (p != null && p.IsLocalPlayer) PlayCue(lib.RaceFinish, 1f);
        }
    }
}

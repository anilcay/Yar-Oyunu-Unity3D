using System.Collections.Generic;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Audio
{
    /// <summary>
    /// Sound of one sled. GameAudio adds it at runtime to every race sled, so offline, network owner and
    /// network proxy sleds all take this one path. The local player's sled is near-2D with all layers;
    /// other sleds are 3D, limited to a roll layer plus their strongest texture, and silent unless
    /// GameAudio marks them <see cref="Audible"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SledAudio : MonoBehaviour
    {
        public static readonly List<SledAudio> All = new List<SledAudio>();

        private const float FadeRate = 7f;       // volume units per second
        private const float LocalSpatial = 0.15f;
        private const float ImpactCooldown = 0.18f;
        private const float LocalImpactBoost = 1f;
        private const float RemoteGain = 0.85f;

        /// <summary>Set by GameAudio: this remote sled is one of the nearest ones.</summary>
        public bool Audible { get; set; }
        public bool IsLocal { get; private set; }
        /// <summary>Speed the sound follows (m/s); derived from positions on kinematic sleds.</summary>
        public float AudioSpeed { get; private set; }
        public AudioSource Roll => roll;

        private SledController sled;
        private RaceParticipant participant;
        private TrackFollower follower;
        private AudioLibrary lib;
        private Transform host;
        private readonly ProxySpeedEstimator estimator = new ProxySpeedEstimator();

        private AudioSource roll, wind, brake, drift, contact;
        private bool configuredLocal;
        private bool configured;
        private float nextImpact;

        private void Awake()
        {
            sled = GetComponent<SledController>();
            participant = GetComponent<RaceParticipant>();
            follower = GetComponent<TrackFollower>();
            lib = AudioLibrary.Instance;
            var go = new GameObject("Audio");
            go.transform.SetParent(transform, false);
            host = go.transform;
        }

        private void OnEnable()
        {
            All.Add(this);
            if (sled == null) return;
            sled.Bumped += OnBumped;
            sled.BoostStarted += OnBoost;
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (sled == null) return;
            sled.Bumped -= OnBumped;
            sled.BoostStarted -= OnBoost;
        }

        private void FixedUpdate()
        {
            if (sled == null || sled.Body == null) return;
            if (sled.Body.isKinematic) estimator.Push(sled.Body.position, Time.fixedDeltaTime);
            else estimator.Reset();
        }

        private void Update()
        {
            if (sled == null || lib == null) return;
            IsLocal = participant != null && participant.IsLocalPlayer;
            if (!configured || configuredLocal != IsLocal) Configure();

            bool kinematic = sled.Body != null && sled.Body.isKinematic;
            bool proxy = kinematic && sled.Frozen;
            AudioSpeed = kinematic ? estimator.Speed : sled.Speed;
            float maxSpeed = sled.Stats != null ? sled.Stats.MaxSpeed : 30f;
            float s = Mathf.Clamp(AudioSpeed / maxSpeed, 0f, 1.25f);
            bool grounded = kinematic || sled.IsGrounded;
            bool audible = IsLocal || Audible;
            bool lift = sled.IsOnLift || (proxy && ProxyOnLift());
            var input = sled.LastInput;

            float rollVol = audible && grounded ? AudioTuning.RollVolume(s) : 0f;
            if (lift) rollVol *= 0.3f;
            float brakeVol = audible && !lift && grounded && AudioSpeed > 2f ? Mathf.Clamp01(input.Brake) * Mathf.Lerp(0.5f, 1f, s) : 0f;
            bool drifting = proxy ? input.Drift : sled.IsDrifting;
            float driftVol = audible && !lift && grounded && drifting && AudioSpeed > 4f ? Mathf.Lerp(0.65f, 1f, s) : 0f;
            bool scraping = sled.IsScrapingBarrier && AudioSpeed > 3f;
            float contactVol = audible ? (lift ? 0.85f : scraping ? Mathf.Lerp(0.6f, 1f, s) : 0f) : 0f;
            AudioClip contactClip = lift ? lib.LiftChain : lib.SledScrape;

            float dt = Time.deltaTime;
            float gain = IsLocal ? 1f : RemoteGain;
            if (!IsLocal)
            {
                // Remote sleds keep only their strongest texture.
                if (contactVol > 0f) { brakeVol = 0f; driftVol = 0f; }
                else if (driftVol >= brakeVol) brakeVol = 0f;
                else driftVol = 0f;
            }
            Drive(ref roll, lib.SledRoll, rollVol * 0.9f * gain, AudioTuning.RollPitch(s), dt);
            if (IsLocal)
                Drive(ref wind, lib.SledWind, (lift ? 0.25f : 1f) * AudioTuning.WindVolume(s) * 0.8f, AudioTuning.WindPitch(s), dt);
            Drive(ref brake, lib.SledBrake, brakeVol * 0.8f * gain, 0.9f + 0.25f * s, dt);
            Drive(ref drift, lib.SledDrift, driftVol * 0.8f * gain, 0.85f + 0.35f * s, dt);
            if (contact != null && contact.clip != contactClip && contact.volume <= 0.001f) contact.clip = contactClip;
            Drive(ref contact, contactClip, contactVol * 0.85f * gain, lift ? 1f : 0.9f + 0.3f * s, dt);
        }

        private bool ProxyOnLift()
        {
            var rm = RaceManager.Instance;
            var track = rm != null ? rm.Track : null;
            if (track == null || !track.HasLift || follower == null || !follower.IsValid) return false;
            float along = track.SignedDelta(track.LiftStart, follower.Distance);
            return along >= 0f && along <= track.LiftLength && AudioSpeed > 1f;
        }

        private void Drive(ref AudioSource source, AudioClip clip, float target, float pitch, float dt)
        {
            if (source == null)
            {
                if (target <= 0f || clip == null) return;
                source = CreateSource(clip);
            }
            source.volume = Mathf.MoveTowards(source.volume, target, FadeRate * dt);
            source.pitch = pitch;
            if (source.volume <= 0.001f)
            {
                if (source.isPlaying) source.Stop();
            }
            else if (!source.isPlaying)
            {
                if (source.clip != clip) source.clip = clip;
                source.timeSamples = Random.Range(0, Mathf.Max(1, source.clip.samples - 1));
                source.Play();
            }
        }

        private AudioSource CreateSource(AudioClip clip)
        {
            var source = host.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.outputAudioMixerGroup = lib.SfxGroup;
            ApplySpace(source);
            return source;
        }

        private void Configure()
        {
            configured = true;
            configuredLocal = IsLocal;
            foreach (var s in host.GetComponents<AudioSource>()) ApplySpace(s);
        }

        private void ApplySpace(AudioSource s)
        {
            bool local = IsLocal;
            s.spatialBlend = local ? LocalSpatial : 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = local ? 8f : 5f;
            s.maxDistance = AudioTuning.RemoteAudibleDistance;
            s.dopplerLevel = local ? 0f : 0.25f;
            s.priority = local ? 64 : 140;
        }

        private void OnBumped(Collision c)
        {
            if (Time.time < nextImpact || lib == null || (!IsLocal && !Audible)) return;
            nextImpact = Time.time + ImpactCooldown;
            float v = c.relativeVelocity.magnitude;
            var clip = lib.SledImpacts[Mathf.Clamp(AudioTuning.ImpactIndex(v), 0, lib.SledImpacts.Length - 1)];
            float volume = Mathf.Lerp(0.55f, 1f, Mathf.InverseLerp(2f, 18f, v)) * (IsLocal ? LocalImpactBoost : RemoteGain);
            GameAudio.PlayAt(clip, transform.position, volume, IsLocal);
        }

        private void OnBoost(SledBoostKind kind)
        {
            if (lib == null || (!IsLocal && !Audible)) return;
            float volume = kind == SledBoostKind.DriftMini ? 0.7f : kind == SledBoostKind.Slipstream ? 0.8f : 1f;
            GameAudio.PlayAt(lib.SledBoost, transform.position, volume * (IsLocal ? 1f : RemoteGain), IsLocal);
        }
    }
}

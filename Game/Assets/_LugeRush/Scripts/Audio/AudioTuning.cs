using UnityEngine;

namespace LugeRush.Audio
{
    /// <summary>Pure audio rules (curves, budgets, mixer names): no scene state, unit tested in EditMode.</summary>
    public static class AudioTuning
    {
        // Exposed mixer parameters.
        public const string MasterParam = "MasterVolume";
        public const string MusicParam = "MusicVolume";
        public const string SfxParam = "SfxVolume";
        public const string UiParam = "UiVolume";
        public const float SilenceDb = -80f;

        // Voice budget. Project real voice limit is 32 (AudioManager.asset).
        public const int MusicVoices = 2;    // crossfade pair
        public const int AmbienceVoices = 2; // crossfade pair
        public const int UiVoices = 1;
        public const int OneShotVoices = 5;  // countdown, boost, impacts (oldest is stolen)
        public const int LocalSledVoices = 5;  // roll, wind, brake, drift, contact
        public const int RemoteSledVoices = 3; // roll + strongest texture, a second one while crossfading
        public const int MaxAudibleRemoteSleds = 4;
        public const float RemoteAudibleDistance = 140f;

        public const int MaxVoices = MusicVoices + AmbienceVoices + UiVoices + OneShotVoices + LocalSledVoices
                                     + RemoteSledVoices * MaxAudibleRemoteSleds;

        public static float ToDb(float linear) =>
            linear <= 0.0001f ? SilenceDb : Mathf.Max(SilenceDb, 20f * Mathf.Log10(linear));

        public static float RollPitch(float speed01) => Mathf.Lerp(0.72f, 1.8f, Mathf.Pow(Mathf.Clamp(speed01, 0f, 1.25f), 0.85f));

        public static float RollVolume(float speed01) => Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(speed01 * 1.1f), 0.55f));

        public static float WindPitch(float speed01) => Mathf.Lerp(0.8f, 1.35f, Mathf.Clamp(speed01, 0f, 1.25f));

        public static float WindVolume(float speed01) => Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(speed01), 2f));

        /// <summary>0 light, 1 medium, 2 heavy from the relative collision speed (m/s).</summary>
        public static int ImpactIndex(float relativeSpeed) => relativeSpeed < 6f ? 0 : relativeSpeed < 13f ? 1 : 2;
    }

    /// <summary>
    /// Speed of a kinematic sled from its position samples. Network proxies are Frozen, so the rigidbody
    /// reports nothing; teleports (respawn) keep the previous estimate instead of spiking.
    /// </summary>
    public sealed class ProxySpeedEstimator
    {
        public const float TeleportSpeed = 80f;
        private const float Smoothing = 0.25f;

        private Vector3 last;
        private bool hasPose;

        public float Speed { get; private set; }

        public float Push(Vector3 position, float dt)
        {
            if (dt <= 0f) return Speed;
            float measured = hasPose ? (position - last).magnitude / dt : 0f;
            if (measured > TeleportSpeed) measured = Speed;
            Speed = Mathf.Lerp(Speed, measured, Smoothing);
            last = position;
            hasPose = true;
            return Speed;
        }

        public void Reset()
        {
            Speed = 0f;
            hasPose = false;
        }
    }
}

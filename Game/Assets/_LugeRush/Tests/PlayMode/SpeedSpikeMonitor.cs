using System.Collections.Generic;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.Tests
{
    /// <summary>
    /// Watches every sled on every physics step for sudden speed jumps and counts SledController's
    /// "velocity spike" warnings. Sampled in FixedUpdate so accelerated tests cannot step over a spike.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SpeedSpikeMonitor : MonoBehaviour
    {
        /// <summary>Largest speed gain (m/s) a healthy sled can show in one physics step (collision knockback included).</summary>
        public const float MaxStepGain = 12f;

        private class Sample { public float Speed; public bool Valid; }

        public readonly List<string> Spikes = new List<string>();
        public int WarningCount { get; private set; }
        public float MaxStepGainSeen { get; private set; }

        private readonly Dictionary<SledController, Sample> samples = new Dictionary<SledController, Sample>();
        private TrackPath track;

        public static SpeedSpikeMonitor Create()
        {
            return new GameObject("SpeedSpikeMonitor").AddComponent<SpeedSpikeMonitor>();
        }

        private void OnEnable() { Application.logMessageReceived += OnLog; }
        private void OnDisable() { Application.logMessageReceived -= OnLog; }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning && message.Contains("velocity spike"))
            {
                WarningCount++;
                Spikes.Add(message);
            }
        }

        private void FixedUpdate()
        {
            if (track == null) track = FindFirstObjectByType<TrackPath>();
            foreach (var sled in SledController.All)
            {
                if (!samples.TryGetValue(sled, out var s)) samples[sled] = s = new Sample();
                if (sled.Body == null || sled.Body.isKinematic) { s.Valid = false; continue; }

                float v = sled.Body.linearVelocity.magnitude;
                float cap = sled.Stats.MaxSpeed * sled.Stats.SpeedCapMultiplier;
                if (s.Valid)
                {
                    float gain = v - s.Speed;
                    MaxStepGainSeen = Mathf.Max(MaxStepGainSeen, gain);
                    if (gain > MaxStepGain || v > cap * 1.15f)
                    {
                        string where = "";
                        if (track != null && sled.TryGetComponent(out TrackFollower f) && f.IsValid)
                            where = $" d={f.Distance:0.0} lat={f.Lateral:0.0} h={f.Height:0.0} lift={track.LiftStart:0}-{track.LiftEnd:0}";
                        Spikes.Add($"{sled.name} {s.Speed * 3.6f:0}->{v * 3.6f:0} km/h t={Time.time:0.00}{where}");
                    }
                }
                s.Speed = v;
                s.Valid = true;
            }
        }

        public string Report(int max = 12)
        {
            int n = Mathf.Min(max, Spikes.Count);
            return $"spikes={Spikes.Count} warnings={WarningCount} maxStepGain={MaxStepGainSeen:0.0}m/s" +
                   (n > 0 ? "\n  " + string.Join("\n  ", Spikes.GetRange(0, n)) : "");
        }
    }
}

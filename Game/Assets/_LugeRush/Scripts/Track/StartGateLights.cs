using System.Collections.Generic;
using LugeRush.Race;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>
    /// Drives the Lamp_Red_1..3 / Lamp_Green child meshes of SM_Track_StartGate from the race countdown:
    /// all off, reds light up one by one on 3-2-1, green only at GO. Off = renderer disabled (the dark lens stays).
    /// </summary>
    public class StartGateLights : MonoBehaviour
    {
        private readonly List<Renderer>[] reds = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        private readonly List<Renderer> greens = new List<Renderer>();
        private RaceManager manager;

        /// <summary>Reds lit, 0..3.</summary>
        public int RedsLit { get; private set; }
        public bool GreenLit { get; private set; }
        public int LampCount => greens.Count + reds[0].Count + reds[1].Count + reds[2].Count;

        /// <summary>Finds the lamps in the loaded scene and binds them to the manager.</summary>
        public static StartGateLights Attach(RaceManager manager)
        {
            var lights = manager.gameObject.AddComponent<StartGateLights>();
            lights.Bind(manager);
            return lights;
        }

        private void Bind(RaceManager raceManager)
        {
            manager = raceManager;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("Lamp_")) continue;
                if (r.name == "Lamp_Green") greens.Add(r);
                else if (r.name.StartsWith("Lamp_Red_") && int.TryParse(r.name.Substring("Lamp_Red_".Length), out int n) && n >= 1 && n <= 3)
                    reds[n - 1].Add(r);
            }
            manager.CountdownTick += OnCountdown;
            Set(0, false);
        }

        private void OnDestroy()
        {
            if (manager != null) manager.CountdownTick -= OnCountdown;
        }

        private void OnCountdown(int tick)
        {
            if (tick <= 0) Set(0, true);
            else Set(Mathf.Clamp(manager.CountdownFrom - tick + 1, 0, 3), false);
        }

        private void Set(int redsLit, bool green)
        {
            RedsLit = redsLit;
            GreenLit = green;
            for (int i = 0; i < reds.Length; i++)
                foreach (var r in reds[i])
                    r.enabled = i < redsLit;
            foreach (var r in greens) r.enabled = green;
        }
    }
}

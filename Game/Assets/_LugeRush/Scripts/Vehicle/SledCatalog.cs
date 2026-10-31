using System;
using UnityEngine;

namespace LugeRush.Vehicle
{
    public enum SledColor { Orange = 0, Red = 1, Yellow = 2, Turquoise = 3 }

    /// <summary>Maps sled types to stats/prefabs and paint options to colours (used by menus and spawning).</summary>
    [CreateAssetMenu(menuName = "Luge Rush/Sled Catalog", fileName = "SledCatalog")]
    public class SledCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public SledType Type;
            public SledStats Stats;
            [Tooltip("Optional art prefab placed under SledVisual (null = keep placeholder).")]
            public GameObject VisualPrefab;
        }

        public Entry[] Sleds = Array.Empty<Entry>();

        public Color[] BodyColors =
        {
            new Color(1.00f, 0.45f, 0.05f), // Orange
            new Color(0.85f, 0.10f, 0.10f), // Red
            new Color(1.00f, 0.82f, 0.10f), // Yellow
            new Color(0.10f, 0.75f, 0.80f), // Turquoise
        };

        public Color[] HelmetColors =
        {
            new Color(1.00f, 0.85f, 0.10f), // yellow
            new Color(0.15f, 0.35f, 0.90f), // blue
            new Color(0.15f, 0.70f, 0.25f), // green
            new Color(0.08f, 0.08f, 0.08f), // black
        };

        public SledStats GetStats(SledType type)
        {
            foreach (var e in Sleds) if (e != null && e.Type == type) return e.Stats;
            return Sleds.Length > 0 ? Sleds[0].Stats : null;
        }

        public Color GetBodyColor(SledColor color) => BodyColors.Length == 0 ? Color.white : BodyColors[(int)color % BodyColors.Length];
        public Color GetHelmetColor(int index) => HelmetColors.Length == 0 ? Color.white : HelmetColors[Mathf.Abs(index) % HelmetColors.Length];
    }
}

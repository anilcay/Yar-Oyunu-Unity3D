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
            [Tooltip("Complete sled prefab (physics root + SledVisual with art model and rider(s)). Prefabs/Sleds/Sled_<Type>.")]
            public GameObject Prefab;
            [Tooltip("Deprecated: kept for old data. Use Prefab.")]
            public GameObject VisualPrefab;
        }

        public Entry[] Sleds = Array.Empty<Entry>();

        // ART_GUIDE sled colours: orange #F2792B, red #D93A3A, yellow #F2C230, turquoise #3BB8C4.
        public Color[] BodyColors =
        {
            new Color(0.949f, 0.475f, 0.169f),
            new Color(0.851f, 0.227f, 0.227f),
            new Color(0.949f, 0.761f, 0.188f),
            new Color(0.231f, 0.722f, 0.769f),
        };

        // Helmet palette cells (row 5): yellow, blue, green, black, white, pink, red, purple (RiderAppearance index = array index).
        public Color[] HelmetColors =
        {
            new Color(0.949f, 0.761f, 0.188f),
            new Color(0.176f, 0.435f, 0.820f),
            new Color(0.369f, 0.761f, 0.290f),
            new Color(0.137f, 0.141f, 0.153f),
            new Color(0.941f, 0.941f, 0.941f),
            new Color(0.910f, 0.416f, 0.627f),
            new Color(0.851f, 0.227f, 0.227f),
            new Color(0.482f, 0.310f, 0.788f),
        };

        public Entry Get(SledType type)
        {
            foreach (var e in Sleds) if (e != null && e.Type == type) return e;
            return Sleds.Length > 0 ? Sleds[0] : null;
        }

        public SledStats GetStats(SledType type) => Get(type)?.Stats;

        /// <summary>Full sled prefab for <paramref name="type"/> (null when not built yet).</summary>
        public GameObject GetPrefab(SledType type)
        {
            foreach (var e in Sleds) if (e != null && e.Type == type) return e.Prefab;
            return null;
        }

        public Color GetBodyColor(SledColor color) => BodyColors.Length == 0 ? Color.white : BodyColors[(int)color % BodyColors.Length];
        public Color GetHelmetColor(int index) => HelmetColors.Length == 0 ? Color.white : HelmetColors[Mathf.Abs(index) % HelmetColors.Length];
    }
}

using System;
using LugeRush.Track;
using UnityEngine;

namespace LugeRush.Race
{
    /// <summary>
    /// Maps that menus and the lobby offer. A map is playable when its scene is in the build settings;
    /// otherwise the menu shows it as "coming soon". Track ids match <see cref="TrackDefinition.TrackId"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Luge Rush/Track Catalog", fileName = "TrackCatalog")]
    public class TrackCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string TrackId;
            public string DisplayName;
            [TextArea] public string Description;
            [Tooltip("Scene name (no path, no extension) loaded for this map.")]
            public string SceneName;
            [Tooltip("Optional: the definition the scene was built from (minimap preview, length).")]
            public TrackDefinition Definition;
            public Color ThemeColor = Color.white;
            [Tooltip("Hide from menus (developer tracks in release builds).")]
            public bool DeveloperOnly;
        }

        public Entry[] Tracks = Array.Empty<Entry>();

        public int Count => Tracks.Length;

        public Entry Get(int index) => Tracks.Length == 0 ? null : Tracks[Mathf.Clamp(index, 0, Tracks.Length - 1)];

        public int IndexOf(string trackId)
        {
            for (int i = 0; i < Tracks.Length; i++)
                if (Tracks[i] != null && string.Equals(Tracks[i].TrackId, trackId, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public Entry Find(string trackId)
        {
            int i = IndexOf(trackId);
            return i >= 0 ? Tracks[i] : null;
        }

        /// <summary>First entry whose scene is playable (fallback when a saved choice is unavailable).</summary>
        public int FirstAvailableIndex()
        {
            for (int i = 0; i < Tracks.Length; i++) if (IsAvailable(Tracks[i])) return i;
            return 0;
        }

        /// <summary>True when the map's scene is part of the build (Application.CanStreamedLevelBeLoaded).</summary>
        public static bool IsAvailable(Entry e) =>
            e != null && !string.IsNullOrEmpty(e.SceneName) && Application.CanStreamedLevelBeLoaded(e.SceneName);
    }
}

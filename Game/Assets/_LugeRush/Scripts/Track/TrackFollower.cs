using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>
    /// Tracks where an object is on the TrackPath (distance, lateral offset, height) once per physics step.
    /// Shared by race progress, AI, lift and recovery so the nearest-point search runs only once per sled.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TrackFollower : MonoBehaviour
    {
        public TrackPath Track;
        [Tooltip("Search window around the previous distance (metres). Prevents jumping to a nearby track section.")]
        [SerializeField] private float searchWindow = 25f;

        private Rigidbody body;
        private bool initialized;

        public float Distance { get; private set; }
        public float Lateral { get; private set; }
        public float Height { get; private set; }
        public bool IsValid => initialized && Track != null;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (Track == null) Track = FindFirstObjectByType<TrackPath>();
        }

        private void FixedUpdate() { Refresh(); }

        public void Refresh()
        {
            if (Track == null) return;
            Vector3 p = body != null ? body.position : transform.position;
            Distance = initialized ? Track.GetNearestDistance(p, Distance, searchWindow) : Track.GetNearestDistance(p);
            initialized = true;
            Track.GetLocalOffset(p, Distance, out float lat, out float h);
            Lateral = lat;
            Height = h;
        }

        /// <summary>Call after teleporting so the windowed search restarts at the right place.</summary>
        public void SetDistanceHint(float distance)
        {
            if (Track == null) return;
            Distance = Track.Wrap(distance);
            initialized = true;
        }
    }
}

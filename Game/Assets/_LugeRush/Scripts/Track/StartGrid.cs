using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>Eight start slots in staggered pairs behind the start line.</summary>
    public class StartGrid : MonoBehaviour
    {
        public const int MaxSlots = 8;

        public TrackPath Track;
        [Tooltip("Distance of the first row behind the start line.")]
        public float FirstRowOffset = 4f;
        public float RowSpacing = 5f;
        [Tooltip("Distance between the two columns.")]
        public float ColumnSpacing = 1.9f;
        [Tooltip("Right column sits this much further back (staggered grid).")]
        public float Stagger = 1.5f;

        private void Awake()
        {
            if (Track == null) Track = GetComponentInParent<TrackPath>();
        }

        public float GetSlotDistance(int index)
        {
            int row = index / 2;
            int col = index % 2;
            return Track.Wrap(Track.StartLineDistance - FirstRowOffset - row * RowSpacing - col * Stagger);
        }

        /// <summary>Pose of a slot with the root lifted by <paramref name="hoverHeight"/> along the road normal.</summary>
        public Pose GetSlot(int index, float hoverHeight)
        {
            index = Mathf.Clamp(index, 0, MaxSlots - 1);
            float d = GetSlotDistance(index);
            float lateral = (index % 2 == 0 ? -0.5f : 0.5f) * ColumnSpacing;
            Vector3 up = Track.GetUp(d);
            Vector3 pos = Track.GetPosition(d) + Track.GetRight(d) * lateral + up * hoverHeight;
            return new Pose(pos, Quaternion.LookRotation(Track.GetTangent(d), up));
        }

        private void OnDrawGizmos()
        {
            if (Track == null || Track.SampleCount == 0) return;
            Gizmos.color = Color.green;
            for (int i = 0; i < MaxSlots; i++)
            {
                var p = GetSlot(i, 0.1f);
                Gizmos.matrix = Matrix4x4.TRS(p.position, p.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.9f, 0.2f, 1.8f));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}

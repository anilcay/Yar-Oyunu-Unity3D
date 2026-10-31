using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Splines.Interpolators;

namespace LugeRush.Track
{
    /// <summary>
    /// SplineContainer wrapper describing a closed-loop track. Banking is stored in knot rotations
    /// (spline up vector), road width in a SplineData&lt;float&gt;. At runtime the spline is resampled into
    /// an arc-length table (~1 m) so all distance queries are cheap and stable.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [DefaultExecutionOrder(-100)]
    public class TrackPath : MonoBehaviour
    {
        [SerializeField] private SplineContainer container;
        [SerializeField] private SplineData<float> widthData = new SplineData<float>();
        [SerializeField] private float defaultWidth = 4f;
        [SerializeField] private float sampleStep = 1f;

        [Header("Lift / start (knot indices + offsets)")]
        [SerializeField] private int liftStartKnot = -1;
        [SerializeField] private int liftEndKnot = -1;
        [SerializeField] private float startLineOffset = 40f;
        [SerializeField] private float checkpointSpacing = 50f;

        private Vector3[] positions;
        private Vector3[] tangents;
        private Vector3[] ups;
        private Vector3[] rights;
        private float[] widths;
        private float[] curvatures;
        private float step;
        private bool built;

        public float Length { get; private set; }
        public float LiftStart { get; private set; }
        public float LiftEnd { get; private set; }
        public float StartLineDistance { get; private set; }
        public float SampleStep => step;
        public int SampleCount => positions?.Length ?? 0;
        public int CheckpointCount { get; private set; }
        public SplineContainer Container => container;
        public bool HasLift => liftStartKnot >= 0 && liftEndKnot >= 0;
        public float LiftLength => HasLift ? Wrap(LiftEnd - LiftStart) : 0f;

        private void Awake() { EnsureBuilt(); }

        public void EnsureBuilt()
        {
            if (!built || positions == null) Rebuild();
        }

        /// <summary>Writes a TrackDefinition into the spline (knots, banking, width) and resamples.</summary>
        public void BuildFromDefinition(TrackDefinition def)
        {
            if (container == null) container = GetComponent<SplineContainer>();
            var knots = def.Knots;
            int n = knots.Count;
            var spline = new Spline();
            var width = new SplineData<float> { PathIndexUnit = PathIndexUnit.Knot, DefaultValue = def.DefaultWidth };

            for (int i = 0; i < n; i++)
            {
                Vector3 prev = knots[(i - 1 + n) % n].Position;
                Vector3 p = knots[i].Position;
                Vector3 next = knots[(i + 1) % n].Position;
                Vector3 dir = (next - prev).normalized;
                float handle = def.HandleScale * Mathf.Min(Vector3.Distance(p, prev), Vector3.Distance(p, next));

                Vector3 flatUp = Vector3.ProjectOnPlane(Vector3.up, dir).normalized;
                Vector3 bankedUp = Quaternion.AngleAxis(-knots[i].BankDeg, dir) * flatUp;
                Quaternion rot = Quaternion.LookRotation(dir, bankedUp);

                Vector3 local = transform.InverseTransformPoint(p);
                Quaternion localRot = Quaternion.Inverse(transform.rotation) * rot;
                var knot = new BezierKnot(local, new float3(0f, 0f, -handle), new float3(0f, 0f, handle), localRot);
                spline.Add(knot, TangentMode.Mirrored);
                width.Add(i, knots[i].Width > 0f ? knots[i].Width : def.DefaultWidth);
            }
            spline.Closed = true;

            container.Spline = spline;
            widthData = width;
            defaultWidth = def.DefaultWidth;
            liftStartKnot = def.LiftStartKnot;
            liftEndKnot = def.LiftEndKnot;
            startLineOffset = def.StartLineOffset;
            checkpointSpacing = def.CheckpointSpacing;
            built = false;
            Rebuild();
        }

        /// <summary>Resamples the spline into the arc-length tables.</summary>
        public void Rebuild()
        {
            if (container == null) container = GetComponent<SplineContainer>();
            var spline = container.Spline;
            if (spline == null || spline.Count < 3)
            {
                built = false;
                return;
            }

            // 1) Fine arc-length table over normalized t.
            int fine = Mathf.Max(4096, spline.Count * 128);
            var cum = new float[fine + 1];
            Vector3 prev = container.EvaluatePosition(0f);
            for (int i = 1; i <= fine; i++)
            {
                Vector3 p = container.EvaluatePosition(i / (float)fine);
                cum[i] = cum[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            Length = cum[fine];

            // 2) Uniform samples in distance.
            int count = Mathf.Max(16, Mathf.RoundToInt(Length / Mathf.Max(0.25f, sampleStep)));
            step = Length / count;
            positions = new Vector3[count];
            tangents = new Vector3[count];
            ups = new Vector3[count];
            rights = new Vector3[count];
            widths = new float[count];
            curvatures = new float[count];

            int j = 0;
            for (int k = 0; k < count; k++)
            {
                float d = k * step;
                while (j < fine - 1 && cum[j + 1] < d) j++;
                float seg = cum[j + 1] - cum[j];
                float t = (j + (seg > 1e-6f ? (d - cum[j]) / seg : 0f)) / fine;

                Vector3 pos = container.EvaluatePosition(t);
                Vector3 tan = ((Vector3)container.EvaluateTangent(t)).normalized;
                Vector3 up = container.EvaluateUpVector(t);
                up = Vector3.ProjectOnPlane(up, tan).normalized;
                if (up.sqrMagnitude < 0.5f) up = Vector3.up;

                positions[k] = pos;
                tangents[k] = tan;
                ups[k] = up;
                rights[k] = Vector3.Cross(up, tan).normalized;
                widths[k] = EvaluateWidth(spline, t);
            }

            // 3) Signed curvature (1/m, positive = right turn) from tangents +-3 samples apart.
            int span = Mathf.Max(1, Mathf.RoundToInt(3f / step));
            for (int k = 0; k < count; k++)
            {
                Vector3 a = tangents[(k - span + count) % count];
                Vector3 b = tangents[(k + span) % count];
                Vector3 axis = ups[k];
                float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(a, axis), Vector3.ProjectOnPlane(b, axis), axis) * Mathf.Deg2Rad;
                curvatures[k] = ang / (2f * span * step);
            }

            // 4) Lift + start line distances from knot indices.
            if (HasLift)
            {
                LiftStart = KnotDistance(spline, liftStartKnot, cum, fine);
                LiftEnd = KnotDistance(spline, liftEndKnot, cum, fine);
            }
            else
            {
                LiftStart = LiftEnd = 0f;
            }
            StartLineDistance = Wrap(LiftEnd + startLineOffset);
            CheckpointCount = Mathf.Max(4, Mathf.RoundToInt(Length / Mathf.Max(5f, checkpointSpacing)));
            built = true;
        }

        private float EvaluateWidth(Spline spline, float t)
        {
            if (widthData == null || widthData.Count == 0) return defaultWidth;
            return widthData.Evaluate(spline, t, PathIndexUnit.Normalized, new LerpFloat());
        }

        private static float KnotDistance(Spline spline, int knot, float[] cum, int fine)
        {
            knot = Mathf.Clamp(knot, 0, spline.Count - 1);
            float t = spline.CurveToSplineT(knot);
            float f = Mathf.Clamp(t, 0f, 1f) * fine;
            int i = Mathf.Min(fine - 1, Mathf.FloorToInt(f));
            return Mathf.Lerp(cum[i], cum[i + 1], f - i);
        }

        // ---------- Distance helpers ----------

        public float Wrap(float d)
        {
            if (Length <= 0f) return d;
            d %= Length;
            return d < 0f ? d + Length : d;
        }

        /// <summary>Shortest signed distance from a to b along the loop, in [-Length/2, Length/2).</summary>
        public float SignedDelta(float from, float to)
        {
            float d = Wrap(to - from);
            return d >= Length * 0.5f ? d - Length : d;
        }

        private void Locate(float distance, out int i0, out int i1, out float f)
        {
            EnsureBuilt();
            float s = Wrap(distance) / step;
            i0 = Mathf.FloorToInt(s) % positions.Length;
            i1 = (i0 + 1) % positions.Length;
            f = s - Mathf.Floor(s);
        }

        public Vector3 GetPosition(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Vector3.Lerp(positions[a], positions[b], f);
        }

        public Vector3 GetTangent(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Vector3.Slerp(tangents[a], tangents[b], f).normalized;
        }

        /// <summary>Road surface normal including banking.</summary>
        public Vector3 GetUp(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Vector3.Slerp(ups[a], ups[b], f).normalized;
        }

        public Vector3 GetRight(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Vector3.Slerp(rights[a], rights[b], f).normalized;
        }

        public float GetWidth(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Mathf.Lerp(widths[a], widths[b], f);
        }

        /// <summary>Signed curvature in 1/m (positive = right turn).</summary>
        public float GetCurvature(float distance)
        {
            Locate(distance, out int a, out int b, out float f);
            return Mathf.Lerp(curvatures[a], curvatures[b], f);
        }

        public Quaternion GetRotation(float distance) => Quaternion.LookRotation(GetTangent(distance), GetUp(distance));

        public bool IsInLift(float distance)
        {
            if (!HasLift) return false;
            return Wrap(distance - LiftStart) < LiftLength;
        }

        // ---------- Checkpoints (derived from distance, ~CheckpointSpacing apart, index 0 = start line) ----------

        public float CheckpointInterval => Length / Mathf.Max(1, CheckpointCount);
        public float GetCheckpointDistance(int index) => Wrap(StartLineDistance + index * CheckpointInterval);

        // ---------- Nearest point ----------

        /// <summary>Global nearest distance (use the hinted overload every frame).</summary>
        public float GetNearestDistance(Vector3 worldPos)
        {
            EnsureBuilt();
            int best = 0;
            float bestSq = float.MaxValue;
            for (int k = 0; k < positions.Length; k++)
            {
                float sq = (positions[k] - worldPos).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = k; }
            }
            return Refine(worldPos, best);
        }

        /// <summary>Nearest distance searched only within +-window metres of a previous result (no jumps between nearby track sections).</summary>
        public float GetNearestDistance(Vector3 worldPos, float hint, float window)
        {
            EnsureBuilt();
            int n = positions.Length;
            int c = Mathf.FloorToInt(Wrap(hint) / step);
            int w = Mathf.Max(2, Mathf.CeilToInt(window / step));
            int best = c;
            float bestSq = float.MaxValue;
            for (int o = -w; o <= w; o++)
            {
                int k = ((c + o) % n + n) % n;
                float sq = (positions[k] - worldPos).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = k; }
            }
            return Refine(worldPos, best);
        }

        private float Refine(Vector3 p, int k)
        {
            int n = positions.Length;
            float bestD = k * step;
            float bestSq = (positions[k] - p).sqrMagnitude;
            for (int side = -1; side <= 0; side++)
            {
                int i0 = ((k + side) % n + n) % n;
                int i1 = (i0 + 1) % n;
                Vector3 a = positions[i0], b = positions[i1];
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float sq = (a + ab * t - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; bestD = (i0 + t) * step; }
            }
            return Wrap(bestD);
        }

        /// <summary>Lateral (right positive) and height offset of a world point relative to the centreline at distance.</summary>
        public void GetLocalOffset(Vector3 worldPos, float distance, out float lateral, out float height)
        {
            Vector3 rel = worldPos - GetPosition(distance);
            lateral = Vector3.Dot(rel, GetRight(distance));
            height = Vector3.Dot(rel, GetUp(distance));
        }

        /// <summary>Samples for mesh building / gizmos (step-spaced).</summary>
        public void GetSample(int index, out Vector3 pos, out Vector3 tan, out Vector3 up, out Vector3 right, out float width)
        {
            EnsureBuilt();
            int n = positions.Length;
            index = (index % n + n) % n;
            pos = positions[index];
            tan = tangents[index];
            up = ups[index];
            right = rights[index];
            width = widths[index];
        }

        private void OnDrawGizmosSelected()
        {
            if (positions == null) return;
            Gizmos.color = Color.yellow;
            for (int k = 0; k < positions.Length; k += 4)
                Gizmos.DrawLine(positions[k], positions[(k + 4) % positions.Length]);
            if (HasLift)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(GetPosition(LiftStart), 1.5f);
                Gizmos.DrawSphere(GetPosition(LiftEnd), 1.5f);
            }
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(GetPosition(StartLineDistance), 1.5f);
        }
    }
}

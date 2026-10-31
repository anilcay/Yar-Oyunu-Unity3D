using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Track
{
    /// <summary>
    /// Builds the road mesh from a TrackPath: dark asphalt surface, raised yellow/black striped curbs,
    /// an optional embankment skirt down to the ground and invisible barrier walls (collider only).
    /// Also places prefab modules (guardrails, nets...) along the track. Works in editor and at runtime.
    /// </summary>
    [RequireComponent(typeof(TrackPath))]
    public class TrackMeshBuilder : MonoBehaviour
    {
        public TrackPath Track;

        [Header("Materials (0 road, 1 curb yellow, 2 curb black, 3 skirt)")]
        public Material RoadMaterial;
        public Material CurbYellowMaterial;
        public Material CurbBlackMaterial;
        public Material SkirtMaterial;

        [Header("Cross section")]
        public float CurbWidth = 0.35f;
        public float CurbHeight = 0.12f;
        [Tooltip("Length of one yellow or black curb stripe.")]
        public float StripeLength = 1f;
        public float BarrierHeight = 1.2f;
        public bool BuildSkirt = true;
        public float SkirtGroundY = 0f;
        [Tooltip("Outward lean of the skirt per metre of height.")]
        public float SkirtSlope = 0.08f;
        public float RoadUvScale = 0.25f;

        [Header("Layers")]
        public int RoadLayer = 8;     // Track
        public int BarrierLayer = 10; // Barrier

        [Tooltip("Rebuild meshes on Awake (meshes are not saved as assets).")]
        public bool BuildOnAwake = true;

        private const string RoadName = "Road";
        private const string BarrierName = "Barriers";

        private void Awake()
        {
            if (BuildOnAwake) Build();
        }

        public void Build()
        {
            if (Track == null) Track = GetComponent<TrackPath>();
            Track.EnsureBuilt();

            var road = GetOrCreateChild(RoadName, RoadLayer);
            var roadMesh = BuildRoadMesh();
            road.GetComponent<MeshFilter>().sharedMesh = roadMesh;
            road.GetComponent<MeshRenderer>().sharedMaterials = new[] { RoadMaterial, CurbYellowMaterial, CurbBlackMaterial, SkirtMaterial };
            var roadCollider = road.GetComponent<MeshCollider>();
            roadCollider.sharedMesh = null;
            roadCollider.sharedMesh = BuildRoadColliderMesh();

            var barrier = GetOrCreateChild(BarrierName, BarrierLayer, withRenderer: false);
            var barrierCollider = barrier.GetComponent<MeshCollider>();
            barrierCollider.sharedMesh = null;
            barrierCollider.sharedMesh = BuildBarrierMesh();
        }

        private GameObject GetOrCreateChild(string childName, int layer, bool withRenderer = true)
        {
            var t = transform.Find(childName);
            GameObject go = t != null ? t.gameObject : new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.layer = layer;
            if (withRenderer)
            {
                if (!go.TryGetComponent<MeshFilter>(out _)) go.AddComponent<MeshFilter>();
                if (!go.TryGetComponent<MeshRenderer>(out _)) go.AddComponent<MeshRenderer>();
            }
            if (!go.TryGetComponent<MeshCollider>(out _)) go.AddComponent<MeshCollider>();
            return go;
        }

        // ---------- Mesh generation ----------

        /// <summary>A ribbon between two cross-section points, swept along every track sample.</summary>
        private class Strip
        {
            public readonly List<Vector3> A = new List<Vector3>();
            public readonly List<Vector3> B = new List<Vector3>();
            public Func<int, int> Submesh;
            public Vector3 DesiredNormal0;
            public float VScale = 1f;
        }

        private struct Section
        {
            public Vector3 LeftEdge, RightEdge, LeftCurbIn, RightCurbIn, LeftCurbOut, RightCurbOut, LeftGround, RightGround;
            public Vector3 Up, Right;
        }

        private Section[] ComputeSections(out int count)
        {
            count = Track.SampleCount;
            var s = new Section[count];
            for (int k = 0; k < count; k++)
            {
                Track.GetSample(k, out var p, out _, out var up, out var right, out var width);
                float hw = width * 0.5f;
                var sec = new Section { Up = up, Right = right };
                sec.LeftEdge = p - right * hw;
                sec.RightEdge = p + right * hw;
                sec.LeftCurbIn = sec.LeftEdge - right * 0.06f + up * CurbHeight;
                sec.RightCurbIn = sec.RightEdge + right * 0.06f + up * CurbHeight;
                sec.LeftCurbOut = sec.LeftEdge - right * CurbWidth + up * CurbHeight;
                sec.RightCurbOut = sec.RightEdge + right * CurbWidth + up * CurbHeight;
                sec.LeftGround = SkirtFoot(sec.LeftCurbOut, -right);
                sec.RightGround = SkirtFoot(sec.RightCurbOut, right);
                s[k] = sec;
            }
            return s;
        }

        private Vector3 SkirtFoot(Vector3 top, Vector3 outward)
        {
            float h = Mathf.Max(0.2f, top.y - SkirtGroundY);
            Vector3 flatOut = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            return new Vector3(top.x, SkirtGroundY - 0.5f, top.z) + flatOut * (h * SkirtSlope);
        }

        private int StripeOf(int k) => Mathf.FloorToInt(k * Track.SampleStep / Mathf.Max(0.1f, StripeLength)) % 2 == 0 ? 1 : 2;

        private Mesh BuildRoadMesh()
        {
            var sec = ComputeSections(out int n);
            var strips = new List<Strip>();
            Func<int, int> road = _ => 0, stripe = StripeOf, skirt = _ => 3;

            strips.Add(MakeStrip(sec, n, x => x.LeftEdge, x => x.RightEdge, road, sec[0].Up, RoadUvScale));
            // Left curb: top + inner slope. Right curb: inner slope + top.
            strips.Add(MakeStrip(sec, n, x => x.LeftCurbOut, x => x.LeftCurbIn, stripe, sec[0].Up, 1f));
            strips.Add(MakeStrip(sec, n, x => x.LeftCurbIn, x => x.LeftEdge, stripe, sec[0].Right, 1f));
            strips.Add(MakeStrip(sec, n, x => x.RightEdge, x => x.RightCurbIn, stripe, -sec[0].Right, 1f));
            strips.Add(MakeStrip(sec, n, x => x.RightCurbIn, x => x.RightCurbOut, stripe, sec[0].Up, 1f));
            if (BuildSkirt)
            {
                strips.Add(MakeStrip(sec, n, x => x.LeftCurbOut, x => x.LeftGround, skirt, -sec[0].Right, 0.25f));
                strips.Add(MakeStrip(sec, n, x => x.RightCurbOut, x => x.RightGround, skirt, sec[0].Right, 0.25f));
            }
            return Assemble("TrackRoad", strips, n, 4);
        }

        /// <summary>Collider: road + curbs only (the skirt is visual).</summary>
        private Mesh BuildRoadColliderMesh()
        {
            var sec = ComputeSections(out int n);
            var strips = new List<Strip>();
            Func<int, int> zero = _ => 0;
            strips.Add(MakeStrip(sec, n, x => x.LeftEdge, x => x.RightEdge, zero, sec[0].Up, 1f));
            strips.Add(MakeStrip(sec, n, x => x.LeftCurbOut, x => x.LeftCurbIn, zero, sec[0].Up, 1f));
            strips.Add(MakeStrip(sec, n, x => x.LeftCurbIn, x => x.LeftEdge, zero, sec[0].Right, 1f));
            strips.Add(MakeStrip(sec, n, x => x.RightEdge, x => x.RightCurbIn, zero, -sec[0].Right, 1f));
            strips.Add(MakeStrip(sec, n, x => x.RightCurbIn, x => x.RightCurbOut, zero, sec[0].Up, 1f));
            return Assemble("TrackRoadCollider", strips, n, 1);
        }

        private Mesh BuildBarrierMesh()
        {
            var sec = ComputeSections(out int n);
            var strips = new List<Strip>();
            Func<int, int> zero = _ => 0;
            float h = BarrierHeight;
            // Walls face the road centre.
            strips.Add(MakeStrip(sec, n, x => x.LeftCurbOut + x.Up * h, x => x.LeftCurbOut - x.Up * 0.3f, zero, sec[0].Right, 1f));
            strips.Add(MakeStrip(sec, n, x => x.RightCurbOut - x.Up * 0.3f, x => x.RightCurbOut + x.Up * h, zero, -sec[0].Right, 1f));
            return Assemble("TrackBarrier", strips, n, 1);
        }

        private static Strip MakeStrip(Section[] sec, int n, Func<Section, Vector3> a, Func<Section, Vector3> b, Func<int, int> submesh, Vector3 desiredNormal, float vScale)
        {
            var s = new Strip { Submesh = submesh, DesiredNormal0 = desiredNormal, VScale = vScale };
            for (int k = 0; k < n; k++)
            {
                s.A.Add(a(sec[k]));
                s.B.Add(b(sec[k]));
            }
            // Ensure the first quad faces the desired side (Unity: clockwise = front).
            Vector3 normal = Vector3.Cross(s.A[1 % n] - s.A[0], s.B[0] - s.A[0]);
            if (Vector3.Dot(normal, desiredNormal) < 0f)
            {
                for (int k = 0; k < n; k++) (s.A[k], s.B[k]) = (s.B[k], s.A[k]);
            }
            return s;
        }

        private Mesh Assemble(string meshName, List<Strip> strips, int n, int submeshCount)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) tris[i] = new List<int>();
            float step = Track.SampleStep;

            foreach (var s in strips)
            {
                int baseIndex = verts.Count;
                // n + 1 rings so UVs can run continuously around the closed loop.
                for (int k = 0; k <= n; k++)
                {
                    int kk = k % n;
                    verts.Add(s.A[kk]);
                    verts.Add(s.B[kk]);
                    float v = k * step * s.VScale;
                    uvs.Add(new Vector2(0f, v));
                    uvs.Add(new Vector2(1f, v));
                }
                for (int k = 0; k < n; k++)
                {
                    int a0 = baseIndex + k * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    var list = tris[Mathf.Clamp(s.Submesh(k), 0, submeshCount - 1)];
                    list.Add(a0); list.Add(a1); list.Add(b0);
                    list.Add(b0); list.Add(a1); list.Add(b1);
                }
            }

            var mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshCount;
            for (int i = 0; i < submeshCount; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Prefab placement ----------

        /// <summary>
        /// Places <paramref name="prefab"/> modules end to end along one side of the track.
        /// side: -1 left, +1 right. The module pivot is put on the outer curb edge, forward = track tangent.
        /// </summary>
        public List<GameObject> PlaceAlong(GameObject prefab, float moduleLength, int side, float lateralOffset,
            float fromDistance, float toDistance, Transform parent,
            Func<float, bool> filter = null, Func<GameObject, Transform, GameObject> instantiate = null)
        {
            var placed = new List<GameObject>();
            if (prefab == null || moduleLength <= 0.1f) return placed;
            if (Track == null) Track = GetComponent<TrackPath>();
            Track.EnsureBuilt();
            instantiate ??= (p, par) => Instantiate(p, par);

            float span = Track.Wrap(toDistance - fromDistance);
            if (span <= 0f) span = Track.Length;
            for (float s = 0f; s + moduleLength <= span + 0.01f; s += moduleLength)
            {
                float d = Track.Wrap(fromDistance + s + moduleLength * 0.5f);
                if (filter != null && !filter(d)) continue;
                Vector3 right = Track.GetRight(d);
                Vector3 up = Track.GetUp(d);
                float offset = Track.GetWidth(d) * 0.5f + CurbWidth + lateralOffset;
                Vector3 pos = Track.GetPosition(d) + right * (side * offset) + up * CurbHeight;
                var go = instantiate(prefab, parent);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Track.GetTangent(d), up));
                placed.Add(go);
            }
            return placed;
        }
    }
}

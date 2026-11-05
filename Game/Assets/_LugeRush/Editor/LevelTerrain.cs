using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LugeRush.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Editor
{
    /// <summary>Track samples with fast nearest queries (horizontal metrics).</summary>
    public class LevelTrackField
    {
        public const float Step = 2f;
        public readonly TrackPath Path;
        public readonly float CurbWidth;
        public readonly int Count;
        public readonly Vector3[] Pos, Right;
        public readonly float[] HalfEdge;   // half road width + curb
        private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
        private const float BucketSize = 16f;

        public LevelTrackField(TrackPath path, float curbWidth)
        {
            Path = path;
            CurbWidth = curbWidth;
            Count = Mathf.CeilToInt(path.Length / Step);
            Pos = new Vector3[Count];
            Right = new Vector3[Count];
            HalfEdge = new float[Count];
            for (int k = 0; k < Count; k++)
            {
                float d = k * Step;
                Pos[k] = path.GetPosition(d);
                Right[k] = path.GetRight(d);
                HalfEdge[k] = path.GetWidth(d) * 0.5f + curbWidth;
                long key = Key(Pos[k].x, Pos[k].z);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(k);
            }
        }

        private static long Key(float x, float z) => ((long)Mathf.FloorToInt(x / BucketSize) << 32) ^ (uint)Mathf.FloorToInt(z / BucketSize);

        /// <summary>Nearest sample within maxDistance, or -1. dist = horizontal distance to the centreline.</summary>
        public int Nearest(float x, float z, float maxDistance, out float dist)
        {
            int r = Mathf.CeilToInt(maxDistance / BucketSize);
            int cx = Mathf.FloorToInt(x / BucketSize), cz = Mathf.FloorToInt(z / BucketSize);
            int best = -1;
            float bestSq = maxDistance * maxDistance;
            for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
            {
                long key = ((long)(cx + i) << 32) ^ (uint)(cz + j);
                if (!buckets.TryGetValue(key, out var list)) continue;
                foreach (int k in list)
                {
                    float dx = Pos[k].x - x, dz = Pos[k].z - z;
                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq) { bestSq = sq; best = k; }
                }
            }
            dist = best >= 0 ? Mathf.Sqrt(bestSq) : float.MaxValue;
            return best;
        }

        /// <summary>Distance from the road's outer curb edge (negative inside the road), MaxValue when far.</summary>
        public float EdgeDistance(float x, float z, float maxDistance)
        {
            int k = Nearest(x, z, maxDistance, out float d);
            return k < 0 ? float.MaxValue : d - HalfEdge[k];
        }

        public int SampleAt(float distance) => Mathf.Clamp(Mathf.RoundToInt(Path.Wrap(distance) / Step), 0, Count - 1);
    }

    /// <summary>Side-of-track terrain profile over a distance range.</summary>
    public struct LevelZone
    {
        public float From, To;      // track distance
        public float Left, Right;   // height offset the terrain reaches away from the road
        public float Range;         // metres from the road over which the offset is reached
        public float Blend;         // road -> terrain blend width
        public float Fade;          // along-track fade in / out
    }

    /// <summary>Flattens the terrain around a set piece.</summary>
    public struct LevelStamp
    {
        public Vector3 Center;      // y = target height
        public float Radius, Falloff;
    }

    public class LevelTerrainParams
    {
        public int Seed;
        public float DefaultBlend = 9f;
        public float DefaultRange = 60f;
        public float DefaultLeft, DefaultRight;
        public float FarRise = 30f;           // large scale hills away from the track
        public float FarRiseStart = 40f, FarRiseEnd = 240f;
        public float NoiseScale = 1f;
        public float TerraceStep;             // > 0: terraced mesas far from the track
        public float BorderRise;              // rises the rim of the map (valley feeling)
        public List<LevelZone> Zones = new List<LevelZone>();
        public List<LevelStamp> Stamps = new List<LevelStamp>();
        /// <summary>(height above lowest road point, slope 0..1, noise 0..1, distance from the road) -> palette cell.</summary>
        public Func<float, float, float, float, string> Paint;
    }

    /// <summary>
    /// Procedural low-poly terrain wrapped around the track: a Delaunay triangulation of road edge rows plus
    /// jittered points whose spacing grows with the distance from the road. Conforms exactly to the curb line and
    /// leaves a hole under the road, so neither the mesh nor its collider touches the driving surface.
    /// Flat shaded; every face is a palette cell chosen by height, slope and noise.
    /// </summary>
    public class LevelTerrain
    {
        private const float TileSize = 140f;
        private const float ColliderBand = 45f;

        private readonly LevelTrackField field;
        private readonly LevelTerrainParams p;
        private readonly System.Random rng;
        private readonly Vector2 noiseOffset;
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<bool> under = new List<bool>();
        private int[] tris;
        private float minRoadY;
        private Bounds trackBounds;

        // Coarse samples for the height field.
        private Vector3[] coarsePos;
        private Vector3[] coarseRight;
        private float[] coarseLeft, coarseRight2, coarseRange;
        private float[] blendWidth;

        // Surface lookup
        private readonly Dictionary<long, List<int>> triGrid = new Dictionary<long, List<int>>();
        private const float TriCell = 20f;

        public int TriangleCount => tris != null ? tris.Length / 3 : 0;
        public int VertexPointCount => points.Count;

        public LevelTerrain(LevelTrackField field, LevelTerrainParams parameters)
        {
            this.field = field;
            p = parameters;
            rng = new System.Random(p.Seed);
            noiseOffset = new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
            PrepareProfile();
        }

        // ---------- Height ----------

        private float Fbm(float x, float z, float scale, int octaves = 4)
        {
            float a = 1f, sum = 0f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += a * (Mathf.PerlinNoise(noiseOffset.x + x / scale, noiseOffset.y + z / scale) - 0.5f);
                total += a;
                a *= 0.5f;
                scale *= 0.5f;
            }
            return sum / total * 2f;
        }

        private void PrepareProfile()
        {
            int n = field.Count;
            minRoadY = float.MaxValue;
            trackBounds = new Bounds(field.Pos[0], Vector3.zero);
            for (int k = 0; k < n; k++)
            {
                minRoadY = Mathf.Min(minRoadY, field.Pos[k].y);
                trackBounds.Encapsulate(field.Pos[k]);
            }

            var left = new float[n];
            var right = new float[n];
            var range = new float[n];
            blendWidth = new float[n];
            for (int k = 0; k < n; k++)
            {
                float d = k * LevelTrackField.Step;
                float wSum = 0f, l = 0f, r = 0f, rg = 0f, b = 0f;
                foreach (var z in p.Zones)
                {
                    float span = field.Path.Wrap(z.To - z.From);
                    float rel = field.Path.Wrap(d - z.From);
                    float w;
                    if (rel <= span) w = Mathf.Min(Mathf.SmoothStep(0f, 1f, rel / Mathf.Max(1f, z.Fade)), Mathf.SmoothStep(0f, 1f, (span - rel) / Mathf.Max(1f, z.Fade)));
                    else w = 0f;
                    if (w <= 0f) continue;
                    wSum += w;
                    l += w * z.Left; r += w * z.Right;
                    rg += w * z.Range; b += w * z.Blend;
                }
                float rest = Mathf.Clamp01(1f - wSum);
                float norm = wSum + rest;
                left[k] = (l + rest * p.DefaultLeft) / norm;
                right[k] = (r + rest * p.DefaultRight) / norm;
                range[k] = (rg + rest * p.DefaultRange) / norm;
                blendWidth[k] = (b + rest * p.DefaultBlend) / norm;
            }

            int m = n / 4;
            coarsePos = new Vector3[m];
            coarseRight = new Vector3[m];
            coarseLeft = new float[m];
            coarseRight2 = new float[m];
            coarseRange = new float[m];
            for (int i = 0; i < m; i++)
            {
                int k = i * 4;
                coarsePos[i] = field.Pos[k];
                coarseRight[i] = field.Right[k];
                coarseLeft[i] = left[k];
                coarseRight2[i] = right[k];
                coarseRange[i] = range[k];
            }
        }

        /// <summary>Smooth interpolation of the road heights + side profiles (Shepard, r^-4), no noise.</summary>
        private float ShapeField(float x, float z)
        {
            double wSum = 0.0, hSum = 0.0;
            for (int i = 0; i < coarsePos.Length; i++)
            {
                float dx = x - coarsePos[i].x, dz = z - coarsePos[i].z;
                float sq = dx * dx + dz * dz + 4f;
                double w = 1.0 / (sq * sq);
                float r = Mathf.Sqrt(sq);
                float side = dx * coarseRight[i].x + dz * coarseRight[i].z;
                float bias = side > 0f ? coarseRight2[i] : coarseLeft[i];
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 3f) / Mathf.Max(4f, coarseRange[i])));
                wSum += w;
                hSum += w * (coarsePos[i].y + bias * ramp);
            }
            return (float)(hSum / wSum);
        }

        private float Height(float x, float z)
        {
            int k = field.Nearest(x, z, 90f, out float dist);
            float shape = ShapeField(x, z);
            float s = p.NoiseScale;

            float edgeD = k >= 0 ? dist - field.HalfEdge[k] : 200f;
            float away = Mathf.Max(0f, k >= 0 ? edgeD : EdgeDistanceFar(x, z));
            float noiseAmp = Mathf.Lerp(0.3f, 4.5f, Mathf.Clamp01(away / 45f)) * s;
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.FarRiseStart, p.FarRiseEnd, away));
            float h = shape
                      + Fbm(x, z, 90f) * noiseAmp
                      + far * (p.FarRise * (0.55f + 1.1f * Mathf.Abs(Fbm(x + 300f, z - 200f, 260f))) + Fbm(x, z, 150f, 3) * 16f * s);
            if (p.TerraceStep > 0f && away > 12f)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 50f, away));
                float step = p.TerraceStep;
                float q = h / step;
                float f = q - Mathf.Floor(q);
                float terraced = (Mathf.Floor(q) + Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - 0.72f) / 0.28f))) * step;
                h = Mathf.Lerp(h, terraced, t);
            }
            if (p.BorderRise != 0f)
            {
                Vector3 c = trackBounds.center;
                float ext = Mathf.Max(trackBounds.extents.x, trackBounds.extents.z);
                float rr = new Vector2(x - c.x, z - c.z).magnitude;
                h += p.BorderRise * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ext + 80f, ext + 420f, rr));
            }

            if (k >= 0)
            {
                // Blend into the road plane (banking extended past the curb, kept level from there).
                float side = Mathf.Sign((x - field.Pos[k].x) * field.Right[k].x + (z - field.Pos[k].z) * field.Right[k].z);
                float roadY = field.Pos[k].y + field.Right[k].y * side * field.HalfEdge[k] - 0.02f;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edgeD - 0.4f) / Mathf.Max(0.5f, blendWidth[k])));
                h = Mathf.Lerp(roadY, h, t);
            }

            foreach (var st in p.Stamps)
            {
                float d = new Vector2(x - st.Center.x, z - st.Center.z).magnitude;
                if (d >= st.Radius + st.Falloff) continue;
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - st.Radius) / Mathf.Max(0.5f, st.Falloff)));
                w *= Mathf.Clamp01((edgeD - 0.3f) / 1.5f);
                h = Mathf.Lerp(h, st.Center.y, w);
            }
            return h;
        }

        private float EdgeDistanceFar(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i < coarsePos.Length; i += 2)
            {
                float dx = x - coarsePos[i].x, dz = z - coarsePos[i].z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(best);
        }

        // ---------- Point set ----------

        private readonly Dictionary<long, List<Vector2>> occupancy = new Dictionary<long, List<Vector2>>();
        private const float OccCell = 8f;

        private static long OccKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private bool TryReserve(float x, float z, float spacing)
        {
            int cx = Mathf.FloorToInt(x / OccCell), cz = Mathf.FloorToInt(z / OccCell);
            int r = Mathf.CeilToInt(spacing / OccCell);
            float sq = spacing * spacing;
            for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
            {
                if (!occupancy.TryGetValue(OccKey(cx + i, cz + j), out var list)) continue;
                foreach (var q in list)
                    if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < sq) return false;
            }
            long key = OccKey(cx, cz);
            if (!occupancy.TryGetValue(key, out var l)) occupancy[key] = l = new List<Vector2>();
            l.Add(new Vector2(x, z));
            return true;
        }

        private float Jitter(float a) => ((float)rng.NextDouble() - 0.5f) * a;

        private void AddPoint(Vector3 pos, bool isUnder)
        {
            // Tiny offset keeps regular rows from being exactly cocircular.
            points.Add(new Vector3(pos.x + Jitter(0.006f), pos.y, pos.z + Jitter(0.006f)));
            under.Add(isUnder);
        }

        private void BuildPoints(float margin)
        {
            int n = field.Count;
            float curbTop = 0.1f;
            // Exact rows along both road edges (every sample): one under the curb, one at its outer face.
            for (int k = 0; k < n; k++)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    float he = field.HalfEdge[k];
                    Vector3 r = field.Right[k];
                    Vector3 pIn = field.Pos[k] + r * (side * (he - 0.3f));
                    Vector3 pOut = field.Pos[k] + r * (side * (he + 0.02f));
                    if (OnOtherRoad(pOut, he, k)) continue;
                    TryReserve(pIn.x, pIn.z, 0.1f);
                    TryReserve(pOut.x, pOut.z, 0.1f);
                    AddPoint(pIn - Vector3.up * 0.25f, true);
                    AddPoint(pOut + Vector3.up * (curbTop - 0.03f), false);
                }
            }
            // Shoulder rows: low-poly transition next to the road.
            foreach (var row in new[] { (off: 1.1f, every: 2, sp: 1.2f), (off: 2.8f, every: 3, sp: 2f), (off: 5.5f, every: 4, sp: 3f) })
            {
                for (int k = 0; k < n; k += row.every)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        float off = field.HalfEdge[k] + row.off + Jitter(row.off * 0.3f);
                        Vector3 q = field.Pos[k] + field.Right[k] * (side * off) + new Vector3(Jitter(0.6f), 0f, Jitter(0.6f));
                        float e = field.EdgeDistance(q.x, q.z, 30f);
                        if (e < row.off * 0.55f) continue;
                        if (!TryReserve(q.x, q.z, row.sp)) continue;
                        AddPoint(new Vector3(q.x, Height(q.x, q.z), q.z), false);
                    }
                }
            }
            // Jittered fill: spacing grows with the distance from the road.
            Bounds b = trackBounds;
            foreach (var pass in new[] { (sp: 3.2f, from: 0f, to: 30f), (sp: 5.5f, from: 24f, to: 90f), (sp: 10f, from: 75f, to: 240f), (sp: 24f, from: 200f, to: 480f), (sp: 55f, from: 420f, to: 99999f) })
            {
                float x0 = b.min.x - margin, x1 = b.max.x + margin, z0 = b.min.z - margin, z1 = b.max.z + margin;
                for (float x = x0; x <= x1; x += pass.sp)
                for (float z = z0; z <= z1; z += pass.sp)
                {
                    float qx = x + Jitter(pass.sp * 0.9f), qz = z + Jitter(pass.sp * 0.9f);
                    float e = pass.to < 100f ? field.EdgeDistance(qx, qz, pass.to + 12f) : EdgeDistanceFar(qx, qz);
                    if (e < Mathf.Max(pass.from, 6f) || e > pass.to) continue;
                    if (!TryReserve(qx, qz, pass.sp * 0.72f)) continue;
                    AddPoint(new Vector3(qx, Height(qx, qz), qz), false);
                }
            }
        }

        private bool OnOtherRoad(Vector3 q, float he, int own)
        {
            int k = field.Nearest(q.x, q.z, 20f, out float d);
            if (k < 0) return false;
            int sep = Mathf.Abs(k - own);
            sep = Mathf.Min(sep, field.Count - sep);
            return sep > 4 && d < field.HalfEdge[k] + 0.05f;
        }

        // ---------- Delaunay (Bowyer-Watson with walking point location) ----------

        private struct DT
        {
            public int A, B, C;      // counter-clockwise in (x, z)
            public int N0, N1, N2;   // neighbour across the edge opposite to A, B, C
            public bool Dead;
        }

        private double[] px, pz;
        private List<DT> dts;

        private static double Orient(double ax, double ay, double bx, double by, double cx, double cy) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

        private bool InCircle(in DT t, int v)
        {
            double ax = px[t.A] - px[v], ay = pz[t.A] - pz[v];
            double bx = px[t.B] - px[v], by = pz[t.B] - pz[v];
            double cx = px[t.C] - px[v], cy = pz[t.C] - pz[v];
            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            double det = ax * (by * c2 - b2 * cy) - ay * (bx * c2 - b2 * cx) + a2 * (bx * cy - by * cx);
            return det > 1e-9;
        }

        private int Locate(int v, int start)
        {
            int t = start;
            double x = px[v], y = pz[v];
            for (int guard = 0; guard < 200000; guard++)
            {
                var d = dts[t];
                if (d.N0 >= -1 && Orient(px[d.B], pz[d.B], px[d.C], pz[d.C], x, y) < 0.0 && d.N0 >= 0) { t = d.N0; continue; }
                if (Orient(px[d.C], pz[d.C], px[d.A], pz[d.A], x, y) < 0.0 && d.N1 >= 0) { t = d.N1; continue; }
                if (Orient(px[d.A], pz[d.A], px[d.B], pz[d.B], x, y) < 0.0 && d.N2 >= 0) { t = d.N2; continue; }
                return t;
            }
            throw new InvalidOperationException("Delaunay walk did not terminate.");
        }

        private void Triangulate()
        {
            int n = points.Count;
            px = new double[n + 3];
            pz = new double[n + 3];
            double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                px[i] = points[i].x; pz[i] = points[i].z;
                minX = Math.Min(minX, px[i]); maxX = Math.Max(maxX, px[i]);
                minZ = Math.Min(minZ, pz[i]); maxZ = Math.Max(maxZ, pz[i]);
            }
            double span = Math.Max(maxX - minX, maxZ - minZ) * 20.0;
            double midX = (minX + maxX) * 0.5, midZ = (minZ + maxZ) * 0.5;
            px[n] = midX - span; pz[n] = midZ - span;
            px[n + 1] = midX + span; pz[n + 1] = midZ - span;
            px[n + 2] = midX; pz[n + 2] = midZ + span;

            dts = new List<DT>(n * 3) { new DT { A = n, B = n + 1, C = n + 2, N0 = -1, N1 = -1, N2 = -1 } };

            // Spatially coherent insertion order keeps the walks short.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            const double cell = 25.0;
            Array.Sort(order, (a, b) =>
            {
                int ca = (int)Math.Floor((pz[a] - minZ) / cell), cb = (int)Math.Floor((pz[b] - minZ) / cell);
                if (ca != cb) return ca.CompareTo(cb);
                int xa = (int)Math.Floor((px[a] - minX) / cell), xb = (int)Math.Floor((px[b] - minX) / cell);
                if (xa != xb) return (ca % 2 == 0) ? xa.CompareTo(xb) : xb.CompareTo(xa);
                return a.CompareTo(b);
            });

            var cavity = new List<int>();
            var stack = new Stack<int>();
            var mark = new Dictionary<int, bool>();
            var edges = new List<(int u, int v, int outer, int outerSlot)>();
            var fan = new Dictionary<int, int>(); // edge start vertex -> new triangle
            int last = 0;
            foreach (int v in order)
            {
                int seed = Locate(v, last);
                cavity.Clear(); mark.Clear(); edges.Clear(); fan.Clear();
                stack.Push(seed);
                mark[seed] = true;
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    cavity.Add(t);
                    var d = dts[t];
                    void Visit(int nb)
                    {
                        if (nb < 0 || mark.ContainsKey(nb)) return;
                        bool inside = InCircle(dts[nb], v);
                        mark[nb] = inside;
                        if (inside) stack.Push(nb);
                    }
                    Visit(d.N0); Visit(d.N1); Visit(d.N2);
                }
                foreach (int t in cavity)
                {
                    var d = dts[t];
                    void Edge(int a, int b, int nb)
                    {
                        if (nb >= 0 && mark.TryGetValue(nb, out bool ins) && ins) return;
                        int slot = -1;
                        if (nb >= 0)
                        {
                            var o = dts[nb];
                            slot = o.N0 == t ? 0 : o.N1 == t ? 1 : 2;
                        }
                        edges.Add((a, b, nb, slot));
                    }
                    Edge(d.B, d.C, d.N0);
                    Edge(d.C, d.A, d.N1);
                    Edge(d.A, d.B, d.N2);
                    d.Dead = true;
                    dts[t] = d;
                }

                // New fan around v. Each boundary edge (u -> w) keeps its CCW orientation.
                int firstNew = dts.Count;
                foreach (var e in edges)
                {
                    int id = dts.Count;
                    dts.Add(new DT { A = v, B = e.u, C = e.v, N0 = e.outer, N1 = -1, N2 = -1 });
                    fan[e.u] = id;
                    if (e.outer >= 0)
                    {
                        var o = dts[e.outer];
                        if (e.outerSlot == 0) o.N0 = id; else if (e.outerSlot == 1) o.N1 = id; else o.N2 = id;
                        dts[e.outer] = o;
                    }
                }
                for (int id = firstNew; id < dts.Count; id++)
                {
                    var t = dts[id];
                    // Edge C->A (opposite B) borders the triangle starting at C; edge A->B borders the one ending at B.
                    t.N1 = fan[t.C];
                    dts[id] = t;
                }
                for (int id = firstNew; id < dts.Count; id++)
                {
                    var t = dts[id];
                    var other = dts[t.N1];
                    other.N2 = id;
                    dts[t.N1] = other;
                }
                last = firstNew;
            }

            var result = new List<int>(n * 6);
            for (int i = 0; i < dts.Count; i++)
            {
                var t = dts[i];
                if (t.Dead || t.A >= n || t.B >= n || t.C >= n) continue;
                if (IsRoadHole(t.A, t.B, t.C)) continue;
                result.Add(t.A); result.Add(t.B); result.Add(t.C);
            }
            tris = result.ToArray();
            dts = null;
        }

        private bool IsRoadHole(int a, int b, int c)
        {
            if (under[a] && under[b] && under[c]) return true;
            Vector3 m = (points[a] + points[b] + points[c]) / 3f;
            int k = field.Nearest(m.x, m.z, 14f, out float d);
            return k >= 0 && d < field.HalfEdge[k] - 0.05f;
        }

        // ---------- Surface queries ----------

        private static long TriKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        private void BuildTriGrid()
        {
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = points[tris[i]], b = points[tris[i + 1]], c = points[tris[i + 2]];
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) / TriCell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) / TriCell);
                int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z) / TriCell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z) / TriCell);
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = TriKey(x, z);
                    if (!triGrid.TryGetValue(key, out var list)) triGrid[key] = list = new List<int>();
                    list.Add(i);
                }
            }
        }

        /// <summary>Terrain height (exact mesh surface) at x,z and the face normal. False in the road hole.</summary>
        public bool Sample(float x, float z, out float height, out Vector3 normal)
        {
            height = 0f;
            normal = Vector3.up;
            if (!triGrid.TryGetValue(TriKey(Mathf.FloorToInt(x / TriCell), Mathf.FloorToInt(z / TriCell)), out var list)) return false;
            foreach (int i in list)
            {
                Vector3 a = points[tris[i]], b = points[tris[i + 1]], c = points[tris[i + 2]];
                float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(det) < 1e-7f) continue;
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / det;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / det;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                height = l1 * a.y + l2 * b.y + l3 * c.y;
                normal = Vector3.Cross(b - a, c - a).normalized;
                if (normal.y < 0f) normal = -normal;
                return true;
            }
            return false;
        }

        // ---------- Build ----------

        public void Generate(float margin = 800f)
        {
            BuildPoints(margin);
            Triangulate();
            BuildTriGrid();
        }

        /// <summary>Palette atlas UVs by cell name (LR_Palette.json).</summary>
        public static Dictionary<string, Vector2> LoadPalette()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "_LugeRush/Art/Textures/LR_Palette.json"));
            var map = new Dictionary<string, Vector2>();
            foreach (Match m in Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*\\{\\s*\"col\"\\s*:\\s*(\\d+)\\s*,\\s*\"row\"\\s*:\\s*(\\d+)"))
            {
                int col = int.Parse(m.Groups[2].Value), row = int.Parse(m.Groups[3].Value);
                map[m.Groups[1].Value] = new Vector2((col + 0.5f) / 16f, 1f - (row + 0.5f) / 16f);
            }
            return map;
        }

        /// <summary>Writes the tiled flat shaded meshes + near-track colliders under parent.</summary>
        public void BuildObjects(Transform parent, string meshFolder, string prefix, Material material, int layer)
        {
            var palette = LoadPalette();
            var tiles = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 m = (points[tris[i]] + points[tris[i + 1]] + points[tris[i + 2]]) / 3f;
                var key = new Vector2Int(Mathf.FloorToInt(m.x / TileSize), Mathf.FloorToInt(m.z / TileSize));
                if (!tiles.TryGetValue(key, out var list)) tiles[key] = list = new List<int>();
                list.Add(i);
            }

            var root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);
            EditorAssetUtil.EnsureFolder(meshFolder);
            foreach (var old in AssetDatabase.FindAssets("t:Mesh", new[] { meshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(old);
                if (Path.GetFileName(path).StartsWith(prefix)) AssetDatabase.DeleteAsset(path);
            }

            int tileIndex = 0;
            foreach (var kv in tiles)
            {
                var verts = new List<Vector3>(kv.Value.Count * 3);
                var normals = new List<Vector3>(kv.Value.Count * 3);
                var uvs = new List<Vector2>(kv.Value.Count * 3);
                var colVerts = new List<Vector3>();
                var colTris = new List<int>();
                foreach (int i in kv.Value)
                {
                    Vector3 a = points[tris[i]], b = points[tris[i + 1]], c = points[tris[i + 2]];
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    if (nrm.y < 0f) { (b, c) = (c, b); nrm = -nrm; }
                    nrm.Normalize();
                    Vector3 m = (a + b + c) / 3f;
                    float edge = field.EdgeDistance(m.x, m.z, 60f);
                    float noise = Mathf.PerlinNoise(noiseOffset.y + m.x / 34f, noiseOffset.x + m.z / 34f) * 0.65f + (float)rng.NextDouble() * 0.35f;
                    string cell = p.Paint(m.y - minRoadY, 1f - nrm.y, noise, edge);
                    if (!palette.TryGetValue(cell, out Vector2 uv)) throw new InvalidOperationException($"Unknown palette cell {cell}");
                    verts.Add(a); verts.Add(b); verts.Add(c);
                    normals.Add(nrm); normals.Add(nrm); normals.Add(nrm);
                    uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
                    if (edge < ColliderBand)
                    {
                        int s = colVerts.Count;
                        colVerts.Add(a); colVerts.Add(b); colVerts.Add(c);
                        colTris.Add(s); colTris.Add(s + 1); colTris.Add(s + 2);
                    }
                }
                var triIdx = new int[verts.Count];
                for (int i = 0; i < triIdx.Length; i++) triIdx[i] = i;
                var mesh = new Mesh { name = $"{prefix}_{tileIndex}", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triIdx, 0);
                mesh.RecalculateBounds();
                mesh = EditorAssetUtil.SaveMesh(mesh, meshFolder, mesh.name);

                var go = new GameObject($"TerrainTile_{tileIndex}");
                go.transform.SetParent(root, false);
                go.layer = layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveGI = ReceiveGI.LightProbes;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

                if (colTris.Count > 0)
                {
                    var col = new Mesh { name = $"{prefix}_{tileIndex}_col", indexFormat = colVerts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    col.SetVertices(colVerts);
                    col.SetTriangles(colTris, 0);
                    col.RecalculateBounds();
                    col = EditorAssetUtil.SaveMesh(col, meshFolder, col.name);
                    go.AddComponent<MeshCollider>().sharedMesh = col;
                }
                tileIndex++;
            }
        }

        /// <summary>Counts the road edge points where the mesh rises over the curb and the centre points not in the hole.</summary>
        public void Validate(out int aboveRoad, out int notHoled)
        {
            aboveRoad = 0;
            notHoled = 0;
            for (int k = 0; k < field.Count; k++)
            {
                if (Sample(field.Pos[k].x, field.Pos[k].z, out float hc, out _) && hc > field.Pos[k].y - 0.02f) notHoled++;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 e = field.Pos[k] + field.Right[k] * (side * (field.HalfEdge[k] - 0.05f));
                    if (Sample(e.x, e.z, out float h, out _) && h > e.y + 0.13f) aboveRoad++;
                }
            }
        }
    }
}

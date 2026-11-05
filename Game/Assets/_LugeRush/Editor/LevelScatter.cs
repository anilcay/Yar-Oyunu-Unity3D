using System;
using System.Collections.Generic;
using LugeRush.Track;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>One scatter rule: prefabs spread in a band measured from the road's outer curb.</summary>
    public class LevelScatterRule
    {
        public string[] Prefabs;
        public float From, To;           // band (metres from the curb)
        public float Density;            // instances per m²
        public float Spacing;            // minimum distance inside SpacingClass
        public string SpacingClass = "";
        public float ScaleMin = 0.85f, ScaleMax = 1.15f;
        public float MaxSlope = 0.35f;   // 1 - normal.y
        public float Sink = 0.1f;
        public float Tilt = 3f;
        public bool Shadows = true;
        public float DrawDistance = 300f;
        public int Cluster = 1;          // instances per accepted spot
        public float ClusterRadius = 2f;
        public bool Tall;                // keeps away from the inside of tight corners, the lift and covered sections
        public float MinHeight = float.MinValue, MaxHeight = float.MaxValue; // above the lowest road point
    }

    /// <summary>A row of modules facing the road (cliffs, canyon walls).</summary>
    public class LevelRowRule
    {
        public string[] Prefabs;
        public float FromDistance, ToDistance;
        public int Side;                 // -1 left, +1 right
        public float Offset;             // metres from the curb
        public float Step = 9f;
        public float OffsetJitter = 1f;
        public float ScaleMin = 1f, ScaleMax = 1.15f;
        public float YawJitter = 8f;
        public bool UseRoadHeight;       // base at road level instead of on the terrain
        public float ClearBehind;        // > 0: skip modules with another road section this close behind them
        public float Sink = 0.3f;
        public float DrawDistance = 700f;
    }

    /// <summary>Seeded placement of the props of one map into a <see cref="LevelPropData"/> asset.</summary>
    public class LevelScatter
    {
        private readonly LevelTrackField field;
        private readonly LevelTerrain terrain;
        private readonly System.Random rng;
        private readonly float minRoadY;
        private readonly List<(Vector2 pos, float radius)> exclusions;
        private readonly Func<int, bool> tallBlocked;
        private readonly Dictionary<string, LevelPropData.Group> groups = new Dictionary<string, LevelPropData.Group>();
        private readonly Dictionary<string, Dictionary<long, List<Vector2>>> spacing = new Dictionary<string, Dictionary<long, List<Vector2>>>();
        private readonly Bounds bounds;

        public LevelScatter(LevelTrackField field, LevelTerrain terrain, int seed, List<(Vector2 pos, float radius)> exclusions, Func<int, bool> tallBlocked)
        {
            this.field = field;
            this.terrain = terrain;
            this.exclusions = exclusions;
            this.tallBlocked = tallBlocked;
            rng = new System.Random(seed);
            bounds = new Bounds(field.Pos[0], Vector3.zero);
            minRoadY = float.MaxValue;
            for (int k = 0; k < field.Count; k++)
            {
                bounds.Encapsulate(field.Pos[k]);
                minRoadY = Mathf.Min(minRoadY, field.Pos[k].y);
            }
        }

        private float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public static GameObject LoadPrefab(string relative) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFactory.EnvPrefabs}/{relative}.prefab");

        private LevelPropData.Group GroupFor(string prefabPath, bool shadows, float drawDistance)
        {
            if (groups.TryGetValue(prefabPath, out var g)) return g;
            var prefab = LoadPrefab(prefabPath);
            if (prefab == null) throw new InvalidOperationException($"Missing prefab {prefabPath}");
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            g = new LevelPropData.Group { Name = prefab.name, Mesh = mf.sharedMesh, Material = mr.sharedMaterial, CastShadows = shadows, DrawDistance = drawDistance };
            groups[prefabPath] = g;
            return g;
        }

        private bool Excluded(float x, float z)
        {
            foreach (var e in exclusions)
                if ((e.pos.x - x) * (e.pos.x - x) + (e.pos.y - z) * (e.pos.y - z) < e.radius * e.radius) return true;
            return false;
        }

        private bool Reserve(string cls, float x, float z, float min)
        {
            if (min <= 0f) return true;
            if (!spacing.TryGetValue(cls, out var grid)) spacing[cls] = grid = new Dictionary<long, List<Vector2>>();
            const float cell = 10f;
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            int r = Mathf.CeilToInt(min / cell);
            for (int i = -r; i <= r; i++)
            for (int j = -r; j <= r; j++)
            {
                long key = ((long)(cx + i) << 32) ^ (uint)(cz + j);
                if (!grid.TryGetValue(key, out var list)) continue;
                foreach (var q in list)
                    if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < min * min) return false;
            }
            long own = ((long)cx << 32) ^ (uint)cz;
            if (!grid.TryGetValue(own, out var l)) grid[own] = l = new List<Vector2>();
            l.Add(new Vector2(x, z));
            return true;
        }

        private bool TryPlace(LevelScatterRule r, string prefab, float x, float z, float scale)
        {
            if (Excluded(x, z)) return false;
            float search = Mathf.Min(r.To + 14f, 110f);
            int k = field.Nearest(x, z, search, out float dist);
            float e = k >= 0 ? dist - field.HalfEdge[k] : float.MaxValue;
            if (r.To <= 100f && (e < r.From || e > r.To)) return false;
            if (r.To > 100f && e < r.From) return false;
            if (r.Tall && k >= 0)
            {
                if (tallBlocked(k) && e < 9f) return false;
                float curv = field.Path.GetCurvature(k * LevelTrackField.Step);
                if (Mathf.Abs(curv) > 0.03f)
                {
                    float side = (x - field.Pos[k].x) * field.Right[k].x + (z - field.Pos[k].z) * field.Right[k].z;
                    bool inside = side * curv > 0f;
                    if (inside && e < 7f) return false;
                }
            }
            if (!terrain.Sample(x, z, out float y, out Vector3 n)) return false;
            if (1f - n.y > r.MaxSlope) return false;
            float rel = y - minRoadY;
            if (rel < r.MinHeight || rel > r.MaxHeight) return false;
            if (!Reserve(r.SpacingClass, x, z, r.Spacing)) return false;

            var g = GroupFor(prefab, r.Shadows, r.DrawDistance);
            g.Instances.Add(new LevelPropData.Instance
            {
                Position = new Vector3(x, y - r.Sink, z),
                Rotation = Quaternion.Euler(R(-r.Tilt, r.Tilt), R(0f, 360f), R(-r.Tilt, r.Tilt)),
                Scale = scale
            });
            return true;
        }

        public void Apply(LevelScatterRule r)
        {
            bool nearBand = r.To <= 100f;
            if (nearBand)
            {
                // Spots along the track on both sides: count follows band area.
                float perSample = r.Density * (r.To - r.From) * LevelTrackField.Step / Mathf.Max(1, r.Cluster);
                float carry = 0f;
                for (int k = 0; k < field.Count; k++)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        carry += perSample;
                        while (carry >= 1f)
                        {
                            carry -= 1f;
                            float off = field.HalfEdge[k] + R(r.From, r.To);
                            Vector3 q = field.Pos[k] + field.Right[k] * (side * off);
                            Spot(r, q.x + R(-1.5f, 1.5f), q.z + R(-1.5f, 1.5f));
                        }
                    }
                }
            }
            else
            {
                float margin = r.To;
                float area = (bounds.size.x + margin * 2f) * (bounds.size.z + margin * 2f);
                int count = Mathf.RoundToInt(area * r.Density / Mathf.Max(1, r.Cluster));
                for (int i = 0; i < count; i++)
                    Spot(r, R(bounds.min.x - margin, bounds.max.x + margin), R(bounds.min.z - margin, bounds.max.z + margin));
            }
        }

        private void Spot(LevelScatterRule r, float x, float z)
        {
            string prefab = r.Prefabs[rng.Next(r.Prefabs.Length)];
            float scale = R(r.ScaleMin, r.ScaleMax);
            if (r.Cluster <= 1)
            {
                TryPlace(r, prefab, x, z, scale);
                return;
            }
            for (int c = 0; c < r.Cluster; c++)
            {
                float a = R(0f, Mathf.PI * 2f), d = R(0f, r.ClusterRadius);
                TryPlace(r, prefab, x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, scale * R(0.9f, 1.1f));
            }
        }

        public void Apply(LevelRowRule r)
        {
            float span = field.Path.Wrap(r.ToDistance - r.FromDistance);
            for (float s = 0f; s <= span; s += r.Step)
            {
                float d = field.Path.Wrap(r.FromDistance + s);
                Vector3 right = field.Path.GetRight(d);
                Vector3 flat = new Vector3(right.x, 0f, right.z).normalized;
                float he = field.Path.GetWidth(d) * 0.5f + field.CurbWidth;
                Vector3 c = field.Path.GetPosition(d);
                Vector3 q = c + flat * (r.Side * (he + r.Offset + R(-r.OffsetJitter, r.OffsetJitter)));
                if (r.ClearBehind > 0f)
                {
                    // A wall's rear is plain: keep it out of sight of other track sections.
                    Vector3 behind = q + flat * (r.Side * 10f);
                    if (field.EdgeDistance(q.x, q.z, 60f) < r.Offset * 0.7f || field.EdgeDistance(behind.x, behind.z, r.ClearBehind + 10f) < r.ClearBehind) continue;
                }
                float y;
                if (r.UseRoadHeight) y = c.y;
                else if (!terrain.Sample(q.x, q.z, out y, out _)) continue;
                string prefab = r.Prefabs[rng.Next(r.Prefabs.Length)];
                var g = GroupFor(prefab, true, r.DrawDistance);
                g.Instances.Add(new LevelPropData.Instance
                {
                    Position = new Vector3(q.x, y - r.Sink, q.z),
                    Rotation = Quaternion.LookRotation(-flat * r.Side, Vector3.up) * Quaternion.Euler(0f, R(-r.YawJitter, r.YawJitter), 0f),
                    Scale = R(r.ScaleMin, r.ScaleMax)
                });
            }
        }

        public LevelPropData Save(string assetPath)
        {
            var data = EditorAssetUtil.LoadOrCreate<LevelPropData>(assetPath);
            data.Groups = new List<LevelPropData.Group>(groups.Values);
            data.Groups.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            EditorUtility.SetDirty(data);
            return data;
        }
    }
}

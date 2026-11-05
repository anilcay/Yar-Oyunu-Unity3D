using System.Collections.Generic;
using System.Linq;
using LugeRush.Track;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Editor
{
    /// <summary>Everything the per-biome authoring shares: resolved marks, placement helpers, exclusion lists.</summary>
    public class LevelContext
    {
        public LevelLayout Layout;
        public TrackPath Path;
        public LevelTrackField Field;
        public LevelTerrain Terrain;
        public LevelTerrainParams TerrainParams;
        public Transform Root;
        public readonly List<(Vector2 pos, float radius)> Exclusions = new List<(Vector2, float)>();
        public readonly List<(float from, float to)> Covered = new List<(float, float)>();   // tunnels, gorge: no rails, no tall props
        public readonly List<(float from, float to)> RailZones = new List<(float, float)>(); // rails on both edges
        public float CurbWidth;

        public float D(string mark) => Path.GetNearestDistance(Layout.Marks[mark]);
        public float Edge(float d) => Path.GetWidth(d) * 0.5f + CurbWidth;

        public Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }
        public bool InRange(float d, (float from, float to) r) => Path.Wrap(d - r.from) <= Path.Wrap(r.to - r.from);
        public bool IsCovered(float d) => Covered.Any(r => InRange(d, r));
        public bool InRailZone(float d) => RailZones.Any(r => InRange(d, r));

        /// <summary>Point at track distance d with a horizontal lateral offset (right positive), height of the road centre.</summary>
        public Vector3 Beside(float d, float lateral) => Path.GetPosition(d) + Flat(Path.GetRight(d)) * lateral;

        public void Exclude(Vector3 p, float radius) => Exclusions.Add((new Vector2(p.x, p.z), radius));

        public void Stamp(Vector3 p, float radius, float falloff) =>
            TerrainParams.Stamps.Add(new LevelStamp { Center = p, Radius = radius, Falloff = falloff });

        public void Zone(float from, float to, float left, float right, float range, float blend, float fade) =>
            TerrainParams.Zones.Add(new LevelZone { From = from, To = to, Left = left, Right = right, Range = range, Blend = blend, Fade = fade });

        public float GroundY(Vector3 p, float fallback) => Terrain.Sample(p.x, p.z, out float y, out _) ? y : fallback;

        public GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            if (prefab == null) { Debug.LogError("[LevelSetPieces] Missing prefab."); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Root);
            go.transform.SetPositionAndRotation(pos, rot);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
            return go;
        }

        public GameObject Env(string relative, Vector3 pos, Quaternion rot, float scale = 1f) => Spawn(LevelScatter.LoadPrefab(relative), pos, rot, scale);
        public GameObject Kit(string model, Vector3 pos, Quaternion rot) => Spawn(PrefabFactory.LoadTrack(model), pos, rot);

        /// <summary>Same module laid along the track between two distances (pivot at the start end, +Z along the road).</summary>
        public void Lay(string model, float from, float to, float length)
        {
            for (float d = from; d + length <= to + 0.01f; d += length)
            {
                Vector3 a = Path.GetPosition(d), b = Path.GetPosition(d + length);
                Kit(model, a, Quaternion.LookRotation(b - a, Path.GetUp(d)));
            }
        }

        public void Sign(string model, float d, int side = 1, float extra = 1.1f)
        {
            Vector3 p = Beside(d, side * (Edge(d) + extra));
            p.y = GroundY(p, p.y);
            Kit(model, p, Quaternion.LookRotation(Flat(Path.GetTangent(d)), Vector3.up));
        }
    }

    /// <summary>
    /// Hand-positioned landmarks of every map. Plan* runs before the terrain exists (terrain zones, flat pads,
    /// exclusions); Place* runs after it and spawns the objects on the final surface.
    /// </summary>
    public static class LevelSetPieces
    {
        private static Quaternion FaceRoad(LevelContext c, float d, int side) =>
            Quaternion.LookRotation(c.Flat(c.Path.GetRight(d)) * -side, Vector3.up);

        // ---------- Common ----------

        public static void PlanCommon(LevelContext c)
        {
            var path = c.Path;
            float d0 = path.StartLineDistance;
            // Start/finish compound stays clear; lift corridor too.
            c.RailZones.Add((path.LiftEnd + 2f, d0 + 22f));
            foreach (float s in new[] { d0 - 34f, d0 - 20f, d0 - 8f, d0 + 8f, d0 + 20f })
            {
                c.Exclude(path.GetPosition(s), 12f);
                c.Stamp(c.Beside(s, -7f), 4f, 6f);
                c.Stamp(c.Beside(s, 7f), 4f, 6f);
            }
            for (float s = 0f; s <= path.LiftLength; s += 6f) c.Exclude(path.GetPosition(path.LiftStart + s), 9f);
            c.Stamp(c.Beside(path.LiftStart + 3f, -6f), 4f, 6f);
            c.Stamp(c.Beside(path.LiftStart + 3f, 6f), 4f, 6f);
            c.Stamp(c.Beside(path.LiftEnd - 3f, -6f), 4f, 6f);
            c.Stamp(c.Beside(path.LiftEnd - 3f, 6f), 4f, 6f);
        }

        public static void PlaceCommon(LevelContext c)
        {
            var path = c.Path;
            float d0 = path.StartLineDistance;

            // Numbered posts spread over the lap; distance posts ahead of the first hard corner.
            float lap = path.Length - path.LiftLength;
            for (int i = 1; i <= 5; i++)
            {
                float d = path.LiftEnd + lap * i / 6f;
                if (c.IsCovered(d) || path.IsInLift(d)) d += 30f;
                c.Sign($"SM_Track_MarkerPost_0{i}", d, 1, 0.9f);
            }
            for (float d = path.StartLineDistance + 40f; d < path.LiftStart - 40f; d += 2f)
            {
                if (Mathf.Abs(path.GetCurvature(d + 100f)) <= 0.06f) continue;
                float prev = 0f;
                for (float o = 0f; o < 100f; o += 4f) prev = Mathf.Max(prev, Mathf.Abs(path.GetCurvature(d + o)));
                if (prev > 0.04f) continue;
                c.Sign("SM_Track_MarkerPost_300m", d - 200f, 1, 0.9f);
                c.Sign("SM_Track_MarkerPost_200m", d - 100f, 1, 0.9f);
                c.Sign("SM_Track_MarkerPost_100m", d, 1, 0.9f);
                break;
            }

            // Start compound furniture.
            float side = c.Edge(d0) + 3.5f;
            foreach (float s in new[] { d0 - 16f, d0 + 21f })
            {
                Vector3 p = c.Beside(s, -(c.Edge(s) + 1.4f));
                p.y = c.GroundY(p, p.y);
                c.Kit("SM_Track_LightPole", p, Quaternion.LookRotation(c.Flat(path.GetRight(s)) * -1f, Vector3.up) * Quaternion.Euler(0f, 90f, 0f));
            }
            Vector3 bin = c.Beside(d0 + 20f, side);
            bin.y = c.GroundY(bin, bin.y);
            c.Kit("SM_Track_TrashBin", bin, Quaternion.identity);

            // Parked sleds: one stack per map, box collider instead of the mesh collider (2.5k tris).
            Vector3 stackPos = c.Beside(d0 - 12f, side + 1.5f);
            stackPos.y = c.GroundY(stackPos, stackPos.y);
            var stack = c.Kit("SM_Track_SledStack", stackPos, FaceRoad(c, d0 - 12f, 1));
            if (stack != null) UseBoxCollider(stack);

            AddStationColliders(c);
        }

        private static void UseBoxCollider(GameObject go)
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Bounds? bounds = null;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = new Bounds(go.transform.InverseTransformPoint(mf.transform.TransformPoint(mf.sharedMesh.bounds.center)), mf.sharedMesh.bounds.size);
                if (bounds.HasValue) { var u = bounds.Value; u.Encapsulate(b); bounds = u; } else bounds = b;
            }
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            if (!bounds.HasValue) return;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.Value.center;
            box.size = bounds.Value.size;
        }

        /// <summary>Simple blocks left and right of the sled corridor (|x| below 0.6 m stays free up to 1.1 m and more).</summary>
        private static void AddStationColliders(LevelContext c)
        {
            var lift = c.Path.transform.Find("LiftVisuals");
            if (lift == null) return;
            foreach (Transform t in lift)
            {
                if (!t.name.Contains("Lift_Station")) continue;
                var mfs = t.GetComponentsInChildren<MeshFilter>();
                if (mfs.Length == 0) continue;
                Bounds b = new Bounds(t.InverseTransformPoint(mfs[0].transform.TransformPoint(mfs[0].sharedMesh.bounds.center)), Vector3.zero);
                foreach (var mf in mfs)
                {
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                        b.Encapsulate(t.InverseTransformPoint(mf.transform.TransformPoint(corner)));
                    }
                }
                const float inner = 2.45f; // outside the road + curb of the lift lane
                foreach (int s in new[] { -1, 1 })
                {
                    float outer = s > 0 ? b.max.x : -b.min.x;
                    if (outer < inner + 0.2f) continue;
                    var box = t.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(outer - inner, b.size.y, b.size.z);
                    box.center = new Vector3(s * (inner + outer) * 0.5f, b.center.y, b.center.z);
                }
                Debug.Log($"[LevelSetPieces] {t.name} local bounds {b}");
            }
        }

        // ---------- Forest ----------

        public static void PlanForest(LevelContext c)
        {
            float bridge = c.D("bridge"), tA = c.D("tunnel_a"), tB = c.D("tunnel_b"), view = c.D("view");
            c.Covered.Add((tA - 3f, tB + 3f));
            c.Zone(bridge - 70f, bridge + 70f, 7f, 7f, 16f, 5f, 20f);
            c.Stamp(c.Beside(bridge, -9.5f), 5f, 7f);
            c.Stamp(c.Beside(bridge, 9.5f), 5f, 7f);
            c.Exclude(c.Beside(bridge, -9.5f), 7f);
            c.Exclude(c.Beside(bridge, 9.5f), 7f);
            c.Exclude(c.Path.GetPosition(bridge), 7f);
            Vector3 v = c.Beside(view, ViewSide(c, view) * (c.Edge(view) + 7f));
            c.Stamp(v, 5f, 7f);
            c.Exclude(v, 7f);
        }

        private static int ViewSide(LevelContext c, float d) => c.Path.GetCurvature(d - 12f) > 0f ? -1 : 1;

        public static void PlaceForest(LevelContext c)
        {
            float bridge = c.D("bridge"), tA = c.D("tunnel_a"), tB = c.D("tunnel_b"), view = c.D("view");

            // Footbridge: span (model X) across the road; towers at +-5.2 m checked against the road.
            float clearance = 5.2f - c.Edge(bridge);
            if (clearance < 0.5f) Debug.LogError($"[LevelSetPieces] Footbridge towers only {clearance:0.00} m from the curb.");
            Vector3 bp = c.Path.GetPosition(bridge);
            c.Env("Forest/Env_Forest_Footbridge_01", bp, Quaternion.LookRotation(c.Flat(c.Path.GetTangent(bridge)), Vector3.up));
            foreach (int s in new[] { -1, 1 })
                c.Env("Forest/Env_Forest_VineCurtain_0" + (s < 0 ? 1 : 2), c.Beside(bridge + 5f, s * (c.Edge(bridge) + 2.2f)), FaceRoad(c, bridge, s));
            c.Sign("SM_Track_Sign_Warning", bridge - 45f);

            c.Lay("SM_Track_Tunnel_4m", tA, tB, 4f);
            c.Sign("SM_Track_Sign_Warning", tA - 28f);

            int vs = ViewSide(c, view);
            Vector3 vp = c.Beside(view, vs * (c.Edge(view) + 7f));
            vp.y = c.GroundY(vp, vp.y);
            c.Env("Forest/Env_Forest_ViewPlatform_01", vp, FaceRoad(c, view, vs));
            c.Sign("SM_Track_Sign_Warning", c.D("hairpin1_in") - 45f, -1);
        }

        // ---------- Alpine ----------

        public static void PlanAlpine(LevelContext c)
        {
            float pano = c.D("panorama"), wide = c.D("wide_out"), fall = c.D("waterfall");
            float tA = c.D("tunnel_a"), tB = c.D("tunnel_b"), meadow = c.D("meadow");
            c.Covered.Add((tA - 3f, tB + 3f));

            // Cliff edge: mountain wall on the inside of the panoramic curve, deep valley on the outside (right).
            c.Zone(pano - 120f, pano + 50f, 14f, -46f, 42f, 5f, 28f);
            c.RailZones.Add((pano - 110f, pano + 40f));
            // Wall + waterfall on the left of the long straight, drop to the right.
            c.Zone(c.D("long_in") - 10f, c.D("long_out") + 10f, 22f, -18f, 26f, 6f, 24f);
            // Tunnel hill.
            c.Zone(tA - 16f, tB + 16f, 20f, 20f, 13f, 2.5f, 14f);
            // Outer rim of the last banked curve falls off too.
            c.Zone(wide - 190f, wide + 10f, 4f, -22f, 40f, 6f, 24f);
            c.RailZones.Add((wide - 170f, wide));
            // Meadow + chalet
            c.Zone(meadow - 90f, meadow + 40f, 0f, 2f, 30f, 12f, 20f);

            Vector3 pool = c.Beside(fall, -(c.Edge(fall) + 10f));
            c.Stamp(pool, 6f, 6f);
            c.Exclude(pool, 13f);
            // Stream bed: flat shelf between the pool and the rocks that swallow the stream.
            for (int i = 0; i <= 5; i++)
            {
                Vector3 s = c.Beside(fall + 4f + 9f * i, -(c.Edge(fall) + 5.5f));
                c.Stamp(s, 3.2f, 3.5f);
                c.Exclude(s, 5f);
            }
            Vector3 chalet = ChaletPos(c);
            c.Stamp(chalet, 7f, 7f);
            c.Exclude(chalet, 11f);
        }

        private static Vector3 ChaletPos(LevelContext c)
        {
            float d = c.D("panorama") - 20f;
            return c.Beside(d, c.Edge(d) + 12f) + Vector3.up * -0.3f;
        }

        public static void PlaceAlpine(LevelContext c)
        {
            float fall = c.D("waterfall"), tA = c.D("tunnel_a"), tB = c.D("tunnel_b");

            // Rock tunnel: mouths + segments between them. The modules extend along local -Z from their pivot.
            Quaternion In(float d) => Quaternion.LookRotation(-c.Path.GetTangent(d), c.Path.GetUp(d));
            Quaternion Out(float d) => Quaternion.LookRotation(c.Path.GetTangent(d), c.Path.GetUp(d));
            c.Env("Alpine/Env_Alpine_TunnelMouth_01", c.Path.GetPosition(tA), In(tA));
            c.Env("Alpine/Env_Alpine_TunnelMouth_01", c.Path.GetPosition(tB), Out(tB));
            for (float d = tA + 8f; d + 8f <= tB - 8f + 0.01f; d += 8f)
                c.Env("Alpine/Env_Alpine_TunnelSegment_01", c.Path.GetPosition(d), In(d));
            c.Sign("SM_Track_Sign_Warning", tA - 32f);

            // Waterfall in front of a cliff module; stream runs from its pool along the road and sinks into the rocks.
            Vector3 pool = c.Beside(fall, -(c.Edge(fall) + 10f));
            pool.y = c.GroundY(pool, pool.y);
            var toRoad = FaceRoad(c, fall, -1);
            c.Env("Alpine/Env_Alpine_Cliff_02", pool + toRoad * Vector3.back * 3.2f + Vector3.up * -0.4f, toRoad, 1.15f);
            c.Env("Alpine/Env_Alpine_Waterfall_01", pool, toRoad);
            float yaw = Quaternion.LookRotation(c.Flat(c.Path.GetTangent(fall))).eulerAngles.y;
            for (int i = 0; i < 4; i++)
            {
                Vector3 s = c.Beside(fall + 4f + 10f * i, -(c.Edge(fall) + 5.5f));
                s.y = c.GroundY(s, s.y) + 0.15f;
                // Stream modules run along model -Z from the pivot.
                c.Env("Alpine/Env_Alpine_Stream_01", s, Quaternion.Euler(0f, yaw + 180f, 0f));
            }
            c.Env("Common/Env_Common_RockLarge_02", c.Beside(fall + 44f, -(c.Edge(fall) + 5.5f)) + Vector3.up * -0.2f, Quaternion.Euler(0f, 40f, 0f));
            c.Sign("SM_Track_Sign_Warning", c.D("hairpin_in") - 30f);

            Vector3 ch = ChaletPos(c);
            ch.y = c.GroundY(ch, ch.y);
            float pano = c.D("panorama") - 20f;
            c.Env("Alpine/Env_Alpine_Chalet_01", ch, FaceRoad(c, pano, 1));
            c.Env("Common/Env_Common_Fence_01", ch + Vector3.right * 6f, Quaternion.Euler(0f, 90f, 0f));
        }

        // ---------- Canyon ----------

        public static void PlanCanyon(LevelContext c)
        {
            float gA = c.D("gorge_a"), gB = c.D("gorge_b"), arch = c.D("arch"), sA = c.D("shed_a"), sB = c.D("shed_b"), mine = c.D("mine");
            c.Covered.Add((gA - 2f, gB + 2f));
            c.Covered.Add((sA - 3f, sB + 3f));
            c.Zone(gA - 14f, gB + 14f, 26f, 26f, 9f, 2.5f, 12f);
            c.Zone(arch - 50f, arch + 50f, 4f, 4f, 24f, 6f, 20f);
            foreach (int s in new[] { -1, 1 })
            {
                c.Stamp(c.Beside(arch, s * 11f), 8f, 7f);
                c.Exclude(c.Beside(arch, s * 9f), 10f);
            }
            c.Exclude(c.Path.GetPosition(arch), 10f);
            c.Zone(mine - 80f, mine + 30f, 3f, 6f, 26f, 8f, 20f);
            Vector3 rail = c.Beside(mine, -(c.Edge(mine) + 6f));
            c.Stamp(rail, 7f, 6f);
            for (int i = -6; i <= 6; i++)
            {
                c.Stamp(c.Beside(mine + i * 6f, -(c.Edge(mine) + 6f)), 2.5f, 3f);
                c.Exclude(c.Beside(mine + i * 6f, -(c.Edge(mine) + 6f)), 3.5f);
            }
            Vector3 tower = c.Beside(mine + 52f, -(c.Edge(mine) + 11f));
            c.Stamp(tower, 5f, 5f);
            c.Exclude(tower, 8f);
        }

        public static void PlaceCanyon(LevelContext c)
        {
            float arch = c.D("arch"), sA = c.D("shed_a"), sB = c.D("shed_b"), mine = c.D("mine");
            c.Env("Canyon/Env_Canyon_Arch_01", c.Path.GetPosition(arch) + Vector3.up * -0.2f, Quaternion.LookRotation(c.Flat(c.Path.GetTangent(arch)), Vector3.up));
            c.Sign("SM_Track_Sign_Warning", arch - 60f);

            c.Lay("SM_Track_Tunnel_4m", sA, sB, 4f);
            c.Sign("SM_Track_Sign_Warning", c.D("hairpin_in") - 30f);

            float yaw = Quaternion.LookRotation(c.Flat(c.Path.GetTangent(mine))).eulerAngles.y;
            for (int i = -6; i <= 6; i++)
            {
                Vector3 r = c.Beside(mine + i * 6f, -(c.Edge(mine) + 6f));
                r.y = c.GroundY(r, r.y) + 0.02f;
                c.Env(i % 3 == 2 ? "Canyon/Env_Canyon_MineRail_02" : "Canyon/Env_Canyon_MineRail_01", r, Quaternion.Euler(0f, yaw, 0f));
            }
            Vector3 cart = c.Beside(mine + 8f, -(c.Edge(mine) + 6f));
            cart.y = c.GroundY(cart, cart.y) + 0.15f;
            c.Env("Canyon/Env_Canyon_MineCart_01", cart, Quaternion.Euler(0f, yaw, 0f));
            Vector3 tower = c.Beside(mine + 52f, -(c.Edge(mine) + 11f));
            tower.y = c.GroundY(tower, tower.y);
            c.Env("Canyon/Env_Canyon_WaterTower_01", tower, Quaternion.Euler(0f, yaw + 30f, 0f));
        }

        // ---------- Far background cards ----------

        public static void PlaceCards(LevelContext c, string[] prefabs, int count, float radius, float baseOffset, float scaleMin, float scaleMax, int seed)
        {
            var rng = new System.Random(seed);
            Bounds b = new Bounds(c.Field.Pos[0], Vector3.zero);
            for (int k = 0; k < c.Field.Count; k++) b.Encapsulate(c.Field.Pos[k]);
            Vector3 center = new Vector3(b.center.x, 0f, b.center.z);
            var root = new GameObject("BackgroundCards").transform;
            root.SetParent(c.Root, false);
            for (int i = 0; i < count; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
                float r = radius * (0.9f + (float)rng.NextDouble() * 0.25f);
                Vector3 pos = center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                pos.y = baseOffset;
                string prefab = prefabs[rng.Next(prefabs.Length)];
                var go = c.Env(prefab, pos, Quaternion.LookRotation(center - new Vector3(pos.x, center.y, pos.z), Vector3.up));
                if (go == null) continue;
                go.transform.SetParent(root, true);
                go.transform.localScale = Vector3.one * (scaleMin + (float)rng.NextDouble() * (scaleMax - scaleMin));
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}

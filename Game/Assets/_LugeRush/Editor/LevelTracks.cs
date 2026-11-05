using System.Collections.Generic;
using LugeRush.Track;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    public enum LevelBiome { Forest, Alpine, Canyon }

    /// <summary>A track definition plus the named spots the set pieces hang on.</summary>
    public class LevelLayout
    {
        public LevelBiome Biome;
        public TrackDefinition Definition;
        public int Seed;
        public string SceneName;
        /// <summary>Named 3D knot positions on the track (resolved to distances after the spline exists).</summary>
        public readonly Dictionary<string, Vector3> Marks = new Dictionary<string, Vector3>();
    }

    /// <summary>
    /// The three race tracks, authored as turtle paths (straights + arcs with grade, bank and width per segment).
    /// Lift bottom sits straight south of the lift top, so each lap closes with a straight climb onto the start zone.
    /// Segment lengths were solved so that the descent ends exactly at the lift bottom.
    /// </summary>
    public static class LevelTracks
    {
        public const string Folder = EditorAssetUtil.Root + "/ScriptableObjects/Tracks";
        private const float BottomHeight = 12f;
        private const float StationLength = 8f;

        /// <summary>Lift exit -> lap line. Holds the last grid row, the start gate and the finish arch.</summary>
        public const float StartLineOffset = 54f;
        /// <summary>First grid row behind the lap line (room for the gate between the grid and the arch).</summary>
        public const float GridFirstRowOffset = 14f;

        private class Writer
        {
            public readonly List<TrackDefinition.Knot> Knots = new List<TrackDefinition.Knot>();
            public readonly List<float> Grades = new List<float>();
            public readonly Dictionary<string, int> MarkKnots = new Dictionary<string, int>();
            private Vector2 pos;
            private float heading;
            public float Length;

            public Writer() { AddKnot(0f, 0f, 0f); }
            public Vector2 Position => pos;
            public float Heading => heading;

            private static Vector2 Dir(float deg) => new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));

            private void AddKnot(float grade, float bank, float width)
            {
                Knots.Add(new TrackDefinition.Knot(new Vector3(pos.x, 0f, pos.y), bank, width));
                Grades.Add(grade);
            }

            public Writer Mark(string name) { MarkKnots[name] = Knots.Count - 1; return this; }

            public Writer S(float length, float grade, float width = 0f, float spacing = 16f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
                Vector2 dir = Dir(heading);
                for (int i = 0; i < n; i++)
                {
                    pos += dir * (length / n);
                    Length += length / n;
                    AddKnot(grade, 0f, width);
                }
                return this;
            }

            /// <summary>angle > 0 = right turn. The bank ramps in and out over the first and last knot.</summary>
            public Writer A(float radius, float angle, float grade, float bank = 0f, float width = 0f, float maxStep = 12f)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angle) / maxStep));
                float sign = Mathf.Sign(angle);
                Vector2 center = pos + Dir(heading + 90f) * (radius * sign);
                float stepDeg = angle / n;
                float segLen = radius * Mathf.Abs(stepDeg) * Mathf.Deg2Rad;
                for (int i = 1; i <= n; i++)
                {
                    heading += stepDeg;
                    pos = center - Dir(heading + 90f) * (radius * sign);
                    Length += segLen;
                    float ramp = i == 1 || i == n ? 0.5f : 1f;
                    AddKnot(grade, bank * sign * ramp, width);
                }
                return this;
            }
        }

        private static LevelLayout Finish(Writer w, LevelBiome biome, string id, string display, int seed, float width)
        {
            Vector2 bottom = w.Position;
            var knots = w.Knots;
            int liftStart = knots.Count - 1;

            // Descent heights from the per segment grades.
            float drop = 0f;
            var heights = new float[knots.Count];
            for (int i = 1; i < knots.Count; i++)
            {
                drop += Vector3.Distance(knots[i].Position, knots[i - 1].Position) * w.Grades[i];
                heights[i] = -drop;
            }
            float top = BottomHeight + drop;
            for (int i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                k.Position.y = top + heights[i];
                knots[i] = k;
            }

            // Lift: flat bottom station, eased climb, flat top station; closes onto knot 0.
            Vector2 topXZ = Vector2.zero;
            Vector2 dir = (topXZ - bottom).normalized;
            float run = Vector2.Distance(bottom, topXZ);
            const float liftWidth = 4f;
            knots[liftStart] = new TrackDefinition.Knot(knots[liftStart].Position, 0f, liftWidth);
            knots.Add(new TrackDefinition.Knot(new Vector3(bottom.x + dir.x * StationLength, BottomHeight, bottom.y + dir.y * StationLength), 0f, liftWidth));
            const int climb = 9;
            float climbRun = run - 2f * StationLength;
            for (int i = 1; i <= climb; i++)
            {
                float u = i / (float)(climb + 1);
                Vector2 p = bottom + dir * (StationLength + climbRun * u);
                knots.Add(new TrackDefinition.Knot(new Vector3(p.x, BottomHeight + drop * EasedClimb(u), p.y), 0f, liftWidth));
            }
            knots.Add(new TrackDefinition.Knot(new Vector3(-dir.x * StationLength, top, -dir.y * StationLength), 0f, liftWidth));

            var def = EditorAssetUtil.LoadOrCreate<TrackDefinition>($"{Folder}/Track_{biome}.asset");
            def.TrackId = id;
            def.DisplayName = display;
            def.Knots = knots;
            def.DefaultWidth = width;
            def.LiftStartKnot = liftStart;
            def.LiftEndKnot = 0;
            def.LiftSpeed = 18f;
            def.LiftExitSpeed = 9f;
            def.StartLineOffset = StartLineOffset;
            def.CheckpointSpacing = 50f;
            def.BuildSkirt = false;
            EditorUtility.SetDirty(def);

            var layout = new LevelLayout { Biome = biome, Definition = def, Seed = seed, SceneName = $"Track_{biome}" };
            foreach (var m in w.MarkKnots) layout.Marks[m.Key] = knots[m.Value].Position;
            Debug.Log($"[LevelTracks] {biome}: descent {w.Length:0} m, drop {drop:0.0} m ({drop / w.Length * 100f:0.0}%), lift run {run:0} m, knots {knots.Count}.");
            return layout;
        }

        private static float EasedClimb(float u)
        {
            const float e = 0.25f;
            float I(float x)
            {
                if (x <= e) return x * x / (2f * e);
                if (x <= 1f - e) return e / 2f + (x - e);
                float r = 1f - x;
                return 1f - e - r * r / (2f * e);
            }
            return Mathf.Clamp01(I(u) / (1f - e));
        }

        /// <summary>Yeşil Vadi: narrow and winding, three hairpins, switchbacks at the bottom of the valley.</summary>
        public static LevelLayout Forest()
        {
            var w = new Writer();
            w.S(92f, 0.06f, width: 4.2f, spacing: 12f);
            w.A(42f, 25f, 0.055f, bank: 3f).Mark("s_in");
            w.A(30f, -50f, 0.06f, bank: 5f);
            w.A(30f, 60f, 0.06f, bank: 5f);
            w.A(30f, 55f, 0.055f, bank: 5f).Mark("straight_in");
            w.S(89f, 0.08f).Mark("bridge");                        // long straight, footbridge in the middle
            w.S(89f, 0.08f).Mark("hairpin1_in");
            w.A(15f, 180f, 0.055f, bank: 6f, width: 4.4f, maxStep: 15f).Mark("hairpin1_out");
            w.S(33f, 0.066f);
            w.A(38f, -150f, 0.058f, bank: 10f, width: 4.2f).Mark("banked_out");
            w.S(4f, 0.055f).Mark("tunnel_a");
            w.S(36f, 0.055f, width: 3.8f, spacing: 9f).Mark("tunnel_b"); // covered section
            w.S(6f, 0.055f);
            w.A(28f, 65f, 0.06f, bank: 6f);
            w.A(28f, -45f, 0.06f, bank: 5f);
            w.A(28f, 60f, 0.06f, bank: 5f).Mark("view");
            w.S(169.6f, 0.066f);
            w.A(24f, 70f, 0.05f, bank: 5f);
            w.S(16f, 0.055f);
            w.A(22f, 180f, 0.055f, bank: 6f, width: 4.4f, maxStep: 15f);
            w.S(16f, 0.055f);
            w.A(22f, -180f, 0.055f, bank: 6f, width: 4.4f, maxStep: 15f);
            w.S(117.6f, 0.056f);
            w.A(24f, 90f, 0.03f, bank: 3f);
            return Finish(w, LevelBiome.Forest, "forest", "Yeşil Vadi", 11, 4.2f);
        }

        /// <summary>Çam Zirvesi: wide and fast, big banked curves along a cliff edge, rock tunnel on the way back.</summary>
        public static LevelLayout Alpine()
        {
            var w = new Writer();
            w.S(92f, 0.058f, width: 4.2f, spacing: 12f);
            w.A(62f, -90f, 0.06f, bank: 12f, width: 5f).Mark("panorama");
            w.S(24f, 0.06f);
            w.A(34f, 35f, 0.06f, bank: 6f);
            w.A(34f, -35f, 0.06f, bank: 6f).Mark("long_in");
            w.S(65f, 0.072f).Mark("waterfall");
            w.S(65f, 0.072f).Mark("long_out");
            w.A(50f, -90f, 0.055f, bank: 12f);
            w.S(60f, 0.068f).Mark("hairpin_in");
            w.A(16f, -180f, 0.05f, bank: 7f, width: 5.2f, maxStep: 15f);
            w.S(20f, 0.055f);
            w.A(46f, 90f, 0.05f, bank: 8f);
            w.S(49f, 0.055f).Mark("meadow");
            w.A(46f, 90f, 0.05f, bank: 8f);
            w.S(60f, 0.055f).Mark("tunnel_a");
            w.S(64f, 0.055f, spacing: 8f).Mark("tunnel_b");          // rock tunnel
            w.S(92.3f, 0.055f);
            w.A(66f, -180f, 0.06f, bank: 13f).Mark("wide_out");
            w.S(36f, 0.04f);
            return Finish(w, LevelBiome.Alpine, "alpine", "Çam Zirvesi", 22, 5f);
        }

        /// <summary>Kızıl Kanyon: long straights, a narrow gorge, a rock arch and a tight hairpin.</summary>
        public static LevelLayout Canyon()
        {
            var w = new Writer();
            w.S(92f, 0.058f, width: 4.2f, spacing: 12f);
            w.S(15f, 0.07f, width: 4.6f).Mark("gorge_a");
            w.S(120f, 0.07f, width: 3.6f, spacing: 12f).Mark("gorge_b"); // narrow gorge on the first long straight
            w.S(35f, 0.07f, width: 4.6f);
            w.A(26f, 90f, 0.05f, bank: 5f);
            w.S(50f, 0.055f);
            w.A(32f, 45f, 0.055f, bank: 6f);
            w.A(32f, -45f, 0.055f, bank: 6f);
            w.S(20f, 0.055f);
            w.A(58f, 90f, 0.055f, bank: 11f).Mark("long2_in");
            w.S(78f, 0.07f).Mark("arch");
            w.S(77f, 0.07f).Mark("hairpin_in");
            w.A(14f, 180f, 0.045f, bank: 6f, width: 4.8f, maxStep: 15f);
            w.S(10f, 0.05f).Mark("shed_a");
            w.S(36f, 0.05f, width: 3.8f, spacing: 9f).Mark("shed_b");   // mine shed (covered section)
            w.S(9f, 0.05f);
            w.A(28f, -90f, 0.05f, bank: 6f);
            w.S(21.3f, 0.055f);
            w.A(28f, -90f, 0.05f, bank: 6f);
            w.S(100f, 0.055f).Mark("mine");
            w.S(123.3f, 0.055f);
            w.A(28f, 90f, 0.05f, bank: 5f);
            w.S(40f, 0.05f);
            w.A(26f, 90f, 0.03f, bank: 3f);
            w.S(20f, 0.03f);
            return Finish(w, LevelBiome.Canyon, "canyon", "Kızıl Kanyon", 33, 4.6f);
        }
    }
}

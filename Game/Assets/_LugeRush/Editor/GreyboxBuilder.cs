using System.Collections.Generic;
using LugeRush.Track;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Generates the Dev_Greybox track definition (≈1.15 km loop: fast start straight, wide banked 180° curve,
    /// S-curve, long straight, hairpin switchback, closing turns and a ≈150 m cable lift back to the top)
    /// and builds Scenes/Dev_Greybox.unity from it.
    /// Batch: Unity.exe -batchmode -quit -projectPath ... -executeMethod LugeRush.Editor.GreyboxBuilder.Build
    /// </summary>
    public static class GreyboxBuilder
    {
        public const string DefinitionPath = EditorAssetUtil.Root + "/ScriptableObjects/Tracks/Track_DevGreybox.asset";
        public const string ScenePath = EditorAssetUtil.Root + "/Scenes/Dev_Greybox.unity";

        private const float BottomHeight = 2f;

        [MenuItem("Luge Rush/Build Dev Greybox")]
        public static void Build()
        {
            var def = CreateDefinition();
            TrackSceneBuilder.BuildScene(def, ScenePath, new TrackSceneBuilder.Options
            {
                GroundCenter = new Vector3(50f, 0f, -60f),
                GroundSize = 800f
            });
            Debug.Log($"[GreyboxBuilder] Built {ScenePath} ({def.Knots.Count} knots).");
        }

        /// <summary>Simple turtle path generator: straights + arcs with per-segment grade and banking.</summary>
        private class PathWriter
        {
            public readonly List<TrackDefinition.Knot> Knots = new List<TrackDefinition.Knot>();
            public readonly List<float> Grades = new List<float>(); // grade of the segment ending at each knot
            private Vector2 pos;
            private float heading; // degrees, 0 = +Z, clockwise (right) positive
            public float Length;

            public PathWriter(Vector2 start, float headingDeg)
            {
                pos = start;
                heading = headingDeg;
                AddKnot(0f, 0f, 0f);
            }

            public Vector2 Position => pos;

            private static Vector2 Dir(float deg) => new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));

            private void AddKnot(float grade, float bank, float width)
            {
                Knots.Add(new TrackDefinition.Knot(new Vector3(pos.x, 0f, pos.y), bank, width));
                Grades.Add(grade);
            }

            public void Straight(float length, float grade, float width = 0f, float knotSpacing = 20f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(length / knotSpacing));
                Vector2 dir = Dir(heading);
                for (int i = 0; i < n; i++)
                {
                    pos += dir * (length / n);
                    Length += length / n;
                    AddKnot(grade, 0f, width);
                }
            }

            /// <summary>angleDeg > 0 = right turn. Bank ramps in/out over the first/last knot.</summary>
            public void Arc(float radius, float angleDeg, float grade, float bankDeg = 0f, float width = 0f, float maxStepDeg = 12f)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angleDeg) / maxStepDeg));
                float sign = Mathf.Sign(angleDeg);
                Vector2 right = Dir(heading + 90f);
                Vector2 center = pos + right * (radius * sign);
                float stepDeg = angleDeg / n;
                float segLen = radius * Mathf.Abs(stepDeg) * Mathf.Deg2Rad;
                for (int i = 1; i <= n; i++)
                {
                    heading += stepDeg;
                    // Position on the circle: centre - (right of the new heading) * r * sign
                    pos = center - Dir(heading + 90f) * (radius * sign);
                    Length += segLen;
                    float ramp = (i == 1 || i == n) ? 0.5f : 1f;
                    AddKnot(grade, bankDeg * sign * ramp, width);
                }
            }
        }

        public static TrackDefinition CreateDefinition()
        {
            // Descent (top of the hill at the lift exit, heading north).
            var w = new PathWriter(new Vector2(0f, -106f), 0f);
            w.Straight(45f, 0.01f, knotSpacing: 15f);   // flat start area: grid + start line
            w.Straight(171f, 0.09f);                    // fast start straight (slipstream)
            w.Arc(50f, 180f, 0.06f, bankDeg: 10f);      // wide banked 180°
            w.Straight(15f, 0.07f);
            w.Arc(25f, -40f, 0.07f, bankDeg: 4f);       // S-curve
            w.Arc(25f, 80f, 0.07f, bankDeg: 4f);
            w.Arc(25f, -40f, 0.07f, bankDeg: 4f);
            w.Straight(260f, 0.08f);                    // long straight
            w.Arc(12f, 180f, 0.03f, bankDeg: 6f, width: 4.6f, maxStepDeg: 15f); // hairpin (right)
            w.Straight(60f, 0.08f);
            w.Arc(12f, -180f, 0.03f, bankDeg: 6f, width: 4.6f, maxStepDeg: 15f); // hairpin (left)
            w.Straight(80f, 0.08f);
            w.Arc(20f, 90f, 0.05f, bankDeg: 4f);
            w.Straight(18f, 0.04f);
            w.Arc(14f, 90f, 0.02f, bankDeg: 3f);
            int liftStart = w.Knots.Count - 1;           // bottom station
            float descentLength = w.Length;

            // Lift: flat bottom station, climb with eased ends, flat top station; ends where the descent started.
            Vector2 bottom = w.Position;
            Vector2 top = new Vector2(0f, -106f);
            float liftRun = Vector2.Distance(bottom, top);

            // Heights for the descent from per-segment grades.
            var knots = w.Knots;
            float drop = 0f;
            var heights = new float[knots.Count];
            for (int i = 1; i < knots.Count; i++)
            {
                float seg = Vector3.Distance(knots[i].Position, knots[i - 1].Position);
                drop += seg * w.Grades[i];
                heights[i] = -drop;
            }
            float topHeight = BottomHeight + drop;
            for (int i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                k.Position.y = topHeight + heights[i];
                knots[i] = k;
            }

            // Lift knots (exclusive of bottom knot already added and of the top = knot 0).
            const float station = 8f;
            int climbKnots = 10;
            Vector2 dir = (top - bottom).normalized;
            knots.Add(new TrackDefinition.Knot(new Vector3(bottom.x + dir.x * station, BottomHeight, bottom.y + dir.y * station)));
            float climbRun = liftRun - 2f * station;
            for (int i = 1; i <= climbKnots; i++)
            {
                float u = i / (float)(climbKnots + 1);
                float h = BottomHeight + drop * EasedClimb(u);
                Vector2 p = bottom + dir * (station + climbRun * u);
                knots.Add(new TrackDefinition.Knot(new Vector3(p.x, h, p.y)));
            }
            knots.Add(new TrackDefinition.Knot(new Vector3(top.x - dir.x * station, topHeight, top.y - dir.y * station)));
            // The loop closes onto knot 0 (= top / lift end).

            var def = EditorAssetUtil.LoadOrCreate<TrackDefinition>(DefinitionPath);
            def.TrackId = "dev_greybox";
            def.DisplayName = "Geliştirme Pisti";
            def.Knots = knots;
            def.DefaultWidth = 4f;
            def.LiftStartKnot = liftStart;
            def.LiftEndKnot = 0;
            def.LiftSpeed = 18f;
            def.LiftExitSpeed = 9f;
            def.StartLineOffset = 40f;
            def.CheckpointSpacing = 50f;
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GreyboxBuilder] Descent {descentLength:0} m, lift run {liftRun:0} m, drop {drop:0.0} m (avg grade {drop / descentLength * 100f:0.0}%).");
            return def;
        }

        /// <summary>0..1 height profile with gentle start/end (trapezoidal slope over 25% at each end).</summary>
        private static float EasedClimb(float u)
        {
            const float e = 0.25f;
            float Integral(float x)
            {
                // slope(x) = min(1, x/e, (1-x)/e); integrate piecewise.
                if (x <= e) return x * x / (2f * e);
                if (x <= 1f - e) return e / 2f + (x - e);
                float r = 1f - x;
                return 1f - e - r * r / (2f * e);
            }
            float total = 1f - e;
            return Mathf.Clamp01(Integral(u) / total);
        }
    }
}

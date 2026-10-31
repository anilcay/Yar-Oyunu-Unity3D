using LugeRush.Editor;
using LugeRush.Track;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace LugeRush.Tests
{
    public class CoreDataTests
    {
        private static SledStats Load(SledType t) =>
            AssetDatabase.LoadAssetAtPath<SledStats>($"{SledAssetFactory.StatsFolder}/Sled_{t}.asset");

        [Test]
        public void SledStats_FollowGddRelativeValues()
        {
            var classic = Load(SledType.Classic);
            var sport = Load(SledType.Sport);
            var tandem = Load(SledType.Tandem);
            var mini = Load(SledType.Mini);
            Assert.IsNotNull(classic); Assert.IsNotNull(sport); Assert.IsNotNull(tandem); Assert.IsNotNull(mini);

            Assert.Greater(tandem.MaxSpeed, classic.MaxSpeed, "Tandem fastest on straights");
            Assert.Less(mini.MaxSpeed, classic.MaxSpeed);
            Assert.Greater(sport.GripLateral, classic.GripLateral, "Sport best grip");
            Assert.Greater(mini.SteerRate, sport.SteerRate, "Mini most agile");
            Assert.Greater(tandem.Mass, classic.Mass);
            Assert.Less(mini.Mass, sport.Mass, "Mini lightest");
            Assert.Greater(mini.DriftChargeRate, classic.DriftChargeRate);
        }

        [Test]
        public void DragForTerminalSpeed_GivesRequestedTerminalVelocity()
        {
            const float vMax = 24f, slope = 0.08f, mult = 2.5f, crr = 0.015f;
            float k = SledStats.DragForTerminalSpeed(vMax, slope, mult, crr);
            float sin = slope / Mathf.Sqrt(1f + slope * slope);
            float accelAtVmax = 9.81f * sin * mult - crr * 9.81f - k * vMax * vMax;
            Assert.That(accelAtVmax, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void GreyboxTrack_GeometryIsConsistent()
        {
            var def = AssetDatabase.LoadAssetAtPath<TrackDefinition>(GreyboxBuilder.DefinitionPath);
            if (def == null) Assert.Ignore("Run GreyboxBuilder.Build first");

            var go = new GameObject("TrackTest", typeof(SplineContainer));
            try
            {
                var path = go.AddComponent<TrackPath>();
                path.BuildFromDefinition(def);

                Assert.That(path.Length, Is.InRange(1000f, 1400f), "Loop length ~1.2 km");
                Assert.That(path.LiftLength, Is.InRange(120f, 180f), "Lift ~150 m");
                Assert.IsFalse(path.IsInLift(path.StartLineDistance), "Start line must not be on the lift");
                Assert.GreaterOrEqual(path.CheckpointCount, 15);

                for (int i = 0; i < 60; i++)
                {
                    float d = path.Length * i / 60f + 0.37f;
                    Vector3 p = path.GetPosition(d);
                    float back = path.GetNearestDistance(p, d, 20f);
                    Assert.That(Mathf.Abs(path.SignedDelta(d, back)), Is.LessThan(0.3f), $"nearest-distance round trip at {d:0}");
                    Assert.That(Vector3.Dot(path.GetUp(d), path.GetTangent(d)), Is.EqualTo(0f).Within(0.02f));
                    Assert.That(path.GetWidth(d), Is.InRange(3f, 6f));
                }

                // Descent average grade 6-9 % (GDD).
                float drop = path.GetPosition(path.LiftEnd).y - path.GetPosition(path.LiftStart).y;
                float descent = path.Wrap(path.LiftStart - path.LiftEnd);
                Assert.That(drop / descent, Is.InRange(0.06f, 0.09f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
